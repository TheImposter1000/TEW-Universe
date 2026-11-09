using System.Data;
using System.Data.OleDb;
using TEWUniverse.Studio;

namespace TEWUniverse.Desktop;

/// <summary>
/// A read-only save inspector. The original save is NEVER used as an ACE data source:
/// the user-selected file is copied to a disposable temporary snapshot first.
/// No SQL mutation APIs are exposed by this class.
/// </summary>
internal sealed class SaveGameSession : IDisposable
{
    private readonly OleDbConnection _connection;
    private readonly string _snapshotPath;
    private bool _disposed;

    private SaveGameSession(string originalPath, string snapshotPath,
        OleDbConnection connection, List<string> tableNames)
    {
        OriginalPath = originalPath;
        _snapshotPath = snapshotPath;
        _connection = connection;
        TableNames = tableNames.AsReadOnly();
    }

    internal string OriginalPath { get; }
    internal IReadOnlyList<string> TableNames { get; }

    internal static SaveGameSession OpenReadOnly(string sourcePath, string password)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new InvalidOperationException("Choose a TEW IX save MDB.");

        var original = Path.GetFullPath(sourcePath.Trim());
        if (!File.Exists(original) || !Path.GetExtension(original)
                .Equals(".mdb", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Choose an existing .mdb file.");

        // A private snapshot prevents even ACE lock files or incidental provider
        // activity from touching the original TEW save.
        var snapshot = Path.Combine(Path.GetTempPath(),
            "TEWUniverse_Save_" + Guid.NewGuid().ToString("N") + ".mdb");
        File.Copy(original, snapshot);
        try
        {
            var errors = new List<string>();
            foreach (var provider in new[] {
                "Microsoft.ACE.OLEDB.16.0", "Microsoft.ACE.OLEDB.12.0"
            })
            {
                OleDbConnection? connection = null;
                try
                {
                    var builder = new OleDbConnectionStringBuilder {
                        Provider = provider, DataSource = snapshot
                    };
                    builder["Mode"] = "Read";
                    builder["Jet OLEDB:Database Password"] = password;
                    connection = new OleDbConnection(builder.ConnectionString);
                    connection.Open();

                    var tables = ReadUserTables(connection);
                    if (TewFileKindDetector.Detect(tables) != TewFileKind.SaveGame)
                        throw new InvalidOperationException(
                            "This file does not have the expected TEW IX save-game tables. " +
                            "Use Select Database for scenario MDBs.");

                    return new SaveGameSession(original, snapshot, connection, tables);
                }
                catch (Exception ex)
                {
                    connection?.Dispose();
                    var message = ex.Message;
                    if (!string.IsNullOrEmpty(password))
                        message = message.Replace(password, "[REDACTED]", StringComparison.Ordinal);
                    errors.Add(provider + ": " + message);
                    // A valid Access connection with the wrong schema is not a
                    // provider fallback case.
                    if (ex is InvalidOperationException &&
                        ex.Message.StartsWith("This file does not have", StringComparison.Ordinal))
                        throw;
                }
            }

            throw new InvalidOperationException(
                "Could not read the save snapshot. Confirm the database password " +
                "and that the 64-bit Microsoft ACE provider is installed. " +
                "Close TEW IX before opening the save.\n\n" +
                string.Join("\n", errors.Take(2)));
        }
        catch
        {
            TryDelete(snapshot);
            throw;
        }
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
    /// Fetch at most 250 records from an existing save table.
    /// Table names MUST come from schema discovery; no arbitrary SQL is accepted.
    /// </summary>
    internal DataTable Preview(string tableName, int limit = 250)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!TableNames.Contains(tableName, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("The table was not discovered in this save.");
        if (limit < 1 || limit > 250)
            throw new ArgumentOutOfRangeException(nameof(limit));

        var quoted = "[" + tableName.Replace("]", "]]", StringComparison.Ordinal) + "]";
        using var command = new OleDbCommand(
            "SELECT TOP " + limit.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            " * FROM " + quoted, _connection);
        using var adapter = new OleDbDataAdapter(command);
        var table = new DataTable(tableName);
        adapter.Fill(table);
        return table;
    }

    private static void TryDelete(string file)
    {
        try { if (File.Exists(file)) File.Delete(file); }
        catch (IOException) { /* Best-effort cleanup of a disposable snapshot. */ }
        catch (UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _connection.Dispose();
        TryDelete(_snapshotPath);
    }
}
