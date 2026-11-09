using System.Data;
using System.Data.OleDb;
using System.Globalization;
using TEWUniverse.Studio;

namespace TEWUniverse.Desktop;

/// <summary>
/// Opens the user's actual TEW IX save MDB and commits approved, validated
/// edits in-place. No working copy, snapshot or mandatory backup is created.
/// Unknown game-state tables remain read-only.
/// </summary>
internal sealed class SaveGameSession : IDisposable
{
    private readonly OleDbConnection _connection;
    private bool _disposed;
    private FileStamp _lastKnownStamp;

    private readonly record struct FileStamp(long Length, DateTime LastWriteUtc);

    private SaveGameSession(string path, OleDbConnection connection, List<string> tables)
    {
        OriginalPath = path;
        _connection = connection;
        TableNames = tables.AsReadOnly();
        _lastKnownStamp = GetStamp(path);
    }

    internal string OriginalPath { get; }
    internal IReadOnlyList<string> TableNames { get; }

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

                var tables = ReadUserTables(connection);
                if (TewFileKindDetector.Detect(tables) != TewFileKind.SaveGame)
                    throw new InvalidOperationException(
                        "This file lacks the expected TEW IX save-game tables. " +
                        "Use Select Database for a scenario MDB.");

                return new SaveGameSession(path, connection, tables);
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

    private static List<string> ReadUserTables(OleDbConnection connection)
    {
        var schema = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Tables, null);
        if (schema == null) return new();
        return schema.Rows.Cast<DataRow>()
            .Where(row => string.Equals(
                Convert.ToString(row["TABLE_TYPE"]), "TABLE", StringComparison.OrdinalIgnoreCase))
            .Select(row => Convert.ToString(row["TABLE_NAME"]) ?? "")
            .Where(name => name.StartsWith("tbl", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Preview existing table data. The DataTable tracks pending grid edits
    /// in memory until Save Changes is explicitly confirmed by the user.
    /// </summary>
    internal DataTable Preview(string tableName, int limit = 250)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!TableNames.Contains(tableName, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("The table was not discovered in this save.");
        if (limit < 1 || limit > 250)
            throw new ArgumentOutOfRangeException(nameof(limit));

        var quoted = QuoteName(tableName);
        using var command = new OleDbCommand(
            "SELECT TOP " + limit.ToString(CultureInfo.InvariantCulture) +
            " * FROM " + quoted, _connection);
        using var adapter = new OleDbDataAdapter(command);
        var table = new DataTable(tableName);
        adapter.Fill(table);
        return table;
    }

    internal static bool CanEdit(DataTable table, DataColumn column)
        => SaveEditRules.CanEdit(table.TableName, column)
            && table.Columns.Contains(SaveEditRules.IdentityColumn(table.TableName) ?? "");

    internal int CountPendingEdits(DataTable table)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return table.Rows.Cast<DataRow>().Sum(row => row.RowState == DataRowState.Modified
            ? table.Columns.Cast<DataColumn>().Count(column =>
                !Equals(row[column, DataRowVersion.Current],
                    row[column, DataRowVersion.Original]))
            : 0);
    }

    /// <summary>
    /// Apply only explicitly allowlisted scalar changes. Validate all pending
    /// edits and the current database identity before any SQL is issued;
    /// rollback the whole batch on a missing, duplicated or stale record.
    /// </summary>
    internal int SaveChanges(DataTable table)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!TableNames.Contains(table.TableName, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("The table was not found in this save.");
        var identityName = SaveEditRules.IdentityColumn(table.TableName);
        if (identityName == null || !table.Columns.Contains(identityName))
            throw new InvalidOperationException("This table is read-only.");

        var identity = table.Columns[identityName]!;
        var pending = new List<(object Id, DataColumn Column, object Before, object After)>();
        foreach (DataRow row in table.Rows)
        {
            if (row.RowState is DataRowState.Added or DataRowState.Deleted)
                throw new InvalidOperationException("Inserting and deleting records is not supported.");
            if (row.RowState != DataRowState.Modified) continue;
            var id = row[identity, DataRowVersion.Original];
            if (id is DBNull || !Equals(id, row[identity, DataRowVersion.Current]))
                throw new InvalidOperationException("Record identity cannot be edited.");

            foreach (DataColumn column in table.Columns)
            {
                var before = row[column, DataRowVersion.Original];
                var after = row[column, DataRowVersion.Current];
                if (Equals(before, after)) continue;
                if (!CanEdit(table, column) ||
                    !SaveEditRules.IsValidValue(table.TableName, column.ColumnName, after))
                    throw new InvalidOperationException(
                        "Unsupported save edit: " + table.TableName + "." +
                        column.ColumnName + ". Only verified skill values from 0 to 100 can be updated.");
                pending.Add((id, column, before, after));
            }
        }
        if (pending.Count == 0) return 0;
        EnsureUnchanged();

        using var transaction = _connection.BeginTransaction();
        try
        {
            foreach (var id in pending.Select(x => x.Id).Distinct())
            {
                using var count = new OleDbCommand(
                    "SELECT COUNT(*) FROM " + QuoteName(table.TableName) +
                    " WHERE " + QuoteName(identityName) + " = ?", _connection, transaction);
                count.Parameters.AddWithValue("id", id);
                if (Convert.ToInt32(count.ExecuteScalar(), CultureInfo.InvariantCulture) != 1)
                    throw new InvalidOperationException(
                        "The save contains a missing or ambiguous worker skill record. " +
                        "No changes will be saved.");
            }
            foreach (var edit in pending)
            {
                var sql = "UPDATE " + QuoteName(table.TableName) +
                    " SET " + QuoteName(edit.Column.ColumnName) + " = ? WHERE " +
                    QuoteName(identityName) + " = ? AND " +
                    QuoteName(edit.Column.ColumnName) +
                    (edit.Before is DBNull ? " IS NULL" : " = ?");
                using var command = new OleDbCommand(sql, _connection, transaction);
                command.Parameters.AddWithValue("newValue", edit.After);
                command.Parameters.AddWithValue("id", edit.Id);
                if (edit.Before is not DBNull)
                    command.Parameters.AddWithValue("oldValue", edit.Before);
                if (command.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException(
                        "A save value changed outside Save Studio. No edits were saved. " +
                        "Reopen the save and try again.");
            }
            transaction.Commit();
            _lastKnownStamp = GetStamp(OriginalPath);
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
        _connection.Dispose();
    }
}
