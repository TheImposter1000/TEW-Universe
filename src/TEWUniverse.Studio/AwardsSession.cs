using System.Data;
using System.Data.OleDb;
using System.Globalization;

namespace TEWUniverse.Studio;

internal sealed record AwardField(string Name, Type Type, string StoredValue,
    bool Editable, string DisplayValue);

internal sealed record AwardEntry(int Uid, string Label, string SearchText,
    IReadOnlyList<AwardField> Fields);

internal sealed partial class DatabaseSession
{
    private static string CheckedAwardTable(bool companySpecific) =>
        companySpecific ? AwardsRules.CompanyTable : AwardsRules.GlobalTable;

    /// <summary>
    /// Read actual TEW IX year-end award data. The world and company tables
    /// do not share a presumed row layout; discover each column at runtime.
    /// </summary>
    public List<AwardEntry> ReadAwards(bool companySpecific)
    {
        string table = CheckedAwardTable(companySpecific);
        var schema = ValidateWorkerExtraSchema(table, "UID");
        if (schema.Columns["UID"]!.DataType != typeof(int))
            throw new InvalidOperationException(
                table + " has an unverified UID type. Awards are read-only for safety.");

        using var cmd = new OleDbCommand("SELECT * FROM [" + table + "]", _connection);
        var data = new DataTable(table);
        using (var adapter = new OleDbDataAdapter(cmd))
            adapter.Fill(data);

        var workers = ListWorkers().ToDictionary(w => w.Id, w => w.Name);
        var companies = ListExistingRecords("Companies").ToDictionary(c => c.Id, c => c.Name);
        var output = new List<AwardEntry>();
        foreach (DataRow row in data.Rows)
        {
            if (row["UID"] is DBNull)
                throw new InvalidOperationException(table + " contains a record with no UID.");
            int uid = Convert.ToInt32(row["UID"], CultureInfo.InvariantCulture);
            var fields = data.Columns.Cast<DataColumn>().Select(col =>
            {
                string value = Display(row[col]);
                var reference = "";
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int refId))
                {
                    if (AwardsRules.IsWorkerField(table, col.ColumnName, col.DataType))
                        reference = workers.TryGetValue(refId, out string? workerName)
                            ? "  —  " + workerName : "  —  unlinked worker ID";
                    else if (AwardsRules.IsCompanyField(table, col.ColumnName, col.DataType))
                        reference = companies.TryGetValue(refId, out string? companyName)
                            ? "  —  " + companyName : "  —  unlinked company ID";
                }
                return new AwardField(col.ColumnName, col.DataType, value,
                    AwardsRules.CanEdit(table, col.ColumnName, col.DataType),
                    value + reference);
            }).ToList();

            string year = fields.FirstOrDefault(x =>
                x.Name.Equals("Year", StringComparison.OrdinalIgnoreCase))?.StoredValue ?? "?";
            string name = companySpecific ? "Company award" : "World award";
            // Some company-award databases hold the company link as FedUID;
            // display it only when actually present, never infer a link.
            if (companySpecific)
            {
                var fed = fields.FirstOrDefault(x =>
                    x.Name.Equals("FedUID", StringComparison.OrdinalIgnoreCase) ||
                    x.Name.Equals("Fed", StringComparison.OrdinalIgnoreCase));
                if (fed != null && int.TryParse(fed.StoredValue,
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out int fedId) &&
                    companies.TryGetValue(fedId, out string? company))
                    name = company;
            }
            string label = year == "?" ? name + "  ·  UID " + uid :
                year + "  ·  " + name;
            string search = label + " UID " + uid + " " +
                string.Join(" ", fields.Select(x => x.Name + " " + x.DisplayValue));
            output.Add(new AwardEntry(uid, label, search, fields));
        }
        return output.OrderByDescending(x => x.Fields.FirstOrDefault(f =>
            f.Name.Equals("Year", StringComparison.OrdinalIgnoreCase)) is { } y &&
            int.TryParse(y.StoredValue, out int number) ? number : 0)
            .ThenBy(x => x.Uid).ToList();
    }

    /// <summary>
    /// Only edit positively identified year / winner / company-of-year fields
    /// of an EXISTING award record. No new award years, winner IDs, or
    /// undocumented numeric codes are invented.
    /// </summary>
    public int SaveAward(int workerAwardUid, bool companySpecific,
        AwardEntry original, IReadOnlyDictionary<string, string> edits)
    {
        if (edits.Count == 0) return 0;
        string table = CheckedAwardTable(companySpecific);
        var schema = ValidateWorkerExtraSchema(table, "UID");
        if (schema.Columns["UID"]!.DataType != typeof(int) || workerAwardUid != original.Uid)
            throw new InvalidOperationException("Award record UID is inconsistent. Reload first.");
        var originals = original.Fields.ToDictionary(x => x.Name,
            StringComparer.OrdinalIgnoreCase);
        var typed = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var edit in edits)
        {
            if (!schema.Columns.Contains(edit.Key) ||
                !originals.TryGetValue(edit.Key, out var field) ||
                !field.Editable || field.Type != schema.Columns[edit.Key]!.DataType ||
                !AwardsRules.CanEdit(table, edit.Key, field.Type))
                throw new InvalidOperationException(
                    "Editing " + table + "." + edit.Key +
                    " requires a verified award field mapping.");
            int value = AwardsRules.IsYearField(edit.Key, field.Type)
                ? AwardsRules.ValidateYear(edit.Value)
                : AwardsRules.CanBeUnassigned(table, edit.Key, field.Type)
                    ? AwardsRules.ValidateWinnerId(edit.Value, edit.Key)
                    : AwardsRules.ValidateLinkedId(edit.Value, edit.Key);
            if (value > 0 && AwardsRules.IsWorkerField(table, edit.Key, field.Type))
                CheckLinkedWorker(value);
            if (value > 0 && AwardsRules.IsCompanyField(table, edit.Key, field.Type) &&
                !ListExistingRecords("Companies").Any(x => x.Id == value))
                throw new InvalidOperationException("The chosen company UID does not exist.");
            typed.Add(edit.Key, Convert.ChangeType(value, field.Type, CultureInfo.InvariantCulture));
        }

        EnsureUnchanged();
        using var tx = _connection.BeginTransaction();
        try
        {
            using (var read = new OleDbCommand(
                "SELECT * FROM [" + table + "] WHERE [UID]=?", _connection, tx))
            {
                read.Parameters.Add("uid", OleDbType.Integer).Value = workerAwardUid;
                using var reader = read.ExecuteReader();
                if (reader == null || !reader.Read())
                    throw new InvalidOperationException(
                        "Award record is missing or has been changed. Reload.");
                foreach (var field in original.Fields)
                {
                    string current = reader.IsDBNull(reader.GetOrdinal(field.Name)) ? "" :
                        Display(reader.GetValue(reader.GetOrdinal(field.Name)));
                    if (current != field.StoredValue)
                        throw new InvalidOperationException(
                            "Award field " + field.Name + " changed outside this form. Reload.");
                }
                if (reader.Read())
                    throw new InvalidOperationException("Duplicate award record UID. Write blocked.");
            }

            if (!companySpecific && typed.TryGetValue("Year", out var nextYear) &&
                !Equals(nextYear, null))
            {
                using var unique = new OleDbCommand(
                    "SELECT COUNT(*) FROM [tblEOYAward] WHERE [Year]=? AND [UID]<>?",
                    _connection, tx);
                unique.Parameters.Add("year", ToOleDbType(schema.Columns["Year"]!.DataType)).Value =
                    nextYear;
                unique.Parameters.Add("uid", OleDbType.Integer).Value = workerAwardUid;
                if (Convert.ToInt32(unique.ExecuteScalar(), CultureInfo.InvariantCulture) != 0)
                    throw new InvalidOperationException(
                        "This global awards year already exists. Duplicate year blocked.");
            }

            string columns = string.Join(", ", typed.Keys.Select(x => "[" + x + "]=?"));
            using var update = new OleDbCommand(
                "UPDATE [" + table + "] SET " + columns + " WHERE [UID]=?",
                _connection, tx);
            foreach (var edit in typed)
                update.Parameters.Add(edit.Key,
                    ToOleDbType(schema.Columns[edit.Key]!.DataType)).Value = edit.Value;
            update.Parameters.Add("uid", OleDbType.Integer).Value = workerAwardUid;
            if (update.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("Award row changed during saving. Rolled back.");
            tx.Commit();
            _openedStamp = GetStamp(PathOnDisk);
            return typed.Count;
        }
        catch
        {
            try { tx.Rollback(); } catch { }
            throw;
        }
    }
}
