using System.Data;
using System.Data.OleDb;
using System.Globalization;

namespace TEWUniverse.Studio;

internal sealed record WorkerItem(int Id, string Name)
{
    public override string ToString() => $"{Name}  |  ID {Id}";
}

internal sealed record FieldEdit(string Table, string Key, string Field, Type FieldType, string NewText);
internal sealed record RecordSection(string Table, string Key, DataTable Data)
{
    public bool Exists => Data.Rows.Count != 0;
}

internal sealed class DatabaseSession : IDisposable
{
    private readonly string _password;
    private OleDbConnection _connection;
    public string PathOnDisk { get; }
    public string ProjectFolder => Path.GetDirectoryName(PathOnDisk)!;
    public string Provider { get; private set; } = "";

    // The session is only constructed for copies in our application-owned working directory.
    private DatabaseSession(string path, string password)
    {
        PathOnDisk = path;
        _password = password;
        _connection = Connect();
    }

    public static string ProjectRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TEW Universe Studio", "Projects");

    public static DatabaseSession ImportSource(string sourceFile, string password)
    {
        if (!File.Exists(sourceFile) || !sourceFile.EndsWith(".mdb", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Select an existing .mdb file.");
        Directory.CreateDirectory(ProjectRoot);
        var folder = Path.Combine(ProjectRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, "TEW9.mdb");
        File.Copy(sourceFile, target, overwrite: false);
        try { return new DatabaseSession(target, password); }
        catch { /* Preserve the imported working copy for troubleshooting. */ throw; }
    }

    public static DatabaseSession OpenWorkingCopy(string path, string password)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetFullPath(ProjectRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only working copies inside TEW Universe Studio's Projects folder can be opened for editing. Use Import to make a copy.");
        if (!File.Exists(full) || !full.EndsWith(".mdb", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The selected working .mdb file is missing.");
        return new DatabaseSession(full, password);
    }

    private OleDbConnection Connect()
    {
        var failures = new List<string>();
        // ACE is required in a 64-bit build; Jet 4 itself is 32-bit only.
        foreach (var provider in new[] { "Microsoft.ACE.OLEDB.16.0", "Microsoft.ACE.OLEDB.12.0" })
        {
            foreach (var variant in new[] { 0, 1, 2 })
            {
                OleDbConnection? cn = null;
                try
                {
                    string cs;
                    if (variant == 0)
                    {
                        var builder = new OleDbConnectionStringBuilder { Provider = provider, DataSource = PathOnDisk };
                        builder["Jet OLEDB:Database Password"] = _password;
                        cs = builder.ConnectionString;
                    }
                    else
                    {
                        static string Quoted(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
                        cs = $"Provider={provider};Data Source={Quoted(PathOnDisk)};";
                        if (variant == 2) cs += "User ID=Admin;";
                        cs += $"Jet OLEDB:Database Password={Quoted(_password)};";
                    }
                    cn = new OleDbConnection(cs);
                    cn.Open();
                    Provider = provider;
                    return cn;
                }
                catch (Exception ex)
                {
                    cn?.Dispose();
                    var message = ex.Message.Replace(_password, "[REDACTED]", StringComparison.Ordinal);
                    message = message.Replace(PathOnDisk, "[DATABASE PATH]", StringComparison.OrdinalIgnoreCase);
                    failures.Add($"{provider}, connection format {variant}: {message}");
                }
            }
        }
        throw new InvalidOperationException("Could not connect to the MDB. Check your 64-bit Microsoft ACE provider and password.\n\n" + string.Join("\n", failures.Take(5)));
    }

    public List<WorkerItem> ListWorkers()
    {
        using var cmd = new OleDbCommand("SELECT [UID], [Name] FROM [tblWorker] ORDER BY [Name]", _connection);
        using var reader = cmd.ExecuteReader();
        var items = new List<WorkerItem>();
        if (reader == null) return items;
        while (reader.Read())
        {
            if (reader.IsDBNull(0)) continue;
            items.Add(new WorkerItem(Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture),
                reader.IsDBNull(1) ? "(unnamed)" : Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture) ?? "(unnamed)"));
        }
        return items;
    }

    // Table and key are resolved from this hardcoded, small allowlist, never arbitrary UI text.
    private static readonly Dictionary<string, string> SectionKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["tblWorker"] = "UID",
        ["tblWorkerSkill"] = "WorkerUID",
        ["tblWorkerOver"] = "WorkerUID",
        ["tblWorkerPhysical"] = "WorkerUID",
        ["tblWorkerBio"] = "UID"
    };

    public RecordSection ReadSection(string table, int workerId)
    {
        if (!SectionKeys.TryGetValue(table, out var key))
            throw new ArgumentException("Unsupported table.");
        using var cmd = new OleDbCommand($"SELECT * FROM [{table}] WHERE [{key}] = ?", _connection);
        cmd.Parameters.Add("workerId", OleDbType.Integer).Value = workerId;
        using var adapter = new OleDbDataAdapter(cmd);
        var result = new DataTable(table);
        adapter.Fill(result);
        return new RecordSection(table, key, result);
    }

    public string Backup()
    {
        // A backup is created with the connection closed so the copy is consistent.
        _connection.Dispose();
        var folder = Path.Combine(ProjectFolder, "Backups");
        Directory.CreateDirectory(folder);
        var destination = Path.Combine(folder, "TEW9-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + ".mdb");
        File.Copy(PathOnDisk, destination, overwrite: false);
        _connection = Connect();
        return destination;
    }

    public (int Modified, string BackupFile) Save(int workerId, IReadOnlyCollection<FieldEdit> edits)
    {
        if (edits.Count == 0) return (0, "");
        // If backup or reopen fails, no database updates are performed.
        var backup = Backup();
        using var transaction = _connection.BeginTransaction();
        try
        {
            int changed = 0;
            foreach (var edit in edits)
            {
                if (!SectionKeys.TryGetValue(edit.Table, out var key) || key != edit.Key)
                    throw new InvalidOperationException("Invalid table/key edit.");
                // Field names must come from the actual schema loaded for this worker.
                using var schemaCommand = new OleDbCommand($"SELECT TOP 1 * FROM [{edit.Table}]", _connection, transaction);
                using (var schemaReader = schemaCommand.ExecuteReader(CommandBehavior.SchemaOnly))
                {
                    var names = Enumerable.Range(0, schemaReader!.FieldCount).Select(schemaReader.GetName);
                    if (!names.Contains(edit.Field, StringComparer.OrdinalIgnoreCase) ||
                        edit.Field.Equals(edit.Key, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Invalid field edit.");
                }
                using var cmd = new OleDbCommand($"UPDATE [{edit.Table}] SET [{edit.Field}] = ? WHERE [{edit.Key}] = ?", _connection, transaction);
                cmd.Parameters.Add("value", ToOleDbType(edit.FieldType)).Value = Parse(edit.NewText, edit.FieldType);
                cmd.Parameters.Add("workerId", OleDbType.Integer).Value = workerId;
                int rows = cmd.ExecuteNonQuery();
                if (rows != 1)
                    throw new InvalidOperationException($"Update of {edit.Table}.{edit.Field} matched {rows} rows, expected 1.");
                changed++;
            }
            transaction.Commit();
            return (changed, backup);
        }
        catch
        {
            try { transaction.Rollback(); } catch { /* original exception is more useful */ }
            throw;
        }
    }

    public static bool IsEditable(Type type) => type == typeof(string) || type == typeof(bool) ||
        type == typeof(short) || type == typeof(int) || type == typeof(long) ||
        type == typeof(byte) || type == typeof(decimal) || type == typeof(double) ||
        type == typeof(float) || type == typeof(DateTime);

    public static string Display(object value)
    {
        if (value == null || value is DBNull) return "";
        if (value is DateTime d) return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
    }

    private static OleDbType ToOleDbType(Type type)
    {
        if (type == typeof(string)) return OleDbType.VarWChar;
        if (type == typeof(bool)) return OleDbType.Boolean;
        if (type == typeof(short)) return OleDbType.SmallInt;
        if (type == typeof(byte)) return OleDbType.UnsignedTinyInt;
        if (type == typeof(int)) return OleDbType.Integer;
        if (type == typeof(long)) return OleDbType.BigInt;
        if (type == typeof(decimal)) return OleDbType.Decimal;
        if (type == typeof(double)) return OleDbType.Double;
        if (type == typeof(float)) return OleDbType.Single;
        if (type == typeof(DateTime)) return OleDbType.Date;
        throw new InvalidOperationException($"Cannot edit fields of type {type.Name}.");
    }

    private static object Parse(string text, Type type)
    {
        if (text.Length == 0) return DBNull.Value;
        var culture = CultureInfo.InvariantCulture;
        if (type == typeof(string)) return text;
        if (type == typeof(bool))
        {
            if (text == "1") return true;
            if (text == "0") return false;
            return bool.Parse(text);
        }
        if (type == typeof(DateTime)) return DateTime.Parse(text, culture);
        if (type == typeof(byte)) return byte.Parse(text, culture);
        if (type == typeof(short)) return short.Parse(text, culture);
        if (type == typeof(int)) return int.Parse(text, culture);
        if (type == typeof(long)) return long.Parse(text, culture);
        if (type == typeof(float)) return float.Parse(text, culture);
        if (type == typeof(double)) return double.Parse(text, culture);
        if (type == typeof(decimal)) return decimal.Parse(text, culture);
        throw new InvalidOperationException($"Unsupported type: {type.Name}");
    }

    public void Dispose() => _connection.Dispose();
}
