using System.Data;
using System.Data.OleDb;
using System.Globalization;

namespace TEWUniverse.Studio;

internal sealed record WorkerContractTemplate(int Uid, int SourceWorkerId,
    int CompanyId, string Label, IReadOnlyList<WorkerContractField> Fields);

internal sealed partial class DatabaseSession
{
    /// <summary>
    /// Read a small set of REAL contract templates from the requested company.
    /// No undocumented contract defaults are generated or assumed.
    /// </summary>
    public List<WorkerContractTemplate> ReadContractTemplates(int companyId)
    {
        if (companyId < 1 || !ListExistingRecords("Companies").Any(c => c.Id == companyId))
            throw new InvalidOperationException("Select a valid company.");
        var schema = ValidateWorkerExtraSchema("tblContract", "WorkerUID");
        foreach (string name in new[] { "UID", "Recordname", "WorkerUID", "FedUID" })
            if (!schema.Columns.Contains(name))
                throw new InvalidOperationException(
                    "Contract creation needs a verified " + name + " column.");
        if (schema.Columns["UID"]!.DataType != typeof(int) ||
            schema.Columns["WorkerUID"]!.DataType != typeof(int) ||
            schema.Columns["FedUID"]!.DataType != typeof(int))
            throw new InvalidOperationException("Contract UID, WorkerUID and FedUID must be integers.");

        using var command = new OleDbCommand(
            "SELECT TOP 25 * FROM [tblContract] WHERE [FedUID]=? ORDER BY [UID]",
            _connection);
        command.Parameters.Add("fed", OleDbType.Integer).Value = companyId;
        var data = new DataTable();
        using (var adapter = new OleDbDataAdapter(command))
            adapter.Fill(data);
        var names = ListWorkers().ToDictionary(w => w.Id, w => w.Name);
        var templates = new List<WorkerContractTemplate>();
        foreach (DataRow row in data.Rows)
        {
            int uid = Convert.ToInt32(row["UID"], CultureInfo.InvariantCulture);
            int source = Convert.ToInt32(row["WorkerUID"], CultureInfo.InvariantCulture);
            var fields = data.Columns.Cast<DataColumn>().Select(column =>
                new WorkerContractField(column.ColumnName, column.DataType,
                    Display(row[column]),
                    IsEditableContractColumn(schema.Columns[column.ColumnName]!),
                    column.AllowDBNull, column.MaxLength)).ToList();
            templates.Add(new WorkerContractTemplate(uid, source, companyId,
                (names.TryGetValue(source, out string? name) ? name : $"Worker #{source}") +
                $"  |  Template contract UID {uid}", fields));
        }
        return templates;
    }

    /// <summary>
    /// Create a new live contract from a verified, same-company template. Copies
    /// the template's actual stored field values, then applies user-edited terms.
    /// This never touches tblCV and never creates a worker or company.
    /// </summary>
    public int CreateWorkerContract(int targetWorkerId, WorkerContractTemplate template,
        IReadOnlyDictionary<string, string> requestedTerms)
    {
        CheckLinkedWorker(targetWorkerId);
        var schema = ValidateWorkerExtraSchema("tblContract", "WorkerUID");
        if (!schema.Columns.Contains("UID") || !schema.Columns.Contains("Recordname") ||
            !schema.Columns.Contains("FedUID") ||
            schema.Columns["UID"]!.DataType != typeof(int) ||
            schema.Columns["Recordname"]!.DataType != typeof(string))
            throw new InvalidOperationException("Unsupported contract identity schema.");
        var company = ListExistingRecords("Companies")
            .FirstOrDefault(c => c.Id == template.CompanyId);
        if (template.CompanyId < 1 || company == null)
            throw new InvalidOperationException("Contract company no longer exists.");
        var worker = ListWorkers().FirstOrDefault(w => w.Id == targetWorkerId);
        if (worker == null)
            throw new InvalidOperationException("Contract worker no longer exists.");
        var sourceFields = template.Fields.ToDictionary(f => f.Name,
            StringComparer.OrdinalIgnoreCase);
        if (sourceFields.Count != schema.Columns.Count)
            throw new InvalidOperationException("Contract template schema changed.");
        foreach (DataColumn column in schema.Columns)
            if (!sourceFields.TryGetValue(column.ColumnName, out var original) ||
                original.Type != column.DataType)
                throw new InvalidOperationException("Contract template schema changed.");
        foreach (var term in requestedTerms)
        {
            if (!schema.Columns.Contains(term.Key) ||
                !IsEditableContractColumn(schema.Columns[term.Key]!))
                throw new InvalidOperationException("Unsupported contract term: " + term.Key);
            var column = schema.Columns[term.Key]!;
            if (term.Value.Length == 0 && !column.AllowDBNull)
                throw new InvalidOperationException(term.Key + " cannot be blank.");
            if (column.MaxLength > 0 && term.Value.Length > column.MaxLength)
                throw new InvalidOperationException(term.Key + " exceeds its text limit.");
        }

        EnsureUnchanged();
        using var tx = _connection.BeginTransaction();
        try
        {
            var source = new DataTable();
            using (var select = new OleDbCommand(
                "SELECT * FROM [tblContract] WHERE [UID]=? AND [WorkerUID]=? AND [FedUID]=?",
                _connection, tx))
            {
                select.Parameters.Add("uid", OleDbType.Integer).Value = template.Uid;
                select.Parameters.Add("worker", OleDbType.Integer).Value = template.SourceWorkerId;
                select.Parameters.Add("fed", OleDbType.Integer).Value = template.CompanyId;
                using var adapter = new OleDbDataAdapter(select);
                adapter.Fill(source);
            }
            if (source.Rows.Count != 1)
                throw new InvalidOperationException("The chosen template contract has changed. Reload.");
            var sourceRow = source.Rows[0];
            foreach (var old in template.Fields)
            {
                if (!string.Equals(Display(sourceRow[old.Name]), old.Value, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "The template's " + old.Name + " changed. Reload before creating.");
            }

            using (var duplicate = new OleDbCommand(
                "SELECT COUNT(*) FROM [tblContract] WHERE [WorkerUID]=? AND [FedUID]=?",
                _connection, tx))
            {
                duplicate.Parameters.Add("worker", OleDbType.Integer).Value = targetWorkerId;
                duplicate.Parameters.Add("fed", OleDbType.Integer).Value = template.CompanyId;
                if (Convert.ToInt32(duplicate.ExecuteScalar(), CultureInfo.InvariantCulture) != 0)
                    throw new InvalidOperationException(
                        "This worker already has a contract with the chosen company. " +
                        "Edit the existing contract instead.");
            }
            int proposedUid;
            using (var max = new OleDbCommand(
                "SELECT MAX([UID]) FROM [tblContract]", _connection, tx))
            {
                object? value = max.ExecuteScalar();
                proposedUid = checked((value is DBNull or null ? 0 :
                    Convert.ToInt32(value, CultureInfo.InvariantCulture)) + 1);
            }
            if (proposedUid < 1) throw new InvalidOperationException("Cannot allocate a contract UID.");
            var reservedContractNames = new List<string>();
            using (var namesRead = new OleDbCommand(
                "SELECT [Recordname] FROM [tblContract]", _connection, tx))
            using (var reader = namesRead.ExecuteReader())
                while (reader?.Read() == true)
                    if (!reader.IsDBNull(0))
                        reservedContractNames.Add(Convert.ToString(reader.GetValue(0),
                            CultureInfo.InvariantCulture) ?? "");
            int maxName = schema.Columns["Recordname"]!.MaxLength;
            string recordName = StudioRecordNames.Contract(
                worker.Name, company.Name, reservedContractNames, maxName);

            var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (DataColumn column in schema.Columns)
            {
                if (column.ColumnName.Equals("UID", StringComparison.OrdinalIgnoreCase))
                {
                    if (!column.AutoIncrement) values[column.ColumnName] = proposedUid;
                }
                else if (column.ColumnName.Equals("Recordname", StringComparison.OrdinalIgnoreCase))
                    values[column.ColumnName] = recordName;
                else if (column.ColumnName.Equals("WorkerUID", StringComparison.OrdinalIgnoreCase))
                    values[column.ColumnName] = targetWorkerId;
                else if (column.ColumnName.Equals("FedUID", StringComparison.OrdinalIgnoreCase))
                    values[column.ColumnName] = template.CompanyId;
                else if (requestedTerms.TryGetValue(column.ColumnName, out string? typed))
                {
                    object parsed;
                    try { parsed = Parse(typed, column.DataType); }
                    catch (Exception ex) when (ex is FormatException or OverflowException)
                    {
                        throw new InvalidOperationException(
                            $"Invalid {column.ColumnName} value. Dates use MM/DD/YYYY.", ex);
                    }
                    values[column.ColumnName] = parsed;
                }
                else values[column.ColumnName] = sourceRow[column.ColumnName];

                if (values.TryGetValue(column.ColumnName, out object? candidate))
                {
                    if (candidate is DBNull && !column.AllowDBNull)
                        throw new InvalidOperationException(
                            "Required contract field is null: " + column.ColumnName);
                    if (column.MaxLength > 0 && candidate is string str &&
                        str.Length > column.MaxLength)
                        throw new InvalidOperationException(
                            "Contract field exceeds its MDB limit: " + column.ColumnName);
                }
            }

            var fields = values.Keys.ToArray();
            using var insert = new OleDbCommand(
                "INSERT INTO [tblContract] (" +
                string.Join(",", fields.Select(c => $"[{c}]")) + ") VALUES (" +
                string.Join(",", fields.Select(_ => "?")) + ")", _connection, tx);
            foreach (var field in fields)
                insert.Parameters.Add(field,
                    ToOleDbType(schema.Columns[field]!.DataType)).Value = values[field];
            if (insert.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("The new contract was not inserted.");
            int id = proposedUid;
            if (schema.Columns["UID"]!.AutoIncrement)
            {
                using var identity = new OleDbCommand("SELECT @@IDENTITY", _connection, tx);
                id = Convert.ToInt32(identity.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
            tx.Commit();
            _openedStamp = GetStamp(PathOnDisk);
            return id;
        }
        catch
        {
            try { tx.Rollback(); } catch { }
            throw;
        }
    }
}
