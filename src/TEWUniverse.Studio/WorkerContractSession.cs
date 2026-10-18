using System.Data;
using System.Data.OleDb;
using System.Globalization;

namespace TEWUniverse.Studio;

internal sealed record WorkerContractField(
    string Name, Type Type, string Value, bool CanEdit, bool AllowNull, int MaxLength);

internal sealed record WorkerContractEntry(
    string KeyColumn, object Key, string Label, string Company,
    IReadOnlyList<WorkerContractField> Fields);

/// <summary>
/// The live tblContract schema differs from tblCV and is inspected at runtime.
/// Only fields that can be safely round-tripped are editable, and updates
/// always identify both the selected contract and its worker.
/// </summary>
internal sealed partial class DatabaseSession
{
    private static string ContractKeyColumn(DataTable schema)
    {
        if (schema.Columns.Contains("UID") &&
            schema.Columns["UID"]!.DataType == typeof(int)) return "UID";
        if (schema.Columns.Contains("Recordname") &&
            schema.Columns["Recordname"]!.DataType == typeof(string)) return "Recordname";
        if (schema.Columns.Contains("RecordName") &&
            schema.Columns["RecordName"]!.DataType == typeof(string)) return "RecordName";
        throw new InvalidOperationException(
            "tblContract has no verified UID or Recordname identity. Editing is disabled.");
    }

    private static bool IsEditableContractColumn(DataColumn col)
    {
        string name = col.ColumnName;
        return IsEditable(col.DataType) && !col.AutoIncrement &&
            !col.ReadOnly && string.IsNullOrWhiteSpace(col.Expression) &&
            name is not ("UID" or "WorkerUID" or "Recordname" or "RecordName" or "FedUID") &&
            !name.EndsWith("UID", StringComparison.OrdinalIgnoreCase) &&
            !name.EndsWith("_ID", StringComparison.OrdinalIgnoreCase);
    }

    public List<WorkerContractEntry> ReadWorkerContracts(int workerId)
    {
        CheckLinkedWorker(workerId);
        var schema = ValidateWorkerExtraSchema("tblContract", "WorkerUID");
        string key = ContractKeyColumn(schema);
        if (schema.Columns["WorkerUID"]!.DataType != typeof(int))
            throw new InvalidOperationException("tblContract.WorkerUID is not the expected integer link.");

        var companies = ListExistingRecords("Companies").ToDictionary(c => c.Id, c => c.Name);
        using var command = new OleDbCommand(
            "SELECT * FROM [tblContract] WHERE [WorkerUID]=?", _connection);
        command.Parameters.Add("worker", OleDbType.Integer).Value = workerId;
        var rows = new DataTable("WorkerContracts");
        using (var adapter = new OleDbDataAdapter(command))
            adapter.Fill(rows);
        var output = new List<WorkerContractEntry>();
        foreach (DataRow row in rows.Rows)
        {
            if (row[key] is DBNull)
                throw new InvalidOperationException("Found a contract without a verified record identity.");
            object id = row[key];
            int companyUid = rows.Columns.Contains("FedUID") && row["FedUID"] is not DBNull
                ? Convert.ToInt32(row["FedUID"], CultureInfo.InvariantCulture) : -1;
            string company = companies.TryGetValue(companyUid, out string? label)
                ? label : companyUid > 0 ? $"Company UID {companyUid}" : "Company not recorded";
            string keyText = Convert.ToString(id, CultureInfo.InvariantCulture) ?? "(unnamed)";
            var fields = rows.Columns.Cast<DataColumn>().Select(col =>
                new WorkerContractField(col.ColumnName, col.DataType, Display(row[col]),
                    IsEditableContractColumn(schema.Columns[col.ColumnName]!), col.AllowDBNull,
                    col.MaxLength)).ToList();
            output.Add(new WorkerContractEntry(key, id,
                $"Contract {keyText}", company, fields));
        }
        return output;
    }

    public int SaveWorkerContract(int workerId, WorkerContractEntry original,
        IReadOnlyDictionary<string, string> changedFields)
    {
        CheckLinkedWorker(workerId);
        if (changedFields.Count == 0) return 0;
        var schema = ValidateWorkerExtraSchema("tblContract", "WorkerUID");
        string key = ContractKeyColumn(schema);
        if (!key.Equals(original.KeyColumn, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The active contract schema changed. Reload the worker.");
        var originalFields = original.Fields.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var change in changedFields)
        {
            if (!schema.Columns.Contains(change.Key) ||
                !originalFields.TryGetValue(change.Key, out var snapshot) ||
                !IsEditableContractColumn(schema.Columns[change.Key]!) ||
                snapshot.Type != schema.Columns[change.Key]!.DataType)
                throw new InvalidOperationException(
                    $"Contract field {change.Key} is unsupported or protected from editing.");
            var col = schema.Columns[change.Key]!;
            if (change.Value.Length == 0 && !col.AllowDBNull)
                throw new InvalidOperationException($"{change.Key} is required and cannot be blank.");
            if (col.MaxLength > 0 && change.Value.Length > col.MaxLength)
                throw new InvalidOperationException(
                    $"{change.Key} exceeds the MDB limit of {col.MaxLength} characters.");
        }

        EnsureUnchanged();
        using var transaction = _connection.BeginTransaction();
        try
        {
            string sql = $"SELECT * FROM [tblContract] WHERE [WorkerUID]=? AND [{key}]=?";
            using (var verify = new OleDbCommand(sql, _connection, transaction))
            {
                verify.Parameters.Add("worker", OleDbType.Integer).Value = workerId;
                verify.Parameters.Add("key", ToOleDbType(schema.Columns[key]!.DataType))
                    .Value = original.Key;
                using var reader = verify.ExecuteReader();
                if (reader == null || !reader.Read())
                    throw new InvalidOperationException(
                        "The selected contract no longer belongs to this worker. Reload.");
                // Compare the full original snapshot before touching a value.
                foreach (var field in original.Fields)
                {
                    int index = reader.GetOrdinal(field.Name);
                    string current = reader.IsDBNull(index) ? "" : Display(reader.GetValue(index));
                    if (!string.Equals(current, field.Value, StringComparison.Ordinal))
                        throw new InvalidOperationException(
                            $"The selected contract's {field.Name} has changed. Reload before editing.");
                }
                if (reader.Read())
                    throw new InvalidOperationException(
                        "Contract identity matches multiple records. No edits were made.");
            }

            int updated = 0;
            foreach (var edit in changedFields)
            {
                var col = schema.Columns[edit.Key]!;
                object value;
                try { value = Parse(edit.Value, col.DataType); }
                catch (Exception ex) when (ex is FormatException or OverflowException)
                {
                    throw new InvalidOperationException(
                        $"Invalid value for {edit.Key}. Dates require MM/DD/YYYY; " +
                        "other fields must use their MDB type.", ex);
                }
                using var command = new OleDbCommand(
                    $"UPDATE [tblContract] SET [{edit.Key}]=? " +
                    $"WHERE [WorkerUID]=? AND [{key}]=?", _connection, transaction);
                command.Parameters.Add("value", ToOleDbType(col.DataType)).Value = value;
                command.Parameters.Add("worker", OleDbType.Integer).Value = workerId;
                command.Parameters.Add("key", ToOleDbType(schema.Columns[key]!.DataType))
                    .Value = original.Key;
                if (command.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException(
                        "The contract changed during saving. Transaction cancelled.");
                updated++;
            }
            transaction.Commit();
            _openedStamp = GetStamp(PathOnDisk);
            return updated;
        }
        catch
        {
            try { transaction.Rollback(); } catch { }
            throw;
        }
    }
}
