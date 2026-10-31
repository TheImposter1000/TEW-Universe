using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TEWUniverse.Studio;

/// <summary>
/// Whole tblCV CSV format. The original record key and source fingerprint
/// protect imported updates from accidental inserts and stale exports.
/// WorkerUID + RecordName identifies the row, not RecordName alone.
/// </summary>
internal sealed record EmploymentCsvChange(
    bool IsAdd, int WorkerUid, string OriginalName,
    WorkerEmploymentDraft Draft, string ExpectedFingerprint);
internal sealed record EmploymentCsvPreview(
    int ExportedRows, IReadOnlyList<EmploymentCsvChange> Changes);

internal static class EmploymentCsvCodec
{
    internal const string Header =
        "Action,WorkerUID,RecordName,OriginalRecordName,CompanyUID,Initials,From,Until,Loan,Trade,Tour,Developmental,SourceFingerprint";

    internal static string Fingerprint(WorkerEmploymentEntry r)
    {
        var canonical = string.Join("\u001f", new[] {
            r.RecordName, r.WorkerUid.ToString(CultureInfo.InvariantCulture),
            r.FedUid.ToString(CultureInfo.InvariantCulture), r.Initials,
            r.FromDate.Ticks.ToString(CultureInfo.InvariantCulture),
            r.UntilDate.Ticks.ToString(CultureInfo.InvariantCulture),
            r.Loan ? "1" : "0", r.Trade ? "1" : "0",
            r.Tour ? "1" : "0", r.Developmental ? "1" : "0"
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string Escape(string raw) =>
        raw.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0
            ? "\"" + raw.Replace("\"", "\"\"") + "\"" : raw;

    internal static string Export(IEnumerable<WorkerEmploymentEntry> records)
    {
        var result = new StringBuilder(Header).AppendLine();
        foreach (var r in records)
        {
            var fields = new[] {
                "Update", r.WorkerUid.ToString(CultureInfo.InvariantCulture),
                r.RecordName, r.RecordName,
                r.FedUid.ToString(CultureInfo.InvariantCulture), r.Initials,
                StudioDateFormat.Format(r.FromDate), StudioDateFormat.Format(r.UntilDate),
                r.Loan ? "True" : "False", r.Trade ? "True" : "False",
                r.Tour ? "True" : "False", r.Developmental ? "True" : "False",
                Fingerprint(r)
            };
            result.AppendLine(string.Join(",", fields.Select(Escape)));
        }
        return result.ToString();
    }

    internal static EmploymentCsvPreview Preview(string csv,
        IReadOnlyList<WorkerEmploymentEntry> existing,
        IReadOnlySet<int> workerIds, IReadOnlySet<int> companyIds)
    {
        if (string.IsNullOrEmpty(csv) || csv.Length > 20_000_000)
            throw new InvalidOperationException("CSV file is empty or exceeds the 20 MB limit.");
        var rows = Parse(csv);
        if (rows.Count == 0 || string.Join(",", rows[0]) != Header)
            throw new InvalidOperationException("CSV headers must match an exported Employment History CSV.");
        var changes = new List<EmploymentCsvChange>();
        var seenSource = new HashSet<(int, string)>();
        for (int rowNo = 1; rowNo < rows.Count; rowNo++)
        {
            var r = rows[rowNo];
            if (r.Count != 13)
                throw new InvalidOperationException($"CSV row {rowNo + 1} must contain 13 columns.");
            bool add = r[0].Equals("Add", StringComparison.OrdinalIgnoreCase);
            bool update = r[0].Equals("Update", StringComparison.OrdinalIgnoreCase);
            if (!add && !update)
                throw new InvalidOperationException($"CSV row {rowNo + 1}: Action must be Add or Update.");
            if (!int.TryParse(r[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int worker) ||
                !workerIds.Contains(worker))
                throw new InvalidOperationException($"CSV row {rowNo + 1}: unknown WorkerUID.");
            if (!int.TryParse(r[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int company) ||
                !companyIds.Contains(company))
                throw new InvalidOperationException($"CSV row {rowNo + 1}: unknown CompanyUID.");
            if (!StudioDateFormat.TryParse(r[6], out var start) ||
                !StudioDateFormat.TryParse(r[7], out var end) ||
                start.Year is < 1800 or > 2100 ||
                (!StudioDateFormat.IsOngoingEndDate(end) &&
                 (end.Year is < 1800 or > 2100 || end < start)))
                throw new InvalidOperationException($"CSV row {rowNo + 1}: invalid employment dates.");
            bool Flag(int n)
            {
                if (bool.TryParse(r[n], out bool b)) return b;
                if (r[n] == "1") return true;
                if (r[n] == "0") return false;
                throw new InvalidOperationException($"CSV row {rowNo + 1}: invalid {Header.Split(',')[n]} flag.");
            }
            string name = r[2].Trim(), oldName = r[3].Trim();
            if (add && (oldName.Length != 0 || r[12].Length != 0))
                throw new InvalidOperationException($"CSV row {rowNo + 1}: Add rows must have blank original name and fingerprint.");
            if (!add && (oldName.Length == 0 || !seenSource.Add((worker, oldName))))
                throw new InvalidOperationException($"CSV row {rowNo + 1}: duplicate or missing original record identity.");
            var draft = new WorkerEmploymentDraft(company, r[5].Trim(),
                start, end, Flag(8), Flag(9), Flag(10), Flag(11), name, name.Length == 0);
            string generated = name.Length == 0
                ? StudioRecordNames.Employment(draft.Initials, start, end) : name;
            if (generated.Length == 0 || generated.Any(char.IsControl))
                throw new InvalidOperationException($"CSV row {rowNo + 1}: invalid RecordName.");
            draft = draft with { RequestedRecordName = generated };
            if (add)
            {
                changes.Add(new EmploymentCsvChange(true, worker, "", draft, ""));
                continue;
            }
            var original = existing.FirstOrDefault(x => x.WorkerUid == worker &&
                string.Equals(x.RecordName, oldName, StringComparison.OrdinalIgnoreCase));
            if (original == null || !Fingerprint(original).Equals(r[12], StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"CSV row {rowNo + 1}: original record was changed or is missing. Export a new CSV.");
            if (original.RecordName == generated && original.FedUid == company &&
                original.Initials == draft.Initials && original.FromDate.Date == start.Date &&
                original.UntilDate.Date == end.Date && original.Loan == draft.Loan &&
                original.Trade == draft.Trade && original.Tour == draft.Tour &&
                original.Developmental == draft.Developmental)
                continue;
            changes.Add(new EmploymentCsvChange(false, worker, original.RecordName,
                draft, r[12]));
        }
        return new EmploymentCsvPreview(rows.Count - 1, changes);
    }

    private static List<List<string>> Parse(string csv)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < csv.Length; i++)
        {
            char c = csv[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < csv.Length && csv[i + 1] == '"')
                { cell.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else cell.Append(c);
            }
            else if (c == '"' && cell.Length == 0) quoted = true;
            else if (c == ',') { row.Add(cell.ToString()); cell.Clear(); }
            else if (c == '\r' || c == '\n')
            {
                if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n') i++;
                row.Add(cell.ToString()); cell.Clear();
                if (row.Count > 1 || row[0].Length > 0) rows.Add(row);
                row = new List<string>();
            }
            else cell.Append(c);
        }
        if (quoted) throw new InvalidOperationException("Unclosed quoted CSV field.");
        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString());
            rows.Add(row);
        }
        return rows;
    }
}
