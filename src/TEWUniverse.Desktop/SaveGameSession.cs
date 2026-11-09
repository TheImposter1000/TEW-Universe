using System.Data;
using System.Data.OleDb;
using System.Globalization;
using TEWUniverse.Studio;

namespace TEWUniverse.Desktop;

/// <summary>
/// Edits existing scalar fields in the selected TEW IX save MDB in place.
/// A table must expose a stable key, and each write verifies record identity
/// and the original field value inside the same Access transaction.
/// No working copy or mandatory backup is created.
/// </summary>
internal sealed class SaveGameSession : IDisposable
{
    private readonly OleDbConnection _connection;
    private readonly bool _ownsConnection;
    private readonly Action? _notifySharedWrite;
    private readonly Dictionary<string, string> _tableTypes;
    private bool _disposed;
    private FileStamp _lastKnownStamp;

    private readonly record struct FileStamp(long Length, DateTime LastWriteUtc);

    private SaveGameSession(string path, OleDbConnection connection,
        Dictionary<string, string> catalog, bool ownsConnection = true,
        Action? notifySharedWrite = null)
    {
        OriginalPath = path;
        _connection = connection;
        _ownsConnection = ownsConnection;
        _notifySharedWrite = notifySharedWrite;
        _tableTypes = catalog;
        TableNames = catalog.Keys.OrderBy(name => name,
            StringComparer.OrdinalIgnoreCase).ToArray();
        _lastKnownStamp = GetStamp(path);
    }

    internal string OriginalPath { get; }
    internal IReadOnlyList<string> TableNames { get; }
    internal string TableType(string name) =>
        _tableTypes.TryGetValue(name, out var type) ? type : "Unknown";

    // The all-tables browser uses the same ACE session as the main editor.
    // It is a view within Studio, not a second database window.
    internal static SaveGameSession Attach(DatabaseSession parent)
    {
        if (!parent.IsSaveGame)
            throw new InvalidOperationException("A save game must be selected first.");
        var catalog = ReadTableCatalog(parent.SharedConnection);
        return new SaveGameSession(parent.PathOnDisk, parent.SharedConnection,
            catalog, ownsConnection: false, notifySharedWrite: parent.MarkInternalWrite);
    }

    private static FileStamp GetStamp(string path)
    {
        var info = new FileInfo(path);
        info.Refresh();
        if (!info.Exists) throw new FileNotFoundException("The save MDB is missing.", path);
        return new FileStamp(info.Length, info.LastWriteTimeUtc);
    }

    private void EnsureUnchanged()
    {
        if (GetStamp(OriginalPath) != _lastKnownStamp)
            throw new InvalidOperationException(
                "The TEW save changed on disk since it was opened. No edits were written. " +
                "Close and reopen Save Studio to load the current game state.");
    }

    internal static SaveGameSession OpenDirect(string sourcePath, string password)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new InvalidOperationException("Choose a TEW IX save MDB.");
        var path = Path.GetFullPath(sourcePath.Trim());
        if (!File.Exists(path) || !Path.GetExtension(path)
                .Equals(".mdb", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Choose an existing .mdb save.");
        if (new FileInfo(path).IsReadOnly)
            throw new InvalidOperationException("The selected save is marked read-only.");

        // This is a direct writer. Do not proceed if the game or another editor
        // already has the database file open.
        try
        {
            using var exclusiveCheck = new FileStream(
                path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException(
                "The save is currently in use. Close TEW IX and other editors " +
                "before opening Save Studio.", ex);
        }

        var errors = new List<string>();
        foreach (var provider in new[] {
            "Microsoft.ACE.OLEDB.16.0", "Microsoft.ACE.OLEDB.12.0"
        })
        {
            OleDbConnection? connection = null;
            try
            {
                var builder = new OleDbConnectionStringBuilder {
                    Provider = provider, DataSource = path
                };
                builder["Jet OLEDB:Database Password"] = password;
                connection = new OleDbConnection(builder.ConnectionString);
                connection.Open();

                var catalog = ReadTableCatalog(connection);
                if (TewFileKindDetector.Detect(catalog.Keys) != TewFileKind.SaveGame)
                    throw new InvalidOperationException(
                        "This file lacks the expected TEW IX save-game tables. " +
                        "Use Select Database for a scenario MDB.");

                return new SaveGameSession(path, connection, catalog);
            }
            catch (Exception ex)
            {
                connection?.Dispose();
                if (ex is InvalidOperationException &&
                    ex.Message.StartsWith("This file lacks", StringComparison.Ordinal))
                    throw;

                var message = ex.Message;
                if (!string.IsNullOrEmpty(password))
                    message = message.Replace(password, "[REDACTED]", StringComparison.Ordinal);
                errors.Add(provider + ": " + message);
            }
        }

        throw new InvalidOperationException(
            "Could not connect to the save. Confirm the password and the 64-bit " +
            "Microsoft ACE OLE DB installation.\n\n" + string.Join("\n", errors.Take(2)));
    }

    // Show all physical user tables, regardless of naming convention.
    // MSys and USys are Access internals, not TEW's missing worker records.
    private static Dictionary<string, string> ReadTableCatalog(OleDbConnection connection)
    {
        var catalog = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var schema = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Tables, null);
        if (schema == null) return catalog;
        foreach (DataRow row in schema.Rows)
        {
            var name = Convert.ToString(row["TABLE_NAME"])?.Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;
            var type = schema.Columns.Contains("TABLE_TYPE")
                ? Convert.ToString(row["TABLE_TYPE"]) ?? "TABLE" : "TABLE";
            if (SaveTableBrowserRules.Include(name, type))
                catalog.TryAdd(name, type);
        }

        // Some TEW IX saves contain tables whose Access metadata is not
        // enumerated normally. These are genuine, directly queried worker
        // sections. Only add them if the provider can actually read them.
        foreach (var workerTable in new[] {
            "tblWorker", "tblWorkerSkill", "tblWorkerOver",
            "tblWorkerPhysical", "tblWorkerBio"
        })
        {
            if (catalog.ContainsKey(workerTable)) continue;
            try
            {
                using var command = new OleDbCommand(
                    "SELECT TOP 1 * FROM [" + workerTable + "]", connection);
                using var reader = command.ExecuteReader();
                if (reader != null)
                    catalog.TryAdd(workerTable, "TABLE");
            }
            catch (OleDbException)
            {
                // This named worker table is not accessible in this save.
            }
        }
        return catalog;
    }

    internal int CountRows(string tableName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!TableNames.Contains(tableName, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("The table was not discovered in this save.");

        using var command = new OleDbCommand(
            "SELECT COUNT(*) FROM " + QuoteName(tableName), _connection);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Load one page or all actual saved records. Sort by Access primary key
    /// when available, so paging does not depend on physical table order.
    /// pageSize = 0 means explicitly load all records (user-selected).
    /// </summary>
    internal DataTable Preview(string tableName, int pageIndex = 0, int pageSize = 250)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!TableNames.Contains(tableName, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("The table was not discovered in this save.");
        if (pageIndex < 0 || pageSize < 0 || pageSize > 1000)
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        if (pageSize == 0 && pageIndex != 0)
            throw new ArgumentOutOfRangeException(nameof(pageIndex));
        if (pageSize > 0 && pageIndex > int.MaxValue / pageSize)
            throw new ArgumentOutOfRangeException(nameof(pageIndex));

        var quoted = QuoteName(tableName);
        // A zero-row result reveals the actual column schema without loading
        // thousands of records to discover the correct identity fields.
        using var schemaCommand = new OleDbCommand(
            "SELECT * FROM " + quoted + " WHERE 1 = 0", _connection);
        using var schemaAdapter = new OleDbDataAdapter(schemaCommand);
        var schema = new DataTable(tableName);
        schemaAdapter.Fill(schema);
        var keys = ResolveRecordKeys(schema);

        var sql = "SELECT * FROM " + quoted +
            (keys.Length == 0 ? "" : " ORDER BY " +
                string.Join(", ", keys.Select(QuoteName)));
        using var pageCommand = new OleDbCommand(sql, _connection);
        using var pageAdapter = new OleDbDataAdapter(pageCommand);
        var dataset = new DataSet();
        if (pageSize == 0)
            pageAdapter.Fill(dataset, tableName);
        else
            pageAdapter.Fill(dataset, pageIndex * pageSize, pageSize, tableName);
        var table = dataset.Tables.Contains(tableName)
            ? dataset.Tables[tableName]!
            : new DataTable(tableName);
        table.ExtendedProperties["SaveRecordKeys"] = keys;
        return table;
    }

    private string[] ResolveRecordKeys(DataTable table)
    {
        // Prefer the actual Access primary key. Composite keys are supported.
        // Provider versions that do not expose this schema can fall back to
        // conventional record keys, but uniqueness is checked at commit time.
        try
        {
            var schema = _connection.GetOleDbSchemaTable(
                OleDbSchemaGuid.Primary_Keys, new object?[] { null, null, table.TableName });
            if (schema != null && schema.Columns.Contains("COLUMN_NAME"))
            {
                var keyRows = schema.Rows.Cast<DataRow>();
                if (schema.Columns.Contains("KEY_SEQ"))
                    keyRows = keyRows.OrderBy(row =>
                        Convert.ToInt32(row["KEY_SEQ"], CultureInfo.InvariantCulture));
                else if (schema.Columns.Contains("ORDINAL"))
                    keyRows = keyRows.OrderBy(row =>
                        Convert.ToInt32(row["ORDINAL"], CultureInfo.InvariantCulture));
                var keys = keyRows
                    .Select(row => Convert.ToString(row["COLUMN_NAME"], CultureInfo.InvariantCulture) ?? "")
                    .Where(name => table.Columns.Contains(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (keys.Length > 0) return keys;
            }
        }
        catch (OleDbException)
        {
            // Some installed ACE versions do not expose the primary-key rowset.
        }
        catch (NotSupportedException)
        {
        }

        foreach (var candidate in new[] {
            "UID", "WorkerUID", "FedUID", "BeltUID", "TeamUID",
            "ContractUID", "RecordID", "ID"
        })
            if (table.Columns.Contains(candidate))
                return new[] { table.Columns[candidate]!.ColumnName };

        return Array.Empty<string>();
    }

    internal static IReadOnlyList<string> RecordKeys(DataTable table)
        => table.ExtendedProperties["SaveRecordKeys"] as string[] ?? Array.Empty<string>();

    internal static bool CanEdit(DataTable table, DataColumn column)
        => SaveEditRules.CanEdit(table.TableName, column, RecordKeys(table));

    internal int CountPendingEdits(DataTable table)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return table.Rows.Cast<DataRow>().Sum(row => row.RowState == DataRowState.Modified
            ? table.Columns.Cast<DataColumn>().Count(column =>
                !Equals(row[column, DataRowVersion.Current],
                    row[column, DataRowVersion.Original]))
            : 0);
    }

    private sealed record PendingField(
        object[] RecordKeyValues, DataColumn Column, object OldValue, object NewValue);

    /// <summary>
    /// Commit validated changes to the exact MDB selected by the user.
    /// Fields with unsupported Access types and relationship keys are locked.
    /// The full batch is atomic and stale or ambiguous rows are rejected.
    /// </summary>
    internal int SaveChanges(DataTable table)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!TableNames.Contains(table.TableName, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("The table was not found in this save.");

        var keyNames = RecordKeys(table);
        if (keyNames.Count == 0)
            throw new InvalidOperationException(
                "This table has no discoverable record identity. It cannot be safely edited.");
        if (keyNames.Any(key => !table.Columns.Contains(key)))
            throw new InvalidOperationException("The table's record keys are missing.");

        var pending = new List<PendingField>();
        foreach (DataRow row in table.Rows)
        {
            if (row.RowState is DataRowState.Added or DataRowState.Deleted)
                throw new InvalidOperationException(
                    "Adding and deleting save records requires dedicated relationship validation.");
            if (row.RowState != DataRowState.Modified) continue;

            var keys = keyNames.Select(name =>
                row[name, DataRowVersion.Original]).ToArray();
            if (keys.Any(value => value is DBNull))
                throw new InvalidOperationException("A save record has a missing identity value.");
            for (var i = 0; i < keyNames.Count; i++)
                if (!Equals(keys[i], row[keyNames[i], DataRowVersion.Current]))
                    throw new InvalidOperationException("Record identity columns cannot be edited.");

            foreach (DataColumn column in table.Columns)
            {
                var before = row[column, DataRowVersion.Original];
                var after = row[column, DataRowVersion.Current];
                if (Equals(before, after)) continue;
                if (!CanEdit(table, column) ||
                    !SaveEditRules.IsValidValue(table.TableName, column, after))
                    throw new InvalidOperationException(
                        "Unsupported or invalid save field: " +
                        table.TableName + "." + column.ColumnName +
                        ". Identifiers, relationship keys and unsupported data types are protected.");
                pending.Add(new PendingField(keys, column, before, after));
            }
        }

        if (pending.Count == 0) return 0;
        EnsureUnchanged();

        var tableSql = QuoteName(table.TableName);
        var keyPredicate = string.Join(" AND ", keyNames.Select(k => QuoteName(k) + " = ?"));

        using var transaction = _connection.BeginTransaction();
        try
        {
            foreach (var edit in pending)
            {
                var fieldSql = QuoteName(edit.Column.ColumnName);

                // Compare values in .NET rather than using SQL equality against
                // Access Memo/LongText columns, which cannot be compared with =.
                using (var select = new OleDbCommand(
                    "SELECT " + fieldSql + " FROM " + tableSql +
                    " WHERE " + keyPredicate, _connection, transaction))
                {
                    for (int k = 0; k < edit.RecordKeyValues.Length; k++)
                        select.Parameters.AddWithValue("key" + k, edit.RecordKeyValues[k]);

                    using var reader = select.ExecuteReader();
                    if (reader == null || !reader.Read())
                        throw new InvalidOperationException(
                            "The save record could not be found. No changes were saved.");
                    var current = reader.GetValue(0);
                    if (reader.Read())
                        throw new InvalidOperationException(
                            "More than one save record matches these keys. No changes were saved.");
                    if (!Equals(current, edit.OldValue))
                        throw new InvalidOperationException(
                            "A save field was modified outside Studio. " +
                            "No changes were saved. Reopen the save to refresh.");
                }

                using var update = new OleDbCommand(
                    "UPDATE " + tableSql + " SET " + fieldSql + " = ? WHERE " +
                    keyPredicate, _connection, transaction);

                if (edit.NewValue is string text && text.Length > 255)
                    update.Parameters.Add("value", OleDbType.LongVarWChar).Value = text;
                else
                    update.Parameters.AddWithValue("value", edit.NewValue);

                for (int k = 0; k < edit.RecordKeyValues.Length; k++)
                    update.Parameters.AddWithValue("key" + k, edit.RecordKeyValues[k]);

                if (update.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException(
                        "The save update did not affect exactly one record. " +
                        "No changes were saved.");
            }

            transaction.Commit();
            _lastKnownStamp = GetStamp(OriginalPath);
            _notifySharedWrite?.Invoke();
            table.AcceptChanges();
            return pending.Count;
        }
        catch
        {
            try { transaction.Rollback(); }
            catch (InvalidOperationException) { }
            throw;
        }
    }

    private static string QuoteName(string value)
        => "[" + value.Replace("]", "]]", StringComparison.Ordinal) + "]";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsConnection) _connection.Dispose();
    }
}
