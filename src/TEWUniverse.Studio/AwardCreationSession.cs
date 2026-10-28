using System.Data;
using System.Data.OleDb;
using System.Globalization;

namespace TEWUniverse.Studio;

internal sealed partial class DatabaseSession
{
    /// <summary>
    /// Create an award from a REAL row of the SAME table. All unverified
    /// required values are copied, not guessed, and only verified winner/year
    /// values can be overridden. The original record is never modified.
    /// </summary>
    public int CreateAwardFromTemplate(bool companySpecific, AwardEntry template,
        string newYear, IReadOnlyDictionary<string, string> changes)
    {
        string table = CheckedAwardTable(companySpecific);
        var schema = ValidateWorkerExtraSchema(table, "UID");
        if (schema.Columns["UID"]!.DataType != typeof(int) ||
            !schema.Columns.Contains("Year") ||
            !AwardsRules.IsYearField("Year", schema.Columns["Year"]!.DataType))
            throw new InvalidOperationException(
                "This awards table does not expose verified UID and Year fields. Creation blocked.");
        int year = AwardsRules.ValidateYear(newYear);
        var original = template.Fields.ToDictionary(f => f.Name,
            StringComparer.OrdinalIgnoreCase);
        if (original.Count != schema.Columns.Count || schema.Columns.Cast<DataColumn>()
            .Any(col => !original.TryGetValue(col.ColumnName, out var snapshot) ||
                snapshot.Type != col.DataType))
            throw new InvalidOperationException("The awards template schema has changed. Reload.");
        foreach (var change in changes)
        {
            if (!schema.Columns.Contains(change.Key) ||
                !AwardsRules.CanEdit(table, change.Key, schema.Columns[change.Key]!.DataType))
                throw new InvalidOperationException(
                    "The award field " + change.Key + " has no verified mapping.");
            int id = change.Key.Equals("Year", StringComparison.OrdinalIgnoreCase)
                ? AwardsRules.ValidateYear(change.Value)
                : AwardsRules.ValidateLinkedId(change.Value, change.Key);
            if (AwardsRules.IsWorkerField(table, change.Key,
                schema.Columns[change.Key]!.DataType))
                CheckLinkedWorker(id);
            if (AwardsRules.IsCompanyField(table, change.Key,
                schema.Columns[change.Key]!.DataType) &&
                !ListExistingRecords("Companies").Any(x => x.Id == id))
                throw new InvalidOperationException("The selected company UID is missing.");
        }
        // The newly created row always uses the NEW YEAR, never the
        // original template's Year, regardless of uncommitted form edits.
        EnsureUnchanged();
        using var tx = _connection.BeginTransaction();
        try
        {
            var source = new DataTable();
            using (var command = new OleDbCommand(
                "SELECT * FROM [" + table + "] WHERE [UID]=?", _connection, tx))
            {
                command.Parameters.Add("uid", OleDbType.Integer).Value = template.Uid;
                using var adapter = new OleDbDataAdapter(command);
                adapter.Fill(source);
            }
            if (source.Rows.Count != 1)
                throw new InvalidOperationException("Award template record changed or is missing. Reload.");
            var row = source.Rows[0];
            foreach (var field in template.Fields)
                if (Display(row[field.Name]) != field.StoredValue)
                    throw new InvalidOperationException(
                        "Template field " + field.Name + " changed outside Studio. Reload.");

            // Prevent duplicate world award years, where one record contains
            // multiple award winners. Company rows can be category-specific;
            // do not incorrectly prohibit multiple awards in the same year.
            if (!companySpecific)
            {
                using var unique = new OleDbCommand(
                    "SELECT COUNT(*) FROM [tblEOYAward] WHERE [Year]=?",
                    _connection, tx);
                unique.Parameters.Add("year",
                    ToOleDbType(schema.Columns["Year"]!.DataType)).Value =
                    Convert.ChangeType(year, schema.Columns["Year"]!.DataType,
                        CultureInfo.InvariantCulture);
                if (Convert.ToInt32(unique.ExecuteScalar(),
                    CultureInfo.InvariantCulture) != 0)
                    throw new InvalidOperationException(
                        "A world year-end award row already exists for " + year + ".");
            }

            int newUid;
            using (var max = new OleDbCommand(
                "SELECT MAX([UID]) FROM [" + table + "]", _connection, tx))
            {
                object? found = max.ExecuteScalar();
                newUid = checked((found is null or DBNull ? 0 :
                    Convert.ToInt32(found, CultureInfo.InvariantCulture)) + 1);
            }

            var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (DataColumn col in schema.Columns)
            {
                if (col.ColumnName.Equals("UID", StringComparison.OrdinalIgnoreCase))
                {
                    if (!col.AutoIncrement) values[col.ColumnName] = newUid;
                    continue;
                }
                if (col.ColumnName.Equals("Recordname", StringComparison.OrdinalIgnoreCase) ||
                    col.ColumnName.Equals("RecordName", StringComparison.OrdinalIgnoreCase))
                {
                    if (col.DataType != typeof(string))
                        throw new InvalidOperationException("Unverified award Recordname type.");
                    string value = "TUSAW" + newUid.ToString("D8", CultureInfo.InvariantCulture);
                    if (col.MaxLength > 0 && value.Length > col.MaxLength)
                        throw new InvalidOperationException(
                            "Award Recordname is too short to create a safe unique identity.");
                    values[col.ColumnName] = value;
                    continue;
                }
                if (col.ColumnName.Equals("Year", StringComparison.OrdinalIgnoreCase))
                {
                    values[col.ColumnName] =
                        Convert.ChangeType(year, col.DataType, CultureInfo.InvariantCulture);
                    continue;
                }
                if (changes.TryGetValue(col.ColumnName, out string? requested))
                {
                    int parsed = AwardsRules.ValidateLinkedId(requested, col.ColumnName);
                    values[col.ColumnName] =
                        Convert.ChangeType(parsed, col.DataType, CultureInfo.InvariantCulture);
                }
                else values[col.ColumnName] = row[col.ColumnName];

                if (values[col.ColumnName] is DBNull && !col.AllowDBNull)
                    throw new InvalidOperationException(
                        "Required award field " + col.ColumnName + " is missing.");
            }

            // Check that the new Recordname is not already in use.
            var recordCol = schema.Columns.Cast<DataColumn>().FirstOrDefault(c =>
                c.ColumnName.Equals("Recordname", StringComparison.OrdinalIgnoreCase));
            if (recordCol != null)
            {
                using var nameCheck = new OleDbCommand(
                    "SELECT COUNT(*) FROM [" + table + "] WHERE [" +
                    recordCol.ColumnName + "]=?", _connection, tx);
                nameCheck.Parameters.Add("name", OleDbType.VarWChar).Value =
                    values[recordCol.ColumnName];
                if (Convert.ToInt32(nameCheck.ExecuteScalar(),
                    CultureInfo.InvariantCulture) != 0)
                    throw new InvalidOperationException("Generated award Recordname already exists.");
            }

            var names = values.Keys.ToArray();
            using var insert = new OleDbCommand(
                "INSERT INTO [" + table + "] (" +
                string.Join(",", names.Select(x => "[" + x + "]")) +
                ") VALUES (" + string.Join(",", names.Select(_ => "?")) + ")",
                _connection, tx);
            foreach (string name in names)
                insert.Parameters.Add(name, ToOleDbType(schema.Columns[name]!.DataType))
                    .Value = values[name];
            if (insert.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("Award row insert failed.");
            if (schema.Columns["UID"]!.AutoIncrement)
            {
                using var identity = new OleDbCommand("SELECT @@IDENTITY", _connection, tx);
                newUid = Convert.ToInt32(identity.ExecuteScalar(),
                    CultureInfo.InvariantCulture);
            }
            tx.Commit();
            _openedStamp = GetStamp(PathOnDisk);
            return newUid;
        }
        catch
        {
            try { tx.Rollback(); } catch { }
            throw;
        }
    }
}
