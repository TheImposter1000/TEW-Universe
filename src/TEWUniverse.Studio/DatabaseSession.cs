using System.Data;
using System.Data.OleDb;
using System.Globalization;

namespace TEWUniverse.Studio;

internal sealed record WorkerItem(int Id, string Name, bool? IsActive, string? PictureName = null)
{
    public override string ToString() => $"{Name}  |  ID {Id}";
}

internal sealed record ExistingRecordItem(int Id, string Name)
{
    public override string ToString() => $"{Name}  |  ID {Id}";
}

/// <summary>
/// Only existing records can be edited. Allowed fields intentionally exclude
/// UIDs and foreign keys (company ownership, title holders, stable members).
/// Additional tables/fields must be validated against TEW IX before enabling.
/// </summary>
internal sealed record ExistingEditorSpec(
    string Section, string Table, string[] EditableColumns);

internal static class ExistingEditors
{
    private static readonly Dictionary<string, ExistingEditorSpec> Registry =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Companies"] = new("Companies", "tblFed",
                new[] { "Name", "Initials", "URL", "Logo", "Backdrop", "Banner", "Prestige",
                    "Influence", "Money", "Momentum" }),
            ["Titles"] = new("Titles", "tblBelt",
                new[] { "Name", "Picture", "Profile", "Prestige", "Active" }),
            ["Events"] = new("Events", "tblCard",
                new[] { "Name", "Logo", "Dormant" }),
            ["Stables"] = new("Stables", "tblStable",
                new[] { "Name", "Logo", "Backdrop", "Profile", "Active", "Important" })
        };

    public static bool TryGet(string section, out ExistingEditorSpec spec)
        => Registry.TryGetValue(section, out spec!);
}

internal sealed record FieldEdit(string Table, string Key, string Field, Type FieldType, string NewText);
/// <summary>Read-only pre-game reigns linked to the selected existing belt.</summary>
internal sealed record TitleLineageResult(DataTable Rows, string? LinkField, string? OrderField, string Message);

// These are proposed modifications to existing prehistory slots, never INSERTs.
internal sealed record GeneratedTitleReign(
    int Order, int Holder1, string HolderName1,
    int Holder2, string HolderName2,
    DateTime Captured, DateTime Lost, short Defences);

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

    public static DatabaseSession ImportSource(string sourceFile, string password, string? destinationFile = null)
    {
        if (!File.Exists(sourceFile) || !sourceFile.EndsWith(".mdb", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Select an existing original .mdb file.");

        string target;
        if (string.IsNullOrWhiteSpace(destinationFile))
        {
            Directory.CreateDirectory(ProjectRoot);
            var folder = Path.Combine(ProjectRoot,
                DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture));
            target = Path.Combine(folder, "TEW9.mdb");
        }
        else
        {
            target = Path.GetFullPath(destinationFile.Trim());
            if (!target.EndsWith(".mdb", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Current Copy must have the .mdb extension.");
        }
        if (AppSettings.SamePath(sourceFile, target))
            throw new InvalidOperationException("The working copy cannot be the original database.");
        if (File.Exists(target))
            throw new InvalidOperationException(
                "The Current Copy destination already exists. Open that working copy instead, " +
                "or choose a different destination in Settings. Existing databases are never overwritten by Import.");

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(sourceFile, target, overwrite: false);
        // Preserve the imported copy if an Access provider refuses to connect.
        return new DatabaseSession(target, password);
    }

    public static DatabaseSession OpenWorkingCopy(string path, string password, string? originalPath = null)
    {
        var full = Path.GetFullPath(path.Trim());
        if (!File.Exists(full) || !full.EndsWith(".mdb", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The selected working .mdb file does not exist.");
        if (AppSettings.SamePath(full, originalPath))
            throw new InvalidOperationException(
                "This file is configured as the original Default Database. " +
                "It cannot be opened for editing. Import it to create a separate working copy.");
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
                    var message = string.IsNullOrEmpty(_password) ? ex.Message : ex.Message.Replace(_password, "[REDACTED]", StringComparison.Ordinal);
                    message = message.Replace(PathOnDisk, "[DATABASE PATH]", StringComparison.OrdinalIgnoreCase);
                    failures.Add($"{provider}, connection format {variant}: {message}");
                }
            }
        }
        throw new InvalidOperationException("Could not connect to the MDB. Check your 64-bit Microsoft ACE provider and password.\n\n" + string.Join("\n", failures.Take(5)));
    }

    public List<WorkerItem> ListWorkers()
    {
        using var cmd = new OleDbCommand("SELECT [UID], [Name], [Active], [Picture] FROM [tblWorker] ORDER BY [Name]", _connection);
        using var reader = cmd.ExecuteReader();
        var items = new List<WorkerItem>();
        if (reader == null) return items;
        while (reader.Read())
        {
            if (reader.IsDBNull(0)) continue;
            items.Add(new WorkerItem(Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture),
                reader.IsDBNull(1) ? "(unnamed)" :
                    Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture) ?? "(unnamed)",
                reader.IsDBNull(2) ? null :
                    Convert.ToBoolean(reader.GetValue(2), CultureInfo.InvariantCulture),
                reader.IsDBNull(3) ? null :
                    Convert.ToString(reader.GetValue(3), CultureInfo.InvariantCulture)));
        }
        return items;
    }

    public List<ExistingRecordItem> ListExistingRecords(string section)
    {
        if (!ExistingEditors.TryGet(section, out var spec))
            throw new InvalidOperationException("This module has no verified existing-record editor.");

        // Table and columns come from our fixed allowlist, not from user input.
        using var cmd = new OleDbCommand(
            $"SELECT [UID], [Name] FROM [{spec.Table}] ORDER BY [Name]", _connection);
        using var reader = cmd.ExecuteReader();
        var records = new List<ExistingRecordItem>();
        if (reader == null) return records;
        while (reader.Read())
        {
            if (reader.IsDBNull(0)) continue;
            records.Add(new ExistingRecordItem(
                Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture),
                reader.IsDBNull(1) ? "(unnamed)" :
                Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture) ?? "(unnamed)"));
        }
        return records;
    }

    public RecordSection ReadExistingRecord(string section, int id)
    {
        if (!ExistingEditors.TryGet(section, out var spec))
            throw new InvalidOperationException("Unsupported editor section.");
        return ReadSection(spec.Table, id);
    }

    public int SaveExistingRecord(string section, int id, IReadOnlyCollection<FieldEdit> edits)
    {
        if (!ExistingEditors.TryGet(section, out var spec))
            throw new InvalidOperationException("Unsupported editor section.");
        if (id <= 0) throw new InvalidOperationException("Invalid existing record ID.");
        if (edits.Count == 0) return 0;

        var allowed = new HashSet<string>(spec.EditableColumns, StringComparer.OrdinalIgnoreCase);
        var sectionData = ReadSection(spec.Table, id);
        if (!sectionData.Exists || sectionData.Data.Rows.Count != 1)
            throw new InvalidOperationException("The existing record could not be found.");
        if (edits.Any(x => !x.Table.Equals(spec.Table, StringComparison.OrdinalIgnoreCase)
            || !x.Key.Equals("UID", StringComparison.OrdinalIgnoreCase)
            || !allowed.Contains(x.Field)
            || !sectionData.Data.Columns.Contains(x.Field)
            || sectionData.Data.Columns[x.Field]!.DataType != x.FieldType))
            throw new InvalidOperationException("Editing an ID, linked relationship, or unapproved field is blocked.");

        // Reuse the established parameterized transaction without generating records.
        return Save(id, edits);
    }

    /// <summary>
    /// TEW IX pre-game title reigns are stored separately from tblBelt.
    /// The database schema is checked at runtime because field names can vary
    /// between TEW versions. A missing/ambiguous link is NEVER handled by showing
    /// unfiltered records, which could attribute the wrong reigns to a title.
    /// No INSERT/UPDATE/DELETE is available for this table.
    /// </summary>
    public TitleLineageResult ReadTitleLineage(int beltId)
    {
        if (beltId <= 0)
            throw new InvalidOperationException("Select an existing championship first.");

        const string table = "tblBeltPreHistory";
        var result = new DataTable(table);
        try
        {
            using var schemaCmd = new OleDbCommand($"SELECT * FROM [{table}] WHERE 1 = 0", _connection);
            using var schemaReader = schemaCmd.ExecuteReader(CommandBehavior.SchemaOnly);
            if (schemaReader == null || schemaReader.FieldCount == 0)
                return new(result, null, null, "No lineage columns were returned by the MDB.");

            var fields = Enumerable.Range(0, schemaReader.FieldCount)
                .Select(i => (Name: schemaReader.GetName(i), Type: schemaReader.GetFieldType(i)))
                .ToArray();
            // ACE does not reliably allow a second command while its schema
            // reader is open. Dispose it before querying any title history.
            schemaReader.Dispose();

            static bool IsInteger(Type type) => type == typeof(byte) || type == typeof(short)
                || type == typeof(int) || type == typeof(long);

            // Only explicitly identifiable numeric belt references are accepted.
            // Do not mistake worker refs, company refs, or the prehistory UID for the belt.
            string[] possibleLinks = {
                "BeltUID", "Belt_UID", "BeltID", "Belt_ID", "BeltsUID",
                "BeltsID", "TitleBeltUID", "TitleBeltID", "TitleUID",
                "Title_UID", "TitleID", "Title_ID", "Belt", "Title"
            };
            string? link = possibleLinks.Select(candidate =>
                fields.FirstOrDefault(f => f.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
                .Where(f => f.Name != null && IsInteger(f.Type))
                .Select(f => f.Name).FirstOrDefault();

            if (link == null)
            {
                var available = string.Join(", ", fields.Select(f => f.Name));
                return new(result, null, null,
                    "Could not safely identify the championship reference in tblBeltPreHistory. " +
                    "No history is shown to avoid mixing reigns from different titles. " +
                    "Detected columns: " + available);
            }

            string[] possibleOrders = {
                "BeltOrder", "OrderNumber", "Order_Number", "OrderNo", "OrderNum",
                "Order", "ReignNumber", "Position", "Sequence"
            };
            string? order = possibleOrders.Select(candidate => fields.FirstOrDefault(f =>
                f.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
                .Where(f => f.Name != null && IsInteger(f.Type))
                .Select(f => f.Name).FirstOrDefault();

            // All identifiers below originate from the schema and are checked to
            // match a fixed allowlist, not interpolated user-supplied text.
            var orderSql = order == null ? "" : $" ORDER BY [{order}] ASC";
            using var cmd = new OleDbCommand(
                $"SELECT * FROM [{table}] WHERE [{link}] = ?{orderSql}", _connection);
            cmd.Parameters.Add("beltId", OleDbType.Integer).Value = beltId;
            using var adapter = new OleDbDataAdapter(cmd);
            adapter.Fill(result);
            string message = result.Rows.Count == 0
                ? $"No pre-game title lineage entries are linked to championship ID {beltId}."
                : $"Showing {result.Rows.Count:N0} recorded reign(s) for championship ID {beltId}.";
            if (order == null)
                message += " Order Number was not identified; sequence is unverified.";
            return new(result, link, order, message);
        }
        catch (OleDbException ex)
        {
            return new(result, null, null,
                "Could not read tblBeltPreHistory from this working MDB: " + ex.Message);
        }
    }

    /// <summary>
    /// Replace the holders/dates of EVERY existing history row for one belt,
    /// keeping history UIDs, BeltUID, BeltOrder, and Recordname intact.
    /// Never creates records, allocates UIDs, or transfers other belts' rows.
    /// </summary>
    public int ReplaceExistingTitleLineage(int beltId, IReadOnlyList<GeneratedTitleReign> proposed)
    {
        if (beltId <= 0 || proposed.Count == 0)
            throw new InvalidOperationException("Choose a title and generate at least one reign.");

        // Verify the exact known TEW IX schema before any database writes.
        var existing = ReadTitleLineage(beltId);
        if (!string.Equals(existing.LinkField, "BeltUID", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(existing.OrderField, "BeltOrder", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The expected TEW IX lineage schema was not found.");

        string[] required = { "UID", "BeltUID", "BeltOrder", "Holder1", "Holder2",
            "Holder3", "Holder_Name1", "Holder_Name2", "Holder_Name3",
            "BeltCaptured", "BeltLost", "Defences" };
        if (required.Any(name => !existing.Rows.Columns.Contains(name)))
            throw new InvalidOperationException("Some required title-lineage columns are missing.");

        var rows = existing.Rows.Rows.Cast<DataRow>().OrderBy(r =>
            Convert.ToInt32(r["BeltOrder"], CultureInfo.InvariantCulture)).ToList();
        if (rows.Count != proposed.Count)
            throw new InvalidOperationException(
                $"Championship {beltId} has {rows.Count} existing lineage slots; " +
                $"the proposal has {proposed.Count}. Exactly one proposal per existing slot " +
                "is required. New history records are not created.");

        var orderNumbers = rows.Select(r => Convert.ToInt32(r["BeltOrder"],
            CultureInfo.InvariantCulture)).ToArray();
        if (orderNumbers.Distinct().Count() != rows.Count)
            throw new InvalidOperationException("Duplicate BeltOrder values need correction in TEW IX first.");
        if (rows.Any(r => r["Holder3"] != DBNull.Value
            && Convert.ToInt32(r["Holder3"], CultureInfo.InvariantCulture) > 0))
            throw new InvalidOperationException(
                "This title has three-person reigns; automatic generation currently supports singles or tag teams.");

        var workers = ListWorkers().ToDictionary(w => w.Id);
        foreach (var reign in proposed)
        {
            if (!workers.TryGetValue(reign.Holder1, out var first)
                || !string.Equals(first.Name, reign.HolderName1, StringComparison.Ordinal)
                || (reign.Holder2 > 0 &&
                    (!workers.TryGetValue(reign.Holder2, out var second)
                    || !string.Equals(second.Name, reign.HolderName2, StringComparison.Ordinal)))
                || (reign.Holder2 <= 0 && !string.IsNullOrEmpty(reign.HolderName2)))
                throw new InvalidOperationException("Every generated holder must match an existing worker ID and name.");
            if (reign.Captured > reign.Lost || reign.Captured.Year < 1800
                || reign.Lost.Year > 2100 || reign.Defences < 0)
                throw new InvalidOperationException("A generated reign has invalid dates or defences.");
            if (reign.HolderName1.Length > 30 || reign.HolderName2.Length > 30)
                throw new InvalidOperationException(
                    "One of the worker names exceeds the title-history name field limit.");
        }
        for (int i = 0; i < proposed.Count; i++)
        {
            if (proposed[i].Order != i + 1)
                throw new InvalidOperationException("Proposed reigns must be ordered consecutively.");
            if (i > 0 && proposed[i].Captured <= proposed[i-1].Lost)
                throw new InvalidOperationException("Generated title reign dates cannot overlap.");
        }

        using var transaction = _connection.BeginTransaction();
        try
        {
            int updated = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                var p = proposed[i];
                var original = rows[i];
                // Keep no-third-holder sentinel unchanged (-1 or 0).
                int third = original["Holder3"] is DBNull ? 0 :
                    Convert.ToInt32(original["Holder3"], CultureInfo.InvariantCulture);
                using var cmd = new OleDbCommand(
                    "UPDATE [tblBeltPreHistory] SET " +
                    "[Holder1]=?, [Holder2]=?, [Holder3]=?, " +
                    "[Holder_Name1]=?, [Holder_Name2]=?, [Holder_Name3]=?, " +
                    "[BeltCaptured]=?, [BeltLost]=?, [Defences]=? " +
                    "WHERE [UID]=? AND [BeltUID]=? AND [BeltOrder]=?",
                    _connection, transaction);
                cmd.Parameters.Add("h1", OleDbType.Integer).Value = p.Holder1;
                cmd.Parameters.Add("h2", OleDbType.Integer).Value = p.Holder2 > 0
                    ? p.Holder2 : (original["Holder2"] is DBNull ? 0 :
                        Math.Min(0, Convert.ToInt32(original["Holder2"], CultureInfo.InvariantCulture)));
                cmd.Parameters.Add("h3", OleDbType.Integer).Value = third;
                cmd.Parameters.Add("name1", OleDbType.VarWChar).Value = p.HolderName1;
                cmd.Parameters.Add("name2", OleDbType.VarWChar).Value = p.Holder2 > 0
                    ? p.HolderName2 : "";
                cmd.Parameters.Add("name3", OleDbType.VarWChar).Value = "";
                cmd.Parameters.Add("captured", OleDbType.Date).Value = p.Captured;
                cmd.Parameters.Add("lost", OleDbType.Date).Value = p.Lost;
                cmd.Parameters.Add("defences", OleDbType.SmallInt).Value = p.Defences;
                cmd.Parameters.Add("uid", OleDbType.Integer).Value =
                    Convert.ToInt32(original["UID"], CultureInfo.InvariantCulture);
                cmd.Parameters.Add("belt", OleDbType.Integer).Value = beltId;
                cmd.Parameters.Add("order", OleDbType.SmallInt).Value = orderNumbers[i];
                var count = cmd.ExecuteNonQuery();
                if (count != 1)
                    throw new InvalidOperationException(
                        "History row identity changed during update. Transaction cancelled.");
                updated++;
            }
            transaction.Commit();
            return updated;
        }
        catch
        {
            try { transaction.Rollback(); } catch { /* preserve original failure */ }
            throw;
        }
    }

    // Table and key are resolved from this hardcoded, small allowlist, never arbitrary UI text.
    private static readonly Dictionary<string, string> SectionKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["tblWorker"] = "UID",
        ["tblWorkerSkill"] = "WorkerUID",
        ["tblWorkerOver"] = "WorkerUID",
        ["tblWorkerPhysical"] = "WorkerUID",
        ["tblWorkerBio"] = "UID",
        ["tblFed"] = "UID",
        ["tblBelt"] = "UID",
        ["tblCard"] = "UID",
        ["tblStable"] = "UID"
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

    // Save As uses a fresh filename, never overwrites either an existing copy or the original.
    public void CopyTo(string destinationFile, string? originalFile)
    {
        var destination = Path.GetFullPath(destinationFile.Trim());
        if (!destination.EndsWith(".mdb", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Working copies must be .mdb files.");
        if (AppSettings.SamePath(destination, PathOnDisk) ||
            AppSettings.SamePath(destination, originalFile))
            throw new InvalidOperationException("Choose a new destination, not the current copy or original.");
        if (File.Exists(destination))
            throw new InvalidOperationException("That database already exists. No file has been overwritten.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        _connection.Dispose();
        try { File.Copy(PathOnDisk, destination, overwrite: false); }
        finally { _connection = Connect(); }
    }

    public string Backup(string? customBackupFolder)
    {
        var folder = string.IsNullOrWhiteSpace(customBackupFolder)
            ? Path.Combine(ProjectFolder, "Backups")
            : Path.GetFullPath(customBackupFolder.Trim());

        // Validate backup destination before closing the live database connection.
        Directory.CreateDirectory(folder);
        var destination = Path.Combine(folder,
            Path.GetFileNameWithoutExtension(PathOnDisk) + "-" +
            DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) +
            "-" + Guid.NewGuid().ToString("N")[..6] + ".mdb");
        _connection.Dispose();
        try { File.Copy(PathOnDisk, destination, overwrite: false); }
        finally { _connection = Connect(); }
        return destination;
    }

    // Save directly modifies the already-selected working copy.
    // A backup is created only when the user explicitly chooses Create Backup.
    public int Save(int workerId, IReadOnlyCollection<FieldEdit> edits)
    {
        if (edits.Count == 0) return 0;
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
            return changed;
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
