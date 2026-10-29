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
            Type type = schema.Columns[change.Key]!.DataType;
            int id = change.Key.Equals("Year", StringComparison.OrdinalIgnoreCase)
                ? AwardsRules.ValidateYear(change.Value)
                : AwardsRules.CanBeUnassigned(table, change.Key, type)
                    ? AwardsRules.ValidateWinnerId(change.Value, change.Key)
                    : AwardsRules.ValidateLinkedId(change.Value, change.Key);
            if (id > 0 && AwardsRules.IsWorkerField(table, change.Key, type))
                CheckLinkedWorker(id);
            if (id > 0 && AwardsRules.IsCompanyField(table, change.Key, type) &&
                !ListExistingRecords("Companies").Any(x => x.Id == id))
                throw new InvalidOperationException("The selected company UID is missing.");
        }
        // Award Recordnames use the promotion's actual stored initials,
        // not its full name, label or numeric database ID.
        if (companySpecific)
        {
            var companySchema = ValidateWorkerExtraSchema(
                "tblFed", "UID", "Initials");
            if (companySchema.Columns["UID"]!.DataType != typeof(int) ||
                companySchema.Columns["Initials"]!.DataType != typeof(string))
                throw new InvalidOperationException(
                    "The company's UID / Initials schema is unsupported.");
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
                    // Generate this after all fields, including the chosen
                    // promotion, have been populated from the NEW record.
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
                    int parsed = AwardsRules.CanBeUnassigned(table, col.ColumnName,
                        col.DataType)
                        ? AwardsRules.ValidateWinnerId(requested, col.ColumnName)
                        : AwardsRules.ValidateLinkedId(requested, col.ColumnName);
                    values[col.ColumnName] =
                        Convert.ChangeType(parsed, col.DataType, CultureInfo.InvariantCulture);
                }
                else if (AwardsRules.CanBeUnassigned(table, col.ColumnName, col.DataType))
                {
                    // Do NOT clone last year's winners into a newly-created year.
                    values[col.ColumnName] =
                        Convert.ChangeType(0, col.DataType, CultureInfo.InvariantCulture);
                }
                else if (col.ColumnName is "Match" or "Match_At" or
                         "Match_Rating" or "Card" or "Card_Rating")
                {
                    // Match/show-of-year details are year-specific too. A
                    // newly-created year must not inherit last year's match,
                    // show, or score. Use neutral values only for known types.
                    values[col.ColumnName] = col.DataType == typeof(string)
                        ? ""
                        : col.DataType == typeof(int) ||
                          col.DataType == typeof(short) ||
                          col.DataType == typeof(long) ||
                          col.DataType == typeof(double) ||
                          col.DataType == typeof(float) ||
                          col.DataType == typeof(decimal)
                          ? Convert.ChangeType(0, col.DataType, CultureInfo.InvariantCulture)
                          : row[col.ColumnName];
                }
                else values[col.ColumnName] = row[col.ColumnName];

                if (values[col.ColumnName] is DBNull && !col.AllowDBNull)
                    throw new InvalidOperationException(
                        "Required award field " + col.ColumnName + " is missing.");
            }

            // TEW-friendly company awards use initials + year (e.g. DNA 2022);
            // world awards retain their separate World Awards + year format.
            // The same promotion can have several categories in one year,
            // so an occupied name receives a readable #2 / #3 suffix.
            var recordCol = schema.Columns.Cast<DataColumn>().FirstOrDefault(c =>
                c.ColumnName.Equals("Recordname", StringComparison.OrdinalIgnoreCase));
            if (recordCol != null)
            {
                var reserved = new List<string>();
                using (var namesRead = new OleDbCommand(
                    "SELECT [" + recordCol.ColumnName + "] FROM [" + table + "]",
                    _connection, tx))
                using (var reader = namesRead.ExecuteReader())
                    while (reader?.Read() == true)
                        if (!reader.IsDBNull(0))
                            reserved.Add(Convert.ToString(reader.GetValue(0),
                                CultureInfo.InvariantCulture) ?? "");

                int maxLength = recordCol.MaxLength;
                string readable;
                if (companySpecific)
                {
                    var companyCol = schema.Columns.Cast<DataColumn>().FirstOrDefault(c =>
                        c.ColumnName.Equals("FedUID", StringComparison.OrdinalIgnoreCase))
                        ?? schema.Columns.Cast<DataColumn>().FirstOrDefault(c =>
                        c.ColumnName.Equals("Fed", StringComparison.OrdinalIgnoreCase));
                    if (companyCol == null ||
                        !AwardsRules.IsCompanyField(table, companyCol.ColumnName,
                            companyCol.DataType) ||
                        !values.TryGetValue(companyCol.ColumnName, out object? companyValue) ||
                        companyValue is null or DBNull)
                        throw new InvalidOperationException(
                            "Company award has no verified promotion. Creation blocked.");
                    int companyId = Convert.ToInt32(companyValue, CultureInfo.InvariantCulture);
                    string initials;
                    using (var companyRead = new OleDbCommand(
                        "SELECT [Initials] FROM [tblFed] WHERE [UID]=?",
                        _connection, tx))
                    {
                        companyRead.Parameters.Add("uid", OleDbType.Integer)
                            .Value = companyId;
                        object? found = companyRead.ExecuteScalar();
                        if (found is null or DBNull)
                            throw new InvalidOperationException(
                                "The selected promotion has no verified initials.");
                        initials = Convert.ToString(found,
                            CultureInfo.InvariantCulture) ?? "";
                    }
                    readable = StudioRecordNames.Award(
                        initials, year, reserved, maxLength);
                }
                else
                    readable = StudioRecordNames.WorldAward(year, reserved, maxLength);
                values[recordCol.ColumnName] = readable;
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
