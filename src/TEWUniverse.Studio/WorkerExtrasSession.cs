using System.Data;
using System.Data.OleDb;
using System.Globalization;

namespace TEWUniverse.Studio;

// These are actual TEW IX pregame MDB records. Attributes are not profile
// scalars, and tblCV employment history is not the current tblContract roster.
internal sealed record WorkerAttributeEntry(
    int Uid, int WorkerUid, string Attribute, bool Hidden, bool Permanent);

internal sealed record WorkerEmploymentEntry(
    string RecordName, int WorkerUid, int FedUid, string Initials,
    DateTime FromDate, DateTime UntilDate,
    bool Loan, bool Trade, bool Tour, bool Developmental);

internal sealed record WorkerEmploymentDraft(
    int FedUid, string Initials, DateTime FromDate, DateTime UntilDate,
    bool Loan, bool Trade, bool Tour, bool Developmental);

internal sealed partial class DatabaseSession
{
    private DataTable ValidateWorkerExtraSchema(string table, params string[] requiredColumns)
    {
        var schema = new DataTable(table);
        using var command = new OleDbCommand($"SELECT * FROM [{table}] WHERE 1=0", _connection);
        using var adapter = new OleDbDataAdapter(command);
        adapter.FillSchema(schema, SchemaType.Source);
        var missing = requiredColumns.Where(name => !schema.Columns.Contains(name)).ToArray();
        if (missing.Length != 0)
            throw new InvalidOperationException(
                $"This TEW IX MDB does not support the verified {table} layout. Missing: " +
                string.Join(", ", missing) + ". No records were modified.");
        return schema;
    }

    private void CheckLinkedWorker(int workerId)
    {
        if (workerId <= 0)
            throw new InvalidOperationException("Select an existing worker first.");
        using var cmd = new OleDbCommand(
            "SELECT COUNT(*) FROM [tblWorker] WHERE [UID]=?", _connection);
        cmd.Parameters.Add("worker", OleDbType.Integer).Value = workerId;
        if (Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) != 1)
            throw new InvalidOperationException("The selected worker UID is not in this MDB.");
    }

    private void CheckLinkedCompany(int companyId)
    {
        if (companyId <= 0)
            throw new InvalidOperationException("Choose a linked existing company for this employment period.");
        using var cmd = new OleDbCommand(
            "SELECT COUNT(*) FROM [tblFed] WHERE [UID]=?", _connection);
        cmd.Parameters.Add("company", OleDbType.Integer).Value = companyId;
        if (Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) != 1)
            throw new InvalidOperationException("The employment company UID is not in tblFed.");
    }

    private static bool Flag(object value) =>
        value != DBNull.Value && Convert.ToBoolean(value, CultureInfo.InvariantCulture);

    private static string FieldText(object value) =>
        value == DBNull.Value ? "" :
        (Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");

    public List<WorkerAttributeEntry> ReadWorkerAttributes(int workerId)
    {
        CheckLinkedWorker(workerId);
        ValidateWorkerExtraSchema("tblAttribute",
            "UID", "WorkerUID", "Attribute", "Hidden", "Permanent");
        using var cmd = new OleDbCommand(
            "SELECT [UID], [WorkerUID], [Attribute], [Hidden], [Permanent] " +
            "FROM [tblAttribute] WHERE [WorkerUID]=? ORDER BY [Attribute]", _connection);
        cmd.Parameters.Add("worker", OleDbType.Integer).Value = workerId;
        using var reader = cmd.ExecuteReader();
        var result = new List<WorkerAttributeEntry>();
        if (reader == null) return result;
        while (reader.Read())
            result.Add(new WorkerAttributeEntry(
                Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture),
                Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture),
                FieldText(reader.GetValue(2)), Flag(reader.GetValue(3)), Flag(reader.GetValue(4))));
        return result;
    }

    public List<string> ListKnownWorkerAttributes()
    {
        ValidateWorkerExtraSchema("tblAttribute", "Attribute");
        using var cmd = new OleDbCommand(
            "SELECT DISTINCT [Attribute] FROM [tblAttribute] " +
            "WHERE [Attribute] IS NOT NULL ORDER BY [Attribute]", _connection);
        using var reader = cmd.ExecuteReader();
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        if (reader == null) return new();
        while (reader.Read())
        {
            string name = FieldText(reader.GetValue(0)).Trim();
            if (name.Length > 0) names.Add(name);
        }
        return names.ToList();
    }

    /// <summary>
    /// Inserts or updates a single worker attribute; never changes any other
    /// worker's attributes. The user-facing form confirms writes beforehand.
    /// </summary>
    public int SaveWorkerAttribute(int workerId, int? entryUid,
        string attribute, bool hidden, bool permanent)
    {
        CheckLinkedWorker(workerId);
        string name = attribute.Trim();
        if (name.Length == 0 || name.Any(char.IsControl))
            throw new InvalidOperationException("Choose a valid TEW IX attribute.");
        var schema = ValidateWorkerExtraSchema("tblAttribute",
            "UID", "WorkerUID", "Attribute", "Hidden", "Permanent");
        if (schema.Columns["Attribute"]!.DataType != typeof(string))
            throw new InvalidOperationException(
                "Unexpected tblAttribute.Attribute type. Changes are blocked.");
        if (schema.Columns["UID"]!.DataType != typeof(int) ||
            schema.Columns["WorkerUID"]!.DataType != typeof(int))
            throw new InvalidOperationException(
                "Unexpected tblAttribute identifier types. Changes are blocked.");
        foreach (string flag in new[] { "Hidden", "Permanent" })
            if (schema.Columns[flag]!.DataType != typeof(bool))
                throw new InvalidOperationException(
                    $"Unexpected tblAttribute.{flag} type. Changes are blocked.");
        if (schema.Columns["Attribute"]!.MaxLength > 0 &&
            name.Length > schema.Columns["Attribute"]!.MaxLength)
            throw new InvalidOperationException("This attribute exceeds its MDB text limit.");

        var supplied = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "UID", "WorkerUID", "Attribute", "Hidden", "Permanent"
        };
        foreach (DataColumn col in schema.Columns)
            if (!supplied.Contains(col.ColumnName) && !col.AutoIncrement &&
                !col.AllowDBNull && col.DefaultValue is DBNull)
                throw new InvalidOperationException(
                    $"Unverified mandatory attribute field {col.ColumnName}; no changes made.");

        EnsureUnchanged();
        using var tx = _connection.BeginTransaction();
        try
        {
            using (var dup = new OleDbCommand(
                "SELECT COUNT(*) FROM [tblAttribute] WHERE [WorkerUID]=? " +
                "AND [Attribute]=? AND [UID]<>?", _connection, tx))
            {
                dup.Parameters.Add("worker", OleDbType.Integer).Value = workerId;
                dup.Parameters.Add("name", OleDbType.VarWChar).Value = name;
                dup.Parameters.Add("uid", OleDbType.Integer).Value = entryUid ?? -1;
                if (Convert.ToInt32(dup.ExecuteScalar(), CultureInfo.InvariantCulture) > 0)
                    throw new InvalidOperationException(
                        "This worker already has the selected attribute.");
            }

            int uid;
            if (entryUid.HasValue)
            {
                uid = entryUid.Value;
                using var update = new OleDbCommand(
                    "UPDATE [tblAttribute] SET [Attribute]=?, [Hidden]=?, [Permanent]=? " +
                    "WHERE [UID]=? AND [WorkerUID]=?", _connection, tx);
                update.Parameters.Add("name", OleDbType.VarWChar).Value = name;
                update.Parameters.Add("hidden", OleDbType.Boolean).Value = hidden;
                update.Parameters.Add("permanent", OleDbType.Boolean).Value = permanent;
                update.Parameters.Add("uid", OleDbType.Integer).Value = uid;
                update.Parameters.Add("worker", OleDbType.Integer).Value = workerId;
                if (update.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException(
                        "The attribute changed since it was selected. No changes saved.");
            }
            else
            {
                using var allocate = new OleDbCommand(
                    "SELECT MAX([UID]) FROM [tblAttribute]", _connection, tx);
                var max = allocate.ExecuteScalar();
                uid = checked((max is null or DBNull ? 0 :
                    Convert.ToInt32(max, CultureInfo.InvariantCulture)) + 1);
                if (uid < 1) throw new InvalidOperationException("Could not allocate an attribute UID.");

                bool autoId = schema.Columns["UID"]!.AutoIncrement;
                string columns = autoId
                    ? "[WorkerUID],[Attribute],[Hidden],[Permanent]"
                    : "[UID],[WorkerUID],[Attribute],[Hidden],[Permanent]";
                string placeholders = autoId ? "?,?,?,?" : "?,?,?,?,?";
                using var insert = new OleDbCommand(
                    $"INSERT INTO [tblAttribute] ({columns}) VALUES ({placeholders})",
                    _connection, tx);
                if (!autoId)
                    insert.Parameters.Add("uid", OleDbType.Integer).Value = uid;
                insert.Parameters.Add("worker", OleDbType.Integer).Value = workerId;
                insert.Parameters.Add("name", OleDbType.VarWChar).Value = name;
                insert.Parameters.Add("hidden", OleDbType.Boolean).Value = hidden;
                insert.Parameters.Add("permanent", OleDbType.Boolean).Value = permanent;
                if (insert.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException("The new attribute was not inserted.");
                if (autoId)
                {
                    using var identity = new OleDbCommand("SELECT @@IDENTITY", _connection, tx);
                    uid = Convert.ToInt32(identity.ExecuteScalar(), CultureInfo.InvariantCulture);
                }
            }
            tx.Commit();
            _openedStamp = GetStamp(PathOnDisk);
            return uid;
        }
        catch
        {
            try { tx.Rollback(); } catch { /* keep original failure */ }
            throw;
        }
    }

    public void DeleteWorkerAttribute(int workerId, int uid)
    {
        CheckLinkedWorker(workerId);
        if (uid <= 0) throw new InvalidOperationException("Select an existing attribute.");
        ValidateWorkerExtraSchema("tblAttribute", "UID", "WorkerUID");
        EnsureUnchanged();
        using var tx = _connection.BeginTransaction();
        try
        {
            using var cmd = new OleDbCommand(
                "DELETE FROM [tblAttribute] WHERE [UID]=? AND [WorkerUID]=?", _connection, tx);
            cmd.Parameters.Add("uid", OleDbType.Integer).Value = uid;
            cmd.Parameters.Add("worker", OleDbType.Integer).Value = workerId;
            if (cmd.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("The selected attribute no longer exists.");
            tx.Commit();
            _openedStamp = GetStamp(PathOnDisk);
        }
        catch
        {
            try { tx.Rollback(); } catch { }
            throw;
        }
    }

    public List<WorkerEmploymentEntry> ReadWorkerEmployment(int workerId)
    {
        CheckLinkedWorker(workerId);
        ValidateWorkerExtraSchema("tblCV", "RecordName", "WorkerUID",
            "FedUID", "Initials", "CVFromDate", "CVUntilDate",
            "Loan", "Trade", "Tour", "Developmental");
        using var cmd = new OleDbCommand(
            "SELECT [RecordName],[WorkerUID],[FedUID],[Initials]," +
            "[CVFromDate],[CVUntilDate],[Loan],[Trade],[Tour],[Developmental] " +
            "FROM [tblCV] WHERE [WorkerUID]=? ORDER BY [CVFromDate]", _connection);
        cmd.Parameters.Add("worker", OleDbType.Integer).Value = workerId;
        using var reader = cmd.ExecuteReader();
        var result = new List<WorkerEmploymentEntry>();
        if (reader == null) return result;
        while (reader.Read())
            result.Add(new WorkerEmploymentEntry(
                FieldText(reader.GetValue(0)),
                Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture),
                Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture),
                FieldText(reader.GetValue(3)),
                reader.GetValue(4) is DateTime from ? from : new DateTime(1666, 1, 1),
                reader.GetValue(5) is DateTime until ? until : new DateTime(1666, 1, 1),
                Flag(reader.GetValue(6)), Flag(reader.GetValue(7)),
                Flag(reader.GetValue(8)), Flag(reader.GetValue(9))));
        return result;
    }

    public string SaveWorkerEmployment(int workerId, string? recordName,
        WorkerEmploymentDraft draft)
    {
        CheckLinkedWorker(workerId);
        CheckLinkedCompany(draft.FedUid);
        var schema = ValidateWorkerExtraSchema("tblCV", "RecordName", "WorkerUID",
            "FedUID", "Initials", "CVFromDate", "CVUntilDate",
            "Loan", "Trade", "Tour", "Developmental");
        if (draft.FromDate.Year is < 1800 or > 2100 ||
            draft.UntilDate.Year is < 1800 or > 2100 ||
            draft.UntilDate.Date < draft.FromDate.Date)
            throw new InvalidOperationException(
                "Employment dates must be in 1800–2100, with the ending date no earlier than the starting date.");
        if (draft.Initials.Length == 0 || draft.Initials.Any(char.IsControl))
            throw new InvalidOperationException("Enter the company's initials for this historical stint.");

        var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) {
            ["WorkerUID"] = workerId, ["FedUID"] = draft.FedUid,
            ["Initials"] = draft.Initials.Trim(),
            ["CVFromDate"] = draft.FromDate.Date, ["CVUntilDate"] = draft.UntilDate.Date,
            ["Loan"] = draft.Loan, ["Trade"] = draft.Trade,
            ["Tour"] = draft.Tour, ["Developmental"] = draft.Developmental
        };
        foreach (var entry in values)
        {
            DataColumn col = schema.Columns[entry.Key]!;
            if (col.DataType != entry.Value.GetType() ||
                col.MaxLength > 0 && entry.Value is string str && str.Length > col.MaxLength)
                throw new InvalidOperationException(
                    $"Unsupported type or value for tblCV.{entry.Key}. No changes made.");
        }
        if (schema.Columns["RecordName"]!.DataType != typeof(string))
            throw new InvalidOperationException("Unexpected employment record identifier type.");
        foreach (DataColumn col in schema.Columns)
            if (col.ColumnName != "RecordName" && !values.ContainsKey(col.ColumnName) &&
                !col.AutoIncrement && !col.AllowDBNull && col.DefaultValue is DBNull)
                throw new InvalidOperationException(
                    $"Unverified mandatory employment field {col.ColumnName}. No changes made.");

        EnsureUnchanged();
        using var tx = _connection.BeginTransaction();
        try
        {
            string key;
            if (string.IsNullOrEmpty(recordName))
            {
                key = "TUSCV" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
                int maxLen = schema.Columns["RecordName"]!.MaxLength;
                if (maxLen > 0 && key.Length > maxLen)
                    throw new InvalidOperationException(
                        "Generated employment RecordName exceeds this MDB's text limit.");
                using (var duplicate = new OleDbCommand(
                    "SELECT COUNT(*) FROM [tblCV] WHERE [RecordName]=?", _connection, tx))
                {
                    duplicate.Parameters.Add("name", OleDbType.VarWChar).Value = key;
                    if (Convert.ToInt32(duplicate.ExecuteScalar(), CultureInfo.InvariantCulture) != 0)
                        throw new InvalidOperationException("The new employment identifier collided. Try again.");
                }
                string[] cols = { "RecordName", "WorkerUID", "FedUID", "Initials",
                    "CVFromDate", "CVUntilDate", "Loan", "Trade", "Tour", "Developmental" };
                using var insert = new OleDbCommand(
                    "INSERT INTO [tblCV] (" + string.Join(",", cols.Select(c => $"[{c}]")) +
                    ") VALUES (" + string.Join(",", cols.Select(_ => "?")) + ")", _connection, tx);
                insert.Parameters.Add("name", OleDbType.VarWChar).Value = key;
                foreach (string col in cols.Skip(1))
                    insert.Parameters.Add(col, ToOleDbType(schema.Columns[col]!.DataType)).Value =
                        values[col];
                if (insert.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException("The new employment period was not inserted.");
            }
            else
            {
                key = recordName;
                string[] cols = { "FedUID", "Initials", "CVFromDate", "CVUntilDate",
                    "Loan", "Trade", "Tour", "Developmental" };
                using var update = new OleDbCommand(
                    "UPDATE [tblCV] SET " +
                    string.Join(",", cols.Select(c => $"[{c}]=?")) +
                    " WHERE [RecordName]=? AND [WorkerUID]=?", _connection, tx);
                foreach (string col in cols)
                    update.Parameters.Add(col, ToOleDbType(schema.Columns[col]!.DataType)).Value =
                        values[col];
                update.Parameters.Add("name", OleDbType.VarWChar).Value = key;
                update.Parameters.Add("worker", OleDbType.Integer).Value = workerId;
                if (update.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException(
                        "The selected employment record no longer matches this worker.");
            }
            tx.Commit();
            _openedStamp = GetStamp(PathOnDisk);
            return key;
        }
        catch
        {
            try { tx.Rollback(); } catch { }
            throw;
        }
    }

    public void DeleteWorkerEmployment(int workerId, string recordName)
    {
        CheckLinkedWorker(workerId);
        if (string.IsNullOrWhiteSpace(recordName))
            throw new InvalidOperationException("Select an employment record.");
        ValidateWorkerExtraSchema("tblCV", "RecordName", "WorkerUID");
        EnsureUnchanged();
        using var tx = _connection.BeginTransaction();
        try
        {
            using var cmd = new OleDbCommand(
                "DELETE FROM [tblCV] WHERE [RecordName]=? AND [WorkerUID]=?", _connection, tx);
            cmd.Parameters.Add("name", OleDbType.VarWChar).Value = recordName;
            cmd.Parameters.Add("worker", OleDbType.Integer).Value = workerId;
            if (cmd.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("The selected employment period no longer exists.");
            tx.Commit();
            _openedStamp = GetStamp(PathOnDisk);
        }
        catch
        {
            try { tx.Rollback(); } catch { }
            throw;
        }
    }
}
