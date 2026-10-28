using System.Data;
using System.Data.OleDb;
using System.Globalization;

namespace TEWUniverse.Studio;

internal sealed record CompanyRelatedSection(
    string Table, string Label, DataTable Rows,
    string Explanation, bool Truncated);

internal sealed record CompanyFieldChange(FieldEdit Edit, int RecordId);

internal sealed partial class DatabaseSession
{
    /// <summary>
    /// Discover the linked records from each verified company table. All
    /// joins must actually expose FedUID; never guess from a numeric UID.
    /// Entries are capped to avoid loading unbounded regional schedules.
    /// </summary>
    public IReadOnlyList<CompanyRelatedSection> ReadCompanyRelated(int companyUid)
    {
        if (companyUid <= 0 || !ReadSection("tblFed", companyUid).Exists)
            throw new InvalidOperationException("Select an existing company.");

        var result = new List<CompanyRelatedSection>();
        foreach (var (table, label) in CompanyFieldRules.RelatedTables)
        {
            try
            {
                var schema = new DataTable();
                using (var query = new OleDbCommand(
                    $"SELECT * FROM [{table}] WHERE 1=0", _connection))
                using (var adapter = new OleDbDataAdapter(query))
                    adapter.FillSchema(schema, SchemaType.Source);

                if (!schema.Columns.Contains("FedUID"))
                {
                    result.Add(new CompanyRelatedSection(table, label,
                        new DataTable(table), "No verified FedUID link. This table is not editable here.",
                        false));
                    continue;
                }

                var rows = new DataTable(table);
                using (var query = new OleDbCommand(
                    $"SELECT TOP 201 * FROM [{table}] WHERE [FedUID]=?", _connection))
                {
                    query.Parameters.Add("company", OleDbType.Integer).Value = companyUid;
                    using var adapter = new OleDbDataAdapter(query);
                    adapter.Fill(rows);
                }
                bool truncated = rows.Rows.Count > 200;
                if (truncated)
                    while (rows.Rows.Count > 200)
                        rows.Rows.RemoveAt(rows.Rows.Count - 1);
                result.Add(new CompanyRelatedSection(table, label, rows,
                    rows.Rows.Count == 0 ? "No records linked to this company." :
                    truncated ? "First 200 linked records shown. Additional records are not loaded." :
                    "Records linked by the verified FedUID column.",
                    truncated));
            }
            catch (OleDbException ex)
            {
                result.Add(new CompanyRelatedSection(table, label,
                    new DataTable(table),
                    "This optional company table cannot be read: " + ex.Message,
                    false));
            }
        }
        return result;
    }

    /// <summary>
    /// All edits are applied in one transaction. Each related row is checked
    /// against the selected company's FedUID immediately before writing.
    /// </summary>
    public int SaveCompanyFields(int companyUid, IReadOnlyList<CompanyFieldChange> changes)
    {
        if (companyUid <= 0 || changes.Count == 0) return 0;
        var primary = ReadSection("tblFed", companyUid);
        if (!primary.Exists || primary.Data.Rows.Count != 1)
            throw new InvalidOperationException("The company no longer exists.");

        var baseEdits = changes.Where(x =>
            x.Edit.Table.Equals("tblFed", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Edit).ToList();
        if (baseEdits.Any(x =>
            x.Field is "OpenYear" or "OpenMonth" or "ClosedYear" or "ClosedMonth"))
            ValidateCompanyDates(primary.Data.Rows[0], baseEdits);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Validate the full request BEFORE opening the transaction.
        foreach (var change in changes)
        {
            var edit = change.Edit;
            if (change.RecordId <= 0 ||
                !(edit.Table.Equals("tblFed", StringComparison.OrdinalIgnoreCase) ||
                  CompanyFieldRules.IsRelatedTable(edit.Table)) ||
                !edit.Key.Equals("UID", StringComparison.OrdinalIgnoreCase) &&
                !edit.Key.Equals("FedUID", StringComparison.OrdinalIgnoreCase) ||
                edit.Table.Equals("tblFed", StringComparison.OrdinalIgnoreCase) &&
                (edit.Key != "UID" || change.RecordId != companyUid))
                throw new InvalidOperationException("Unverified company record identity.");

            if (!seen.Add(edit.Table + "|" + change.RecordId + "|" + edit.Field))
                throw new InvalidOperationException("A company column was submitted twice.");

            using var query = new OleDbCommand(
                $"SELECT * FROM [{edit.Table}] WHERE [{edit.Key}]=?", _connection);
            query.Parameters.Add("id", OleDbType.Integer).Value = change.RecordId;
            using var adapter = new OleDbDataAdapter(query);
            var current = new DataTable();
            adapter.Fill(current);
            if (current.Rows.Count != 1 || !current.Columns.Contains(edit.Field))
                throw new InvalidOperationException(
                    "The company-linked record is missing or its identity is ambiguous.");
            if (!edit.Table.Equals("tblFed", StringComparison.OrdinalIgnoreCase))
            {
                if (!current.Columns.Contains("FedUID") ||
                    current.Rows[0]["FedUID"] is DBNull ||
                    Convert.ToInt32(current.Rows[0]["FedUID"], CultureInfo.InvariantCulture)
                        != companyUid)
                    throw new InvalidOperationException(
                        "Company-related record belongs to a different company.");
            }
            DataColumn column = current.Columns[edit.Field]!;
            if (!CompanyFieldRules.CanEdit(column) || column.DataType != edit.FieldType)
                throw new InvalidOperationException(
                    "Company field " + edit.Table + "." + edit.Field +
                    " is a protected link or unsupported type.");
        }

        EnsureUnchanged();
        using var tx = _connection.BeginTransaction();
        try
        {
            int applied = 0;
            foreach (var change in changes)
            {
                var edit = change.Edit;
                // The verified FedUID condition makes the row update
                // company-specific even when UID is the related table key.
                bool related = !edit.Table.Equals("tblFed", StringComparison.OrdinalIgnoreCase);
                string sql = $"UPDATE [{edit.Table}] SET [{edit.Field}]=? " +
                    $"WHERE [{edit.Key}]=?" +
                    (related && edit.Key.Equals("UID", StringComparison.OrdinalIgnoreCase)
                        ? " AND [FedUID]=?" : "");
                using var command = new OleDbCommand(sql, _connection, tx);
                command.Parameters.Add("value", ToOleDbType(edit.FieldType)).Value =
                    Parse(edit.NewText, edit.FieldType);
                command.Parameters.Add("record", OleDbType.Integer).Value = change.RecordId;
                if (related && edit.Key.Equals("UID", StringComparison.OrdinalIgnoreCase))
                    command.Parameters.Add("company", OleDbType.Integer).Value = companyUid;
                if (command.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException(
                        "Company record changed during save. Entire transaction cancelled.");
                applied++;
            }
            tx.Commit();
            _openedStamp = GetStamp(PathOnDisk);
            return applied;
        }
        catch
        {
            try { tx.Rollback(); } catch { }
            throw;
        }
    }
}
