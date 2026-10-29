using System.Globalization;
using System.Text;

namespace TEWUniverse.Studio;

// Generic record-scoped CSV. Future record-backed sections only need to
// supply a snapshot of verified fields and a validated save adapter.
internal sealed record StudioCsvField(
    string Section, int RecordUid, string RecordName, string Table,
    string KeyColumn, int RowUid, string Field, string FieldType,
    string OriginalValue, string Value, bool Editable);

internal sealed record StudioCsvChange(StudioCsvField Field, string NewValue);

internal sealed record StudioCsvPreview(
    IReadOnlyList<StudioCsvChange> Changes, int VerifiedFields);

internal static class StudioSectionCsvCodec
{
    private static readonly string[] Headers = {
        "Section", "RecordUID", "RecordName", "Table", "KeyColumn",
        "RowUID", "Field", "FieldType", "OriginalValue", "Value", "Editable"
    };

    internal static string Export(IReadOnlyList<StudioCsvField> fields)
    {
        if (fields.Count == 0)
            throw new InvalidOperationException("No fields are available for this record.");
        var csv = new StringBuilder().AppendLine(string.Join(",", Headers));
        foreach (var f in fields)
        {
            string[] values = {
                f.Section, f.RecordUid.ToString(CultureInfo.InvariantCulture),
                f.RecordName, f.Table, f.KeyColumn,
                f.RowUid.ToString(CultureInfo.InvariantCulture), f.Field,
                f.FieldType, f.OriginalValue, f.Value,
                f.Editable ? "True" : "False"
            };
            csv.AppendLine(string.Join(",", values.Select(Escape)));
        }
        return csv.ToString();
    }

    internal static StudioCsvPreview Preview(string csv,
        IReadOnlyList<StudioCsvField> current, string section, int uid)
    {
        if (csv.Length is < 1 or > 4_000_000)
            throw new InvalidOperationException("CSV must be between 1 byte and 4 MB.");
        if (current.Count == 0 || uid <= 0 || section == "Workers" ||
            current.Any(x => x.Section != section || x.RecordUid != uid))
            throw new InvalidOperationException("An existing non-worker record is required.");
        var rows = Parse(csv);
        if (rows.Count is < 2 or > 20001 ||
            !rows[0].SequenceEqual(Headers, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Expected an exported Studio section CSV with its original headers.");

        static string Key(string table, string keyColumn, int rowUid, string field) =>
            table.ToUpperInvariant() + "|" + keyColumn.ToUpperInvariant() +
            "|" + rowUid.ToString(CultureInfo.InvariantCulture) +
            "|" + field.ToUpperInvariant();
        var lookup = current.ToDictionary(f =>
            Key(f.Table, f.KeyColumn, f.RowUid, f.Field), StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var changes = new List<StudioCsvChange>();
        for (int i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Count != Headers.Length ||
                row[0] != section ||
                !int.TryParse(row[1], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int recordUid) ||
                !int.TryParse(row[5], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int rowUid) ||
                recordUid != uid)
                throw new InvalidOperationException(
                    $"CSV row {i} is not a valid {section} record UID {uid}.");

            string key = Key(row[3], row[4], rowUid, row[6]);
            if (!seen.Add(key) || !lookup.TryGetValue(key, out var field) ||
                row[2] != field.RecordName ||
                row[7] != field.FieldType ||
                row[8] != field.OriginalValue ||
                !string.Equals(row[10], field.Editable ? "True" : "False",
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"CSV row {i} has a stale value, unexpected field or mismatched record identity. " +
                    "Export a new CSV from the selected record.");

            if (row[9] == row[8]) continue;
            if (!field.Editable)
                throw new InvalidOperationException(
                    $"CSV row {i} attempts to change protected {field.Table}.{field.Field}.");
            changes.Add(new StudioCsvChange(field, row[9]));
        }
        return new StudioCsvPreview(changes, rows.Count - 1);
    }

    private static string Escape(string text) =>
        text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0
            ? text : "\"" + text.Replace("\"", "\"\"") + "\"";

    private static List<List<string>> Parse(string csv)
    {
        var records = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool quoted = false, closed = false, started = false;
        for (int i = 0; i < csv.Length; i++)
        {
            char c = csv[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < csv.Length && csv[i + 1] == '"')
                { field.Append('"'); i++; }
                else if (c == '"') { quoted = false; closed = true; }
                else field.Append(c);
            }
            else if (c == '"' && !started && !closed)
            { quoted = true; started = true; }
            else if (c == ',')
            {
                row.Add(field.ToString()); field.Clear();
                started = false; closed = false;
            }
            else if (c is '\r' or '\n')
            {
                row.Add(field.ToString());
                if (row.Any(x => x.Length > 0)) records.Add(row);
                row = new List<string>(); field.Clear();
                started = false; closed = false;
                if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n') i++;
            }
            else if (closed || c == '"')
                throw new InvalidOperationException("Malformed CSV quoting.");
            else { field.Append(c); started = true; }
        }
        if (quoted) throw new InvalidOperationException("Unclosed CSV quoted field.");
        row.Add(field.ToString());
        if (row.Any(x => x.Length > 0)) records.Add(row);
        if (records.Count > 0 && records[0].Count > 0)
            records[0][0] = records[0][0].TrimStart('\uFEFF');
        return records;
    }
}
