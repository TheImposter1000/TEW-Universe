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
    WorkerEmploymentDraft Draft, string ExpectedFingerprint, bool IsDelete = false,
    WorkerEmploymentEntry? Original = null);
internal sealed record EmploymentCsvPreview(
    int ExportedRows, IReadOnlyList<EmploymentCsvChange> Changes,
    bool IsReplacement = false);

internal static class EmploymentCsvCodec
{
    internal const string Header =
        "Action,WorkerUID,RecordName,OriginalRecordName,CompanyUID,Initials,From,Until,Loan,Trade,Tour,Developmental,SourceFingerprint";
    internal const string SimpleHeader =
        "WorkerUID,RecordName,CompanyUID,Initials,From,Until,Loan,Trade,Tour,Developmental";

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

    // Standard Championship-style snapshot export. Every CSV row represents
    // one period. Removing a row requests deletion; adding a row requests
    // insertion. The original snapshot is re-read at preview and apply.
    internal static string ExportSimple(IEnumerable<WorkerEmploymentEntry> records)
    {
        var csv = new StringBuilder(SimpleHeader).AppendLine();
        foreach (var r in records)
        {
            var fields = new[] {
                r.WorkerUid.ToString(CultureInfo.InvariantCulture),
                r.RecordName, r.FedUid.ToString(CultureInfo.InvariantCulture),
                r.Initials, StudioDateFormat.Format(r.FromDate),
                StudioDateFormat.Format(r.UntilDate),
                r.Loan ? "True" : "False", r.Trade ? "True" : "False",
                r.Tour ? "True" : "False", r.Developmental ? "True" : "False"
            };
            csv.AppendLine(string.Join(",", fields.Select(Escape)));
        }
        return csv.ToString();
    }

    private static WorkerEmploymentDraft ParseDraft(
        IReadOnlyList<string> fields, int rowNumber, int companyIndex,
        int initialsIndex, int fromIndex, int untilIndex, int flagStart,
        string recordName, IReadOnlySet<int> companies)
    {
        if (!int.TryParse(fields[companyIndex].Trim(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int company) ||
            company < 0 || (company != 0 && !companies.Contains(company)))
            throw new InvalidOperationException(
                $"CSV row {rowNumber}: CompanyUID '{fields[companyIndex]}' is invalid. " +
                "Use 0 for an unlisted historical company, or a positive UID from tblFed.");
        if (!StudioDateFormat.TryParse(fields[fromIndex], out var start) ||
            !StudioDateFormat.TryParse(fields[untilIndex], out var end) ||
            start.Year is < 1800 or > 2100 ||
            (!StudioDateFormat.IsOngoingEndDate(end) &&
                (end.Year is < 1800 or > 2100 || end < start)))
            throw new InvalidOperationException(
                $"CSV row {rowNumber}: invalid From / Until date. Use 1/1/1666 only for an ongoing Until date.");
        bool ReadFlag(int n)
        {
            string v = fields[n].Trim();
            if (bool.TryParse(v, out bool b)) return b;
            if (v == "1" || v == "-1") return true;
            if (v == "0") return false;
            throw new InvalidOperationException(
                $"CSV row {rowNumber}: invalid {SimpleHeader.Split(',')[6 + n - flagStart]} value '{v}'.");
        }
        string initials = fields[initialsIndex].Trim();
        if (initials.Length == 0 || initials.Any(char.IsControl))
            throw new InvalidOperationException(
                $"CSV row {rowNumber}: enter valid company initials.");
        string name = recordName.Trim();
        if (name.Length == 0)
            name = StudioRecordNames.Employment(initials, start, end);
        if (name.Any(char.IsControl))
            throw new InvalidOperationException(
                $"CSV row {rowNumber}: RecordName cannot contain control characters.");
        return new WorkerEmploymentDraft(company, initials, start, end,
            ReadFlag(flagStart), ReadFlag(flagStart + 1),
            ReadFlag(flagStart + 2), ReadFlag(flagStart + 3), name, false);
    }

    private static bool Equal(WorkerEmploymentEntry old, WorkerEmploymentDraft draft) =>
        old.RecordName == draft.RequestedRecordName &&
        old.FedUid == draft.FedUid && old.Initials == draft.Initials &&
        old.FromDate.Date == draft.FromDate.Date &&
        old.UntilDate.Date == draft.UntilDate.Date &&
        old.Loan == draft.Loan && old.Trade == draft.Trade &&
        old.Tour == draft.Tour && old.Developmental == draft.Developmental;

    private static bool SameRow(WorkerEmploymentEntry original, WorkerEmploymentEntry desired) =>
        original.WorkerUid == desired.WorkerUid &&
        original.FedUid == desired.FedUid &&
        original.FromDate.Date == desired.FromDate.Date &&
        original.UntilDate.Date == desired.UntilDate.Date &&
        string.Equals(original.RecordName, desired.RecordName, StringComparison.OrdinalIgnoreCase) &&
        original.Initials == desired.Initials && original.Loan == desired.Loan &&
        original.Trade == desired.Trade && original.Tour == desired.Tour &&
        original.Developmental == desired.Developmental;

    private static WorkerEmploymentDraft AsDraft(WorkerEmploymentEntry row) =>
        new(row.FedUid, row.Initials, row.FromDate, row.UntilDate, row.Loan,
            row.Trade, row.Tour, row.Developmental, row.RecordName);

    internal static EmploymentCsvPreview PreviewReplacement(
        string csv, IReadOnlyList<WorkerEmploymentEntry> existing,
        IReadOnlySet<int> workerIds, IReadOnlySet<int> companyIds)
    {
        if (string.IsNullOrEmpty(csv) || csv.Length > 20_000_000)
            throw new InvalidOperationException("CSV file is empty or exceeds 20 MB.");
        var rows = Parse(csv);
        if (rows.Count == 0 ||
            string.Join(",", rows[0]).TrimStart('\ufeff') != SimpleHeader)
            throw new InvalidOperationException(
                "Expected an Employment History CSV exported with the current header.");

        var desired = new List<WorkerEmploymentEntry>();
        for (int i = 1; i < rows.Count; i++)
        {
            var r = rows[i]; int rowNo = i + 1;
            if (r.Count != 10)
                throw new InvalidOperationException(
                    $"CSV row {rowNo}: expected 10 columns, received {r.Count}.");
            if (!int.TryParse(r[0].Trim(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int worker) ||
                !workerIds.Contains(worker))
                throw new InvalidOperationException(
                    $"CSV row {rowNo}: WorkerUID '{r[0]}' does not identify a worker.");
            var d = ParseDraft(r, rowNo, 2, 3, 4, 5, 6, r[1], companyIds);
            desired.Add(new WorkerEmploymentEntry(d.RequestedRecordName!, worker,
                d.FedUid, d.Initials, d.FromDate, d.UntilDate,
                d.Loan, d.Trade, d.Tour, d.Developmental));
        }

        // Do not introduce an additional physically indistinguishable row:
        // such a row could not later be edited or deleted safely. Existing
        // exact duplicates remain readable and are not treated as corruption.
        foreach (var group in desired.GroupBy(Fingerprint))
        {
            int alreadyStored = existing.Count(x =>
                Fingerprint(x) == group.Key);
            if (group.Count() > 1 && group.Count() > alreadyStored)
                throw new InvalidOperationException(
                    $"Worker {group.First().WorkerUid}: CSV introduces identical " +
                    "employment rows (same company, exact dates, name and flags). " +
                    "Separate handshake stints must have distinct date ranges.");
        }

        // RecordName is display text, not a unique identifier. Match complete
        // unchanged rows first; separate handshake stints in the same year
        // are valid even when they have identical display names.
        var unmatched = existing.ToList();
        var outstanding = new List<WorkerEmploymentEntry>();
        foreach (var row in desired)
        {
            int index = unmatched.FindIndex(old => SameRow(old, row));
            if (index >= 0) unmatched.RemoveAt(index);
            else outstanding.Add(row);
        }

        var changes = new List<EmploymentCsvChange>();
        // Prefer exact period (worker, company, full dates), so changing flags
        // or historical initials does not delete and recreate the period.
        // A date/name change can also be paired if exactly one old row remains.
        foreach (var row in outstanding)
        {
            var candidates = unmatched
                .Where(old => old.WorkerUid == row.WorkerUid &&
                    old.FedUid == row.FedUid &&
                    old.FromDate.Date == row.FromDate.Date &&
                    old.UntilDate.Date == row.UntilDate.Date).ToList();
            if (candidates.Count != 1)
                candidates = unmatched.Where(old =>
                    old.WorkerUid == row.WorkerUid &&
                    old.FedUid == row.FedUid &&
                    string.Equals(old.RecordName, row.RecordName,
                        StringComparison.OrdinalIgnoreCase)).ToList();
            if (candidates.Count != 1)
                candidates = unmatched.Where(old =>
                    old.WorkerUid == row.WorkerUid &&
                    string.Equals(old.RecordName, row.RecordName,
                        StringComparison.OrdinalIgnoreCase)).ToList();
            if (candidates.Count != 1)
                candidates = unmatched.Where(old =>
                    old.WorkerUid == row.WorkerUid &&
                    old.FromDate.Date == row.FromDate.Date &&
                    old.UntilDate.Date == row.UntilDate.Date).ToList();
            if (candidates.Count == 1)
            {
                var original = candidates[0];
                unmatched.Remove(original);
                changes.Add(new EmploymentCsvChange(false, row.WorkerUid,
                    original.RecordName, AsDraft(row), Fingerprint(original),
                    Original: original));
            }
            else
                changes.Add(new EmploymentCsvChange(true, row.WorkerUid, "",
                    AsDraft(row), ""));
        }
        foreach (var old in unmatched)
            changes.Add(new EmploymentCsvChange(false, old.WorkerUid,
                old.RecordName, AsDraft(old), Fingerprint(old), IsDelete: true,
                Original: old));
        return new EmploymentCsvPreview(rows.Count - 1, changes,
            IsReplacement: true);
    }

    internal static bool IsLegacyFormat(string csv)
    {
        var first = csv.Replace("\r", "").Split('\n', 2)[0].TrimStart('\ufeff');
        return first == Header;
    }

    /// <summary>
    /// Allows an already-edited old-format CSV to work as a full-table
    /// snapshot, disregarding bookkeeping identity/fingerprint columns.
    /// Explicit legacy Action=Delete rows are omitted from the desired final
    /// history. The UI must ask permission before using this mode, because
    /// absent rows will be deleted from tblCV.
    /// </summary>
    internal static EmploymentCsvPreview PreviewLegacyReplacement(
        string csv, IReadOnlyList<WorkerEmploymentEntry> existing,
        IReadOnlySet<int> workerIds, IReadOnlySet<int> companyIds)
    {
        if (string.IsNullOrEmpty(csv) || csv.Length > 20_000_000)
            throw new InvalidOperationException("CSV file is empty or exceeds 20 MB.");
        var rows = Parse(csv);
        if (rows.Count == 0 ||
            string.Join(",", rows[0]).TrimStart('\ufeff') != Header)
            throw new InvalidOperationException("The selected file is not an older Employment History CSV.");
        var simple = new StringBuilder(SimpleHeader).AppendLine();
        for (int i = 1; i < rows.Count; i++)
        {
            var fields = rows[i];
            if (fields.Count != 13)
                throw new InvalidOperationException(
                    $"CSV row {i + 1}: legacy spreadsheet needs 13 columns.");
            string action = fields[0].Trim();
            if (action.Equals("Delete", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!action.Equals("Update", StringComparison.OrdinalIgnoreCase) &&
                !action.Equals("Add", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"CSV row {i + 1}: Action must be Add, Update or Delete.");
            int[] source = { 1, 2, 4, 5, 6, 7, 8, 9, 10, 11 };
            simple.AppendLine(string.Join(",", source.Select(index => Escape(fields[index]))));
        }
        return PreviewReplacement(simple.ToString(), existing, workerIds, companyIds);
    }

    internal static EmploymentCsvPreview Preview(string csv,
        IReadOnlyList<WorkerEmploymentEntry> existing,
        IReadOnlySet<int> workerIds, IReadOnlySet<int> companyIds)
    {
        if (string.IsNullOrEmpty(csv) || csv.Length > 20_000_000)
            throw new InvalidOperationException("CSV file is empty or exceeds the 20 MB limit.");
        var rows = Parse(csv);
        if (rows.Count == 0)
            throw new InvalidOperationException("The CSV has no header.");
        string header = string.Join(",", rows[0]).TrimStart('\ufeff');
        if (header == SimpleHeader)
            return PreviewReplacement(csv, existing, workerIds, companyIds);
        if (header != Header)
            throw new InvalidOperationException(
                "Unrecognized Employment History CSV columns. Use Export All CSV for the straightforward table format.");
        var changes = new List<EmploymentCsvChange>();
        var seenSource = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int rowNo = 1; rowNo < rows.Count; rowNo++)
        {
            var r = rows[rowNo];
            if (r.Count != 13)
                throw new InvalidOperationException($"CSV row {rowNo + 1} must contain 13 columns.");
            bool add = r[0].Equals("Add", StringComparison.OrdinalIgnoreCase);
            bool update = r[0].Equals("Update", StringComparison.OrdinalIgnoreCase);
            bool delete = r[0].Equals("Delete", StringComparison.OrdinalIgnoreCase);
            if (!add && !update && !delete)
                throw new InvalidOperationException($"CSV row {rowNo + 1}: Action must be Add, Update or Delete.");
            if (!int.TryParse(r[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int worker) ||
                !workerIds.Contains(worker))
                throw new InvalidOperationException($"CSV row {rowNo + 1}: unknown WorkerUID.");
            if (!int.TryParse(r[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int company) ||
                (company < 0 || (company != 0 && !companyIds.Contains(company))))
                throw new InvalidOperationException(
                    $"CSV row {rowNo + 1}: CompanyUID must be 0 for an unlisted " +
                    "company or an existing positive company UID.");
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
            if (!add && oldName.Length == 0)
                throw new InvalidOperationException(
                    $"CSV row {rowNo + 1}: OriginalRecordName is blank for WorkerUID {worker}. " +
                    "For a new period, set Action to Add and clear SourceFingerprint, " +
                    "or export a fresh standard CSV to add and remove rows directly.");
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
            var candidates = existing.Where(x => x.WorkerUid == worker &&
                string.Equals(x.RecordName, oldName, StringComparison.OrdinalIgnoreCase) &&
                Fingerprint(x).Equals(r[12], StringComparison.OrdinalIgnoreCase)).ToList();
            if (candidates.Count != 1)
                throw new InvalidOperationException(
                    $"CSV row {rowNo + 1}: worker {worker}, record '{oldName}' is not " +
                    "identified by one original row fingerprint. Re-export or use full-table CSV.");
            var original = candidates[0];
            if (!seenSource.Add(Fingerprint(original)))
                throw new InvalidOperationException(
                    $"CSV row {rowNo + 1}: original period for worker {worker}, " +
                    $"'{oldName}' is referenced twice. Use Action=Add for a new period.");

            if (delete)
            {
                changes.Add(new EmploymentCsvChange(false, worker, original.RecordName,
                    draft, r[12], IsDelete: true, Original: original));
                continue;
            }
            if (original.RecordName == generated && original.FedUid == company &&
                original.Initials == draft.Initials && original.FromDate.Date == start.Date &&
                original.UntilDate.Date == end.Date && original.Loan == draft.Loan &&
                original.Trade == draft.Trade && original.Tour == draft.Tour &&
                original.Developmental == draft.Developmental)
                continue;
            changes.Add(new EmploymentCsvChange(false, worker, original.RecordName,
                draft, r[12], Original: original));
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
