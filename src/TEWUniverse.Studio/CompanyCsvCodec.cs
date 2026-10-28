using System.Data;
using System.Globalization;
using System.Text;

namespace TEWUniverse.Studio;

internal sealed record CompanyCsvCell(
    int CompanyUid, string CompanyName, string Table, string RecordKey, int RecordId,
    string Field, string Type, string OriginalValue, string Value, bool Editable);

internal sealed record CompanyCsvPreview(
    IReadOnlyList<CompanyFieldChange> Changes, int VerifiedRows);

/// <summary>
/// One record-specific scalar field per CSV row. This makes company exports
/// reversible without inventing a giant column schema or rewriting relations.
/// No add/remove worker or contract operations are represented in this format.
/// </summary>
internal static class CompanyCsvCodec
{
    private static readonly string[] Headers = {
        "CompanyUID", "CompanyName", "Table", "RecordKey", "RecordID",
        "Field", "FieldType", "OriginalValue", "Value", "Editable"
    };

    internal static string Export(IReadOnlyList<CompanyCsvCell> source)
    {
        if (source.Count == 0)
            throw new InvalidOperationException("No company fields are available to export.");
        var sb = new StringBuilder().AppendLine(string.Join(",", Headers));
        foreach (var row in source)
        {
            string[] values = {
                row.CompanyUid.ToString(CultureInfo.InvariantCulture),
                row.CompanyName, row.Table, row.RecordKey,
                row.RecordId.ToString(CultureInfo.InvariantCulture), row.Field,
                row.Type, row.OriginalValue, row.Value,
                row.Editable ? "True" : "False"
            };
            sb.AppendLine(string.Join(",", values.Select(Escape)));
        }
        return sb.ToString();
    }

    internal static CompanyCsvPreview Preview(string csv, IReadOnlyList<CompanyCsvCell> live,
        int selectedCompanyUid)
    {
        if (csv.Length is < 1 or > 2_000_000)
            throw new InvalidOperationException("Company CSV must be between 1 byte and 2 MB.");
        var records = Read(csv);
        if (records.Count < 2 || records.Count > 10001)
            throw new InvalidOperationException("Expected a header and 1 to 10,000 company fields.");
        if (!records[0].SequenceEqual(Headers, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "This is not a TEW Universe Studio company CSV. Keep its exact column headers.");

        static string Key(string table, string recordKey, int recordId, string field) =>
            table.ToUpperInvariant() + "|" + recordKey.ToUpperInvariant() +
            "|" + recordId.ToString(CultureInfo.InvariantCulture) + "|" +
            field.ToUpperInvariant();
        var byKey = live.ToDictionary(x =>
            Key(x.Table, x.RecordKey, x.RecordId, x.Field), StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var updates = new List<CompanyFieldChange>();

        for (int i = 1; i < records.Count; i++)
        {
            var raw = records[i];
            if (raw.Count != Headers.Length)
                throw new InvalidOperationException(
                    $"Company CSV record {i} has {raw.Count} cells; expected {Headers.Length}.");

            if (!int.TryParse(raw[0], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int uid) ||
                !int.TryParse(raw[4], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int recordId) ||
                uid != selectedCompanyUid || recordId <= 0)
                throw new InvalidOperationException(
                    $"Company CSV record {i} is for the wrong company or has an invalid record ID.");

            string key = Key(raw[2], raw[3], recordId, raw[5]);
            if (!seen.Add(key) || !byKey.TryGetValue(key, out var actual))
                throw new InvalidOperationException(
                    $"Company CSV record {i} is duplicated or references a missing/protected record.");
            if (!string.Equals(raw[1], actual.CompanyName, StringComparison.Ordinal) ||
                !string.Equals(raw[6], actual.Type, StringComparison.Ordinal) ||
                !string.Equals(raw[9], actual.Editable ? "True" : "False",
                    StringComparison.OrdinalIgnoreCase) ||
                raw[7] != actual.OriginalValue)
                throw new InvalidOperationException(
                    $"Company CSV record {i} differs from the selected company's current data. " +
                    "Refresh and export a fresh CSV rather than overwriting changed information.");

            if (raw[8] == raw[7]) continue;
            if (!actual.Editable)
                throw new InvalidOperationException(
                    $"Company CSV record {i} tries to change protected {actual.Table}.{actual.Field}.");
            Type fieldType = Type.GetType("System." + actual.Type) ??
                throw new InvalidOperationException("Unsupported CSV field type " + actual.Type);
            updates.Add(new CompanyFieldChange(
                new FieldEdit(actual.Table, actual.RecordKey,
                    actual.Field, fieldType, raw[8]), actual.RecordId));
        }
        return new CompanyCsvPreview(updates, records.Count - 1);
    }

    private static string Escape(string raw) =>
        raw.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ?
            "\"" + raw.Replace("\"", "\"\"") + "\"" : raw;

    private static List<List<string>> Read(string csv)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        bool inQuotes = false, afterQuote = false, atStart = true;
        for (int i = 0; i < csv.Length; i++)
        {
            char c = csv[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < csv.Length && csv[i + 1] == '"')
                    { cell.Append('"'); i++; }
                    else { inQuotes = false; afterQuote = true; }
                }
                else cell.Append(c);
                continue;
            }
            if (c == '"' && atStart)
            { inQuotes = true; atStart = false; }
            else if (c == ',')
            {
                row.Add(cell.ToString());
                cell.Clear(); atStart = true; afterQuote = false;
            }
            else if (c is '\r' or '\n')
            {
                row.Add(cell.ToString());
                if (row.Any(x => x.Length != 0)) rows.Add(row);
                row = new List<string>();
                cell.Clear(); atStart = true; afterQuote = false;
                if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n') i++;
            }
            else if (afterQuote || c == '"')
                throw new InvalidOperationException("Malformed CSV quoting.");
            else { cell.Append(c); atStart = false; }
        }
        if (inQuotes) throw new InvalidOperationException("Unterminated CSV quote.");
        row.Add(cell.ToString());
        if (row.Any(x => x.Length != 0)) rows.Add(row);
        if (rows.Count > 0 && rows[0].Count > 0)
            rows[0][0] = rows[0][0].TrimStart('\uFEFF');
        return rows;
    }
}
