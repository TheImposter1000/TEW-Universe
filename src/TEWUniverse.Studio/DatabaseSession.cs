using System.Data;
using System.Data.OleDb;
using System.Globalization;

namespace TEWUniverse.Studio;

internal sealed record WorkerItem(int Id, string Name, bool? IsActive, string? PictureName = null)
{
    public override string ToString() => $"{Name}  |  ID {Id}";
}

internal sealed record NpcNameSeed(string Name, string? GenderCode, string? RaceCode);

internal sealed record TeamCreationDraft(string Name, int Worker1, int Worker2, int CompanyId,
    int TypeCode, string Experience, string Formed, string Finisher, string FinisherDescription,
    bool Active);

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
                new[] { "Name", "Picture", "Profile", "Prestige", "Active",
                    // Presentation and descriptive fields only; foreign keys,
                    // ownership, holders and cross-table references stay locked.
                    "ShortName", "Initials", "Description", "Backdrop",
                    "Banner", "Logo", "Website", "URL" }),
            ["Events"] = new("Events", "tblCard",
                new[] { "Name", "Logo", "Dormant" }),
            ["Stables"] = new("Stables", "tblStable",
                new[] { "Name", "Logo", "Backdrop", "Profile", "Active", "Important" }),
            ["Tag Teams"] = new("Tag Teams", "tblTeam",
                new[] { "Name", "Worker1", "Worker2", "Fed", "Type", "Experience",
                    "Formed", "Finisher", "TagFinisherDesc", "Active" })
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

    // Session connects directly to the user's selected scenario MDB.
    // Close TEW IX's editor before saving to prevent concurrent modifications.
    private readonly record struct FileStamp(long Size, DateTime ModifiedUtc);
    private FileStamp _openedStamp;

    private static FileStamp GetStamp(string file)
    {
        var info = new FileInfo(file);
        info.Refresh();
        if (!info.Exists) throw new FileNotFoundException("The active database was removed.", file);
        return new(info.Length, info.LastWriteTimeUtc);
    }

    private void EnsureUnchanged()
    {
        if (_openedStamp != GetStamp(PathOnDisk))
            throw new InvalidOperationException(
                "The active MDB was changed outside TEW Universe Studio. No changes have been written. " +
                "Close TEW IX's database editor, choose Refresh Database, review the updated records, " +
                "and then make your changes again.");
    }
    private DatabaseSession(string path, string password)
    {
        PathOnDisk = path;
        _password = password;
        _connection = Connect();
        _openedStamp = GetStamp(PathOnDisk);
    }

    public static DatabaseSession OpenDatabase(string path, string password)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Choose an existing TEW IX scenario MDB.");
        var full = Path.GetFullPath(path.Trim());
        if (!File.Exists(full) || !full.EndsWith(".mdb", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Select an existing .mdb scenario database.");
        if (Path.GetFileName(full).Equals("TEW9Save.mdb", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "TEW9Save.mdb is a save-game database. Only scenario MDBs are supported.");
        if (new FileInfo(full).IsReadOnly)
            throw new InvalidOperationException("This MDB is marked read-only.");
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

    /// <summary>
    /// Name-only historical NPC source. Read tblGenerateWorker, not tblWorker;
    /// never create a worker record. The exact schema is checked at runtime.
    /// </summary>
    public List<NpcNameSeed> ReadGeneratedWorkerNames(string? genderCode = null, string? raceCode = null)
    {
        var schema = new DataTable("GeneratedWorkerSchema");
        using (var schemaCmd = new OleDbCommand("SELECT * FROM [tblGenerateWorker] WHERE 1 = 0", _connection))
        using (var adapter = new OleDbDataAdapter(schemaCmd))
            adapter.Fill(schema);

        var columns = schema.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToArray();
        string? Pick(params string[] options) => options.Select(x =>
            columns.FirstOrDefault(c => c.Equals(x, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(x => x != null);

        string? nameCol = Pick("Name", "WorkerName", "FullName", "Full_Name",
            "GeneratedName", "Worker_Name", "DisplayName");
        string? firstCol = Pick("FirstName", "First_Name", "Forename", "First");
        string? lastCol = Pick("LastName", "Last_Name", "Surname", "Last", "FamilyName");
        string? genderCol = Pick("Gender", "GenderID", "Gender_ID", "Sex");
        string? raceCol = Pick("Race", "RaceID", "Race_ID", "Ethnicity", "EthnicityID");

        if (nameCol == null && (firstCol == null || lastCol == null))
            throw new InvalidOperationException(
                "tblGenerateWorker was found, but a full name or first/last name pair " +
                "could not be identified. Columns: " + string.Join(", ", columns));
        if (!string.IsNullOrWhiteSpace(genderCode) && genderCol == null)
            throw new InvalidOperationException(
                "The NPC gender filter was requested but tblGenerateWorker has no recognized gender column. " +
                "Columns: " + string.Join(", ", columns));
        if (!string.IsNullOrWhiteSpace(raceCode) && raceCol == null)
            throw new InvalidOperationException(
                "The NPC race filter was requested but tblGenerateWorker has no recognized race column. " +
                "Columns: " + string.Join(", ", columns));

        var source = new DataTable("GeneratedNames");
        using (var cmd = new OleDbCommand("SELECT * FROM [tblGenerateWorker]", _connection))
        using (var adapter = new OleDbDataAdapter(cmd))
            adapter.Fill(source);

        var output = new List<NpcNameSeed>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DataRow row in source.Rows)
        {
            string Clean(string? column) => column == null || row[column] is DBNull ? "" :
                (Convert.ToString(row[column], CultureInfo.InvariantCulture) ?? "").Trim();
            string name = nameCol == null ? (Clean(firstCol) + " " + Clean(lastCol)).Trim() :
                Clean(nameCol);
            if (name.Length is < 2 or > 30 || name.Any(char.IsControl))
                continue;
            var gender = Clean(genderCol);
            var race = Clean(raceCol);
            if (!string.IsNullOrWhiteSpace(genderCode) &&
                !gender.Equals(genderCode.Trim(), StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.IsNullOrWhiteSpace(raceCode) &&
                !race.Equals(raceCode.Trim(), StringComparison.OrdinalIgnoreCase))
                continue;
            if (seen.Add(name))
                output.Add(new NpcNameSeed(name, gender.Length == 0 ? null : gender,
                    race.Length == 0 ? null : race));
        }
        if (output.Count == 0)
            throw new InvalidOperationException(
                "tblGenerateWorker returned no usable NPC names for the selected race/gender codes. " +
                "Check the codes and available columns: " + string.Join(", ", columns));
        return output;
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

    /// <summary>
    /// Belts have many numeric switches and codes that the simple existing-record
    /// editor previously exposed read-only. Permit supported scalar columns,
    /// retaining primary keys, internal record names, and unknown relationships
    /// as read-only. The two recognized linked references are checked below.
    /// </summary>
    public static bool CanEditTitleColumn(DataColumn column)
    {
        if (!IsEditable(column.DataType) || column.ReadOnly ||
            column.AutoIncrement || !string.IsNullOrEmpty(column.Expression))
            return false;

        var field = column.ColumnName;
        if (field.Equals("UID", StringComparison.OrdinalIgnoreCase) ||
            field.Equals("Recordname", StringComparison.OrdinalIgnoreCase))
            return false;

        // Explicitly mapped relationships can be edited after verifying
        // that their target actually exists. Other unknown relationship keys
        // are kept read-only until their semantics can be confirmed.
        if (field.Equals("OriginalFed", StringComparison.OrdinalIgnoreCase) ||
            field.Equals("CardUID", StringComparison.OrdinalIgnoreCase))
            return true;
        if (field.EndsWith("UID", StringComparison.OrdinalIgnoreCase) ||
            field.EndsWith("_ID", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private void ValidateTitleReference(FieldEdit edit)
    {
        string? referencedTable = edit.Field switch
        {
            "OriginalFed" => "tblFed",
            "CardUID" => "tblCard",
            _ => null
        };
        if (referencedTable == null) return;

        // Zero and -1 appear as unassigned-link sentinels in TEW databases.
        if (!int.TryParse(edit.NewText, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int target))
            throw new InvalidOperationException(
                $"{edit.Field} must contain an existing record ID, 0, or -1.");
        if (target is 0 or -1) return;
        if (target < -1)
            throw new InvalidOperationException(
                $"{edit.Field} cannot refer to record {target}.");

        using var cmd = new OleDbCommand(
            $"SELECT COUNT(*) FROM [{referencedTable}] WHERE [UID] = ?", _connection);
        cmd.Parameters.Add("linkedId", OleDbType.Integer).Value = target;
        if (Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) != 1)
            throw new InvalidOperationException(
                $"{edit.Field} refers to ID {target}, but that record was not found in {referencedTable}. " +
                "Choose an existing record, or use a known unassigned value.");
    }


    /// <summary>Available TEW IX codes, read from actual team records rather than guessed.</summary>
    public List<int> ListTagTeamTypeCodes()
    {
        using var cmd = new OleDbCommand(
            "SELECT DISTINCT [Type] FROM [tblTeam] WHERE [Type] IS NOT NULL ORDER BY [Type]",
            _connection);
        using var reader = cmd.ExecuteReader();
        var codes = new List<int>();
        while (reader?.Read() == true)
            codes.Add(Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture));
        return codes;
    }

    public Type GetTagTeamColumnType(string column)
    {
        if (column != "Formed")
            throw new ArgumentException("Only the verified Formed column can be queried.");
        using var cmd = new OleDbCommand("SELECT * FROM [tblTeam] WHERE 1 = 0", _connection);
        using var reader = cmd.ExecuteReader(CommandBehavior.SchemaOnly);
        if (reader == null)
            throw new InvalidOperationException("Could not read the tblTeam schema.");
        for (int i = 0; i < reader.FieldCount; i++)
            if (reader.GetName(i).Equals(column, StringComparison.OrdinalIgnoreCase))
                return reader.GetFieldType(i);
        throw new InvalidOperationException("Missing tblTeam field: " + column);
    }

    public List<int> ListTagTeamGlobalCompanyCodes()
    {
        using var cmd = new OleDbCommand(
            "SELECT DISTINCT [Fed] FROM [tblTeam] WHERE [Fed] IN (0, -1)",
            _connection);
        using var reader = cmd.ExecuteReader();
        var codes = new List<int>();
        while (reader?.Read() == true)
            codes.Add(Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture));
        return codes;
    }

    private bool RecordExists(string verifiedTable, int uid)
    {
        // Only callers with hard-coded table names use this method.
        using var cmd = new OleDbCommand(
            $"SELECT COUNT(*) FROM [{verifiedTable}] WHERE [UID] = ?", _connection);
        cmd.Parameters.Add("id", OleDbType.Integer).Value = uid;
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
    }

    private void ValidateTagTeamReferences(int worker1, int worker2, int companyId, int typeCode,
        IReadOnlyCollection<int> availableCodes)
    {
        if (worker1 <= 0 || worker2 <= 0 || worker1 == worker2 ||
            !RecordExists("tblWorker", worker1) || !RecordExists("tblWorker", worker2))
            throw new InvalidOperationException("Select two different existing workers.");
        if (companyId > 0)
        {
            if (!RecordExists("tblFed", companyId))
                throw new InvalidOperationException("The selected company does not exist in tblFed.");
        }
        else if (companyId is 0 or -1)
        {
            if (!ListTagTeamGlobalCompanyCodes().Contains(companyId))
                throw new InvalidOperationException(
                    "This database has no verified game-wide team using that None/Company code. " +
                    "Choose an existing company instead.");
        }
        else throw new InvalidOperationException("Invalid company reference.");

        if (!availableCodes.Contains(typeCode))
            throw new InvalidOperationException(
                "The team type is not one of the codes observed in this database.");
    }

    private void ValidateTagTeamEdits(DataRow original, IReadOnlyCollection<FieldEdit> edits)
    {
        int Value(string column)
        {
            var edit = edits.FirstOrDefault(x => x.Field.Equals(column,
                StringComparison.OrdinalIgnoreCase));
            return edit == null
                ? Convert.ToInt32(original[column], CultureInfo.InvariantCulture)
                : int.Parse(edit.NewText, CultureInfo.InvariantCulture);
        }
        int a = Value("Worker1"), b = Value("Worker2"), company = Value("Fed");
        int type = Value("Type");
        ValidateTagTeamReferences(a, b, company, type, ListTagTeamTypeCodes());
        var name = edits.FirstOrDefault(x => x.Field.Equals("Name",
            StringComparison.OrdinalIgnoreCase));
        if (name != null && string.IsNullOrWhiteSpace(name.NewText))
            throw new InvalidOperationException("A tag team needs a name.");
    }

    /// <summary>
    /// Create one real tblTeam entry. All linked IDs and the database's schema
    /// are checked before entering a transaction. No other table is modified.
    /// </summary>
    public int CreateTagTeam(TeamCreationDraft draft)
    {
        var teamName = draft.Name.Trim();
        if (teamName.Length == 0 || teamName.Any(char.IsControl))
            throw new InvalidOperationException("Enter a valid tag team name.");
        ValidateTagTeamReferences(draft.Worker1, draft.Worker2, draft.CompanyId,
            draft.TypeCode, ListTagTeamTypeCodes());

        // Resolve all data types from the active MDB. Unknown mandatory columns
        // are never filled with guessed values.
        var schema = new DataTable("tblTeam");
        using (var cmd = new OleDbCommand("SELECT * FROM [tblTeam] WHERE 1 = 0", _connection))
        using (var adapter = new OleDbDataAdapter(cmd))
            adapter.FillSchema(schema, SchemaType.Source);
        string[] mandatory = { "UID", "Recordname", "Name", "Worker1", "Worker2",
            "Fed", "Type", "Experience", "Formed", "Finisher", "TagFinisherDesc", "Active" };
        var missing = mandatory.Where(name => !schema.Columns.Contains(name)).ToArray();
        if (missing.Length != 0)
            throw new InvalidOperationException(
                "Creating tag teams is unsupported for this MDB schema. Missing: " +
                string.Join(", ", missing));
        var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) {
            ["Name"] = teamName,
            ["Worker1"] = draft.Worker1, ["Worker2"] = draft.Worker2,
            ["Fed"] = draft.CompanyId, ["Type"] = draft.TypeCode,
            ["Experience"] = Parse(draft.Experience.Trim(), schema.Columns["Experience"]!.DataType),
            ["Formed"] = Parse(draft.Formed.Trim(), schema.Columns["Formed"]!.DataType),
            ["Finisher"] = draft.Finisher.Trim(),
            ["TagFinisherDesc"] = draft.FinisherDescription.Trim(),
            ["Active"] = draft.Active
        };
        foreach (var pair in values)
        {
            var column = schema.Columns[pair.Key]!;
            if (column.MaxLength > 0 && pair.Value is string text && text.Length > column.MaxLength)
                throw new InvalidOperationException(
                    $"{pair.Key} exceeds its MDB limit of {column.MaxLength} characters.");
            if (pair.Value == DBNull.Value && !column.AllowDBNull)
                throw new InvalidOperationException(
                    $"{pair.Key} requires a value in this database.");
            if (!IsEditable(column.DataType))
                throw new InvalidOperationException(
                    $"Unsupported field type for tblTeam.{pair.Key}: {column.DataType.Name}.");
        }
        var used = new HashSet<string>(values.Keys, StringComparer.OrdinalIgnoreCase) {
            "UID", "Recordname"
        };
        foreach (DataColumn c in schema.Columns)
            if (!used.Contains(c.ColumnName) && !c.AutoIncrement &&
                !c.AllowDBNull && c.DefaultValue is DBNull)
                throw new InvalidOperationException(
                    $"Unknown required tblTeam column '{c.ColumnName}'. " +
                    "Creation is blocked until its TEW IX meaning is verified.");

        EnsureUnchanged();
        using var tx = _connection.BeginTransaction();
        try
        {
            // A pair is unordered in TEW; do not create a duplicate partnership
            // for the same company (or the same verified game-wide sentinel).
            using (var duplicatePair = new OleDbCommand(
                "SELECT COUNT(*) FROM [tblTeam] WHERE [Fed]=? AND " +
                "(([Worker1]=? AND [Worker2]=?) OR ([Worker1]=? AND [Worker2]=?))",
                _connection, tx))
            {
                duplicatePair.Parameters.Add("company", OleDbType.Integer).Value = draft.CompanyId;
                duplicatePair.Parameters.Add("first", OleDbType.Integer).Value = draft.Worker1;
                duplicatePair.Parameters.Add("second", OleDbType.Integer).Value = draft.Worker2;
                duplicatePair.Parameters.Add("reversedFirst", OleDbType.Integer).Value = draft.Worker2;
                duplicatePair.Parameters.Add("reversedSecond", OleDbType.Integer).Value = draft.Worker1;
                if (Convert.ToInt32(duplicatePair.ExecuteScalar(), CultureInfo.InvariantCulture) != 0)
                    throw new InvalidOperationException(
                        "This pair already has a team in the selected company. " +
                        "Edit the existing tag team instead.");
            }
            int id;
            using (var next = new OleDbCommand("SELECT MAX([UID]) FROM [tblTeam]", _connection, tx))
            {
                var value = next.ExecuteScalar();
                id = checked((value is null or DBNull ? 0 :
                    Convert.ToInt32(value, CultureInfo.InvariantCulture)) + 1);
            }
            if (id < 1) throw new InvalidOperationException("Could not allocate a team UID.");
            string recordName = $"TUSTEAM{id:D8}";
            int maxName = schema.Columns["Recordname"]!.MaxLength;
            if (maxName > 0 && recordName.Length > maxName)
                throw new InvalidOperationException("Generated Recordname exceeds MDB length limit.");
            using (var duplicate = new OleDbCommand(
                "SELECT COUNT(*) FROM [tblTeam] WHERE [Recordname] = ?", _connection, tx))
            {
                duplicate.Parameters.Add("record", OleDbType.VarWChar).Value = recordName;
                if (Convert.ToInt32(duplicate.ExecuteScalar(), CultureInfo.InvariantCulture) != 0)
                    throw new InvalidOperationException("The generated Recordname already exists.");
            }
            values["Recordname"] = recordName;
            bool autoUid = schema.Columns["UID"]!.AutoIncrement;
            if (!autoUid) values["UID"] = id;
            var cols = values.Keys.ToArray();
            using var insert = new OleDbCommand(
                "INSERT INTO [tblTeam] (" + string.Join(",", cols.Select(x => $"[{x}]")) +
                ") VALUES (" + string.Join(",", cols.Select(_ => "?")) + ")", _connection, tx);
            foreach (string name in cols)
                insert.Parameters.Add(name, ToOleDbType(schema.Columns[name]!.DataType)).Value =
                    values[name];
            if (insert.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("The team INSERT did not write exactly one row.");
            if (autoUid)
            {
                using var identity = new OleDbCommand("SELECT @@IDENTITY", _connection, tx);
                id = Convert.ToInt32(identity.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
            if (id < 1) throw new InvalidOperationException("The new team UID was invalid.");
            tx.Commit();
            _openedStamp = GetStamp(PathOnDisk);
            return id;
        }
        catch
        {
            try { tx.Rollback(); } catch { /* Preserve original exception. */ }
            throw;
        }
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

        foreach (var edit in edits)
        {
            if (!edit.Table.Equals(spec.Table, StringComparison.OrdinalIgnoreCase) ||
                !edit.Key.Equals("UID", StringComparison.OrdinalIgnoreCase) ||
                !sectionData.Data.Columns.Contains(edit.Field))
                throw new InvalidOperationException(
                    "The requested edit does not belong to the selected existing record.");

            var column = sectionData.Data.Columns[edit.Field]!;
            if (column.DataType != edit.FieldType ||
                (section == "Titles"
                    ? !CanEditTitleColumn(column)
                    : !allowed.Contains(edit.Field)))
                throw new InvalidOperationException(
                    $"Editing {edit.Field} is not supported or would modify a protected identifier.");

            if (section == "Titles")
                ValidateTitleReference(edit);
        }
        if (section == "Tag Teams")
            ValidateTagTeamEdits(sectionData.Data.Rows[0], edits);

        // Parameterized transactional updates to the already-selected MDB.
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

        // Recognize known legacy field names, never arbitrary user SQL identifiers.
        string ResolveName(string a, string b) =>
            existing.Rows.Columns.Contains(a) ? a :
            existing.Rows.Columns.Contains(b) ? b :
            throw new InvalidOperationException("Missing title-history field " + a + " / " + b);
        string holderName1 = ResolveName("Holder_Name1", "HolderName1");
        string holderName2 = ResolveName("Holder_Name2", "HolderName2");
        string holderName3 = ResolveName("Holder_Name3", "HolderName3");
        string[] required = { "UID", "BeltUID", "BeltOrder", "Holder1", "Holder2",
            "Holder3", "BeltCaptured", "BeltLost", "Defences" };
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
        // Historical names without a database worker are linked as -1.
        // Real worker IDs may legitimately have an alternative ring name.
        foreach (var reign in proposed)
        {
            if ((reign.Holder1 != -1 &&
                    (reign.Holder1 <= 0 || !workers.ContainsKey(reign.Holder1))) ||
                (reign.Holder2 > 0 && !workers.ContainsKey(reign.Holder2)) ||
                reign.Holder2 < -1)
                throw new InvalidOperationException(
                    "A holder must have an existing worker UID or -1 for name-only history.");
            if (string.IsNullOrWhiteSpace(reign.HolderName1) ||
                (reign.Holder2 > 0 && string.IsNullOrWhiteSpace(reign.HolderName2)) ||
                (reign.Holder2 <= 0 && !string.IsNullOrWhiteSpace(reign.HolderName2)))
                throw new InvalidOperationException(
                    "Each historical holder needs a display name.");
            if (reign.Captured > reign.Lost || reign.Captured.Year < 1800 ||
                reign.Lost.Year > 2100 || reign.Defences < 0)
                throw new InvalidOperationException("A generated reign has invalid dates or defences.");
            if (reign.HolderName1.Length > 30 || reign.HolderName2.Length > 30 ||
                reign.HolderName1.Any(char.IsControl) || reign.HolderName2.Any(char.IsControl))
                throw new InvalidOperationException(
                    "Historical champion names must be at most 30 characters.");
        }
        if (rows.Any(r => r["Holder2"] != DBNull.Value &&
                Convert.ToInt32(r["Holder2"], CultureInfo.InvariantCulture) > 0)
            && proposed.Any(r => r.Holder2 <= 0))
            throw new InvalidOperationException(
                "A singles-only proposal cannot replace a championship's existing tag-team reigns.");

        for (int i = 0; i < proposed.Count; i++)
        {
            if (proposed[i].Order != i + 1)
                throw new InvalidOperationException("Proposed reigns must be ordered consecutively.");
            if (i > 0 && proposed[i].Captured <= proposed[i-1].Lost)
                throw new InvalidOperationException("Generated title reign dates cannot overlap.");
        }

        EnsureUnchanged();
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
                    $"[{holderName1}]=?, [{holderName2}]=?, [{holderName3}]=?, " +
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
            _openedStamp = GetStamp(PathOnDisk);
            return updated;
        }
        catch
        {
            try { transaction.Rollback(); } catch { /* preserve original failure */ }
            throw;
        }
    }

    /// <summary>
    /// Full lineage rebuild for the selected title only. Unlike the previous
    /// existing-slot updater, this creates new tblBeltPreHistory records.
    /// The caller must require an explicit backup and destructive confirmation.
    /// Transaction rolls back DELETE and all INSERTs if any insertion fails.
    /// No changes to tblWorker or tblGenerateWorker are ever made.
    /// </summary>
    public int RebuildTitleLineage(int beltId, IReadOnlyList<GeneratedTitleReign> proposed,
        int expectedExistingRows, bool annualTournament = false)
    {
        if (beltId <= 0 || proposed.Count is < 1 or > 500)
            throw new InvalidOperationException("Choose a valid title and 1–500 reigns.");
        var workers = ListWorkers().Select(x => x.Id).ToHashSet();
        for (int i = 0; i < proposed.Count; i++)
        {
            var reign = proposed[i];
            if (reign.Order != i + 1 ||
                (reign.Holder1 != -1 && !workers.Contains(reign.Holder1)) ||
                reign.Holder2 != -1 || !string.IsNullOrEmpty(reign.HolderName2) ||
                string.IsNullOrWhiteSpace(reign.HolderName1) ||
                reign.HolderName1.Length > 30 || reign.HolderName1.Any(char.IsControl) ||
                reign.Captured.Year is < 1800 or > 2100 ||
                (annualTournament
                    ? (reign.Lost != new DateTime(1666, 1, 1) || reign.Defences != -1)
                    : (reign.Lost.Year is < 1800 or > 2100 ||
                       reign.Captured > reign.Lost || reign.Defences < 0)))
                throw new InvalidOperationException(
                    $"Generated reign #{i + 1} is invalid or contains an unknown linked worker.");
            if (i > 0 && (annualTournament
                ? reign.Captured <= proposed[i-1].Captured
                : reign.Captured <= proposed[i-1].Lost))
                throw new InvalidOperationException(
                    "Generated capture and loss dates must be chronological and non-overlapping.");
        }

        var history = ReadTitleLineage(beltId);
        if (history.LinkField != "BeltUID" || history.OrderField != "BeltOrder")
            throw new InvalidOperationException(
                "Cannot rebuild history: BeltUID or BeltOrder schema is unverified.");
        if (history.Rows.Rows.Count != expectedExistingRows)
            throw new InvalidOperationException(
                "The title history changed after the preview was generated. Generate a fresh preview.");
        var available = history.Rows.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToArray();
        string NameCol(string a, string b) =>
            available.Contains(a, StringComparer.OrdinalIgnoreCase) ? a :
            available.Contains(b, StringComparer.OrdinalIgnoreCase) ? b :
            throw new InvalidOperationException("Missing expected title history field: " + a);
        string n1 = NameCol("Holder_Name1", "HolderName1");
        string n2 = NameCol("Holder_Name2", "HolderName2");
        string n3 = NameCol("Holder_Name3", "HolderName3");
        string[] mandatory = { "UID", "Recordname", "BeltUID", "BeltOrder", "Holder1",
            "Holder2", "Holder3", "BeltCaptured", "BeltLost", "Defences" };
        if (mandatory.Any(c => !available.Contains(c, StringComparer.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                "Title history is missing a mandatory field. Detected: " +
                string.Join(", ", available));

        // DataTable schema supplies actual max lengths and nullability.
        var schema = new DataTable();
        using (var schemaCmd = new OleDbCommand(
            "SELECT * FROM [tblBeltPreHistory] WHERE 1 = 0", _connection))
        using (var adapter = new OleDbDataAdapter(schemaCmd))
            adapter.FillSchema(schema, SchemaType.Source);

        var insertedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        EnsureUnchanged();
        using var tx = _connection.BeginTransaction();
        try
        {
            using (var countCmd = new OleDbCommand(
                "SELECT COUNT(*) FROM [tblBeltPreHistory] WHERE [BeltUID]=?",
                _connection, tx))
            {
                countCmd.Parameters.Add("belt", OleDbType.Integer).Value = beltId;
                if (Convert.ToInt32(countCmd.ExecuteScalar(), CultureInfo.InvariantCulture) != expectedExistingRows)
                    throw new InvalidOperationException(
                        "Title history count changed during generation. No rows were modified.");
            }

            var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var names = new OleDbCommand(
                "SELECT [Recordname] FROM [tblBeltPreHistory] WHERE [BeltUID] <> ?",
                _connection, tx))
            {
                names.Parameters.Add("belt", OleDbType.Integer).Value = beltId;
                using var reader = names.ExecuteReader();
                while (reader?.Read() == true)
                    if (!reader.IsDBNull(0))
                        existingNames.Add(Convert.ToString(reader.GetValue(0),
                            CultureInfo.InvariantCulture) ?? "");
            }
            int maxUid;
            using (var maxCmd = new OleDbCommand(
                "SELECT MAX([UID]) FROM [tblBeltPreHistory]", _connection, tx))
            {
                var value = maxCmd.ExecuteScalar();
                maxUid = value is DBNull or null ? 0 :
                    Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            bool autonumber = schema.Columns["UID"]?.AutoIncrement == true;
            var cols = new[] { "Recordname", "BeltUID", "BeltOrder", "Holder1",
                "Holder2", "Holder3", n1, n2, n3, "BeltCaptured", "BeltLost", "Defences" };
            var inserted = autonumber ? cols : new[] { "UID" }.Concat(cols).ToArray();
            var used = inserted.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (DataColumn column in schema.Columns)
            {
                if (used.Contains(column.ColumnName) || column.AutoIncrement) continue;
                if (!column.AllowDBNull && column.DefaultValue is DBNull)
                    throw new InvalidOperationException(
                        $"Additional required column {column.ColumnName} has no default. " +
                        "No records have been changed; inspect the table schema first.");
            }

            using (var deleteCmd = new OleDbCommand(
                "DELETE FROM [tblBeltPreHistory] WHERE [BeltUID]=?", _connection, tx))
            {
                deleteCmd.Parameters.Add("belt", OleDbType.Integer).Value = beltId;
                int removed = deleteCmd.ExecuteNonQuery();
                if (removed != expectedExistingRows)
                    throw new InvalidOperationException(
                        "Unexpected number of existing title reigns. Rollback initiated.");
            }

            // Column names are only validated MDB schema fields or fixed literals.
            string sql = "INSERT INTO [tblBeltPreHistory] (" +
                string.Join(", ", inserted.Select(c => "[" + c + "]")) +
                ") VALUES (" + string.Join(", ", inserted.Select(_ => "?")) + ")";
            for (int i = 0; i < proposed.Count; i++)
            {
                var p = proposed[i];
                var baseName = $"TUS{beltId:D5}_{i+1:D5}";
                var recordName = baseName;
                for (int suffix = 1; existingNames.Contains(recordName) ||
                    insertedNames.Contains(recordName); suffix++)
                    recordName = baseName + "_" + suffix.ToString(CultureInfo.InvariantCulture);
                int limit = schema.Columns["Recordname"]?.MaxLength ?? -1;
                if (limit > 0 && recordName.Length > limit)
                    throw new InvalidOperationException("Title history Recordname exceeds the MDB text limit.");
                insertedNames.Add(recordName);

                using var cmd = new OleDbCommand(sql, _connection, tx);
                // OLE DB uses positional parameters. Values must match the
                // exact INSERT column order, even if named placeholders appear.
                if (!autonumber)
                    cmd.Parameters.Add("uid", OleDbType.Integer).Value = checked(maxUid + i + 1);
                cmd.Parameters.Add("record", OleDbType.VarWChar).Value = recordName;
                cmd.Parameters.Add("belt", OleDbType.Integer).Value = beltId;
                cmd.Parameters.Add("order", OleDbType.SmallInt).Value = p.Order;
                cmd.Parameters.Add("h1", OleDbType.Integer).Value = p.Holder1;
                cmd.Parameters.Add("h2", OleDbType.Integer).Value = -1;
                cmd.Parameters.Add("h3", OleDbType.Integer).Value = -1;
                cmd.Parameters.Add("n1", OleDbType.VarWChar).Value = p.HolderName1;
                cmd.Parameters.Add("n2", OleDbType.VarWChar).Value = "";
                cmd.Parameters.Add("n3", OleDbType.VarWChar).Value = "";
                cmd.Parameters.Add("captured", OleDbType.Date).Value = p.Captured;
                cmd.Parameters.Add("lost", OleDbType.Date).Value = p.Lost;
                cmd.Parameters.Add("defences", OleDbType.SmallInt).Value = p.Defences;
                if (cmd.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException(
                        "An inserted reign did not write exactly one record. Rollback initiated.");
            }
            tx.Commit();
            _openedStamp = GetStamp(PathOnDisk);
            return proposed.Count;
        }
        catch
        {
            try { tx.Rollback(); } catch { /* preserve original exception */ }
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
        ["tblStable"] = "UID",
        ["tblTeam"] = "UID"
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

    // Export Copy As makes a separate file without changing the active database.
    public void CopyTo(string destinationFile)
    {
        var destination = Path.GetFullPath(destinationFile.Trim());
        if (!destination.EndsWith(".mdb", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Working copies must be .mdb files.");
        if (AppSettings.SamePath(destination, PathOnDisk))
            throw new InvalidOperationException("The export cannot replace the active database.");
        if (File.Exists(destination))
            throw new InvalidOperationException("That database already exists. No file has been overwritten.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        EnsureUnchanged();
        _connection.Dispose();
        try { File.Copy(PathOnDisk, destination, overwrite: false); }
        finally { _connection = Connect(); _openedStamp = GetStamp(PathOnDisk); }
    }

    public string Backup(string? customBackupFolder)
    {
        var folder = string.IsNullOrWhiteSpace(customBackupFolder)
            ? Path.Combine(ProjectFolder, "Backups")
            : Path.GetFullPath(customBackupFolder.Trim());

        // Backups remain a separate user-requested operation.
        EnsureUnchanged();
        Directory.CreateDirectory(folder);
        var destination = Path.Combine(folder,
            Path.GetFileNameWithoutExtension(PathOnDisk) + "-" +
            DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) +
            "-" + Guid.NewGuid().ToString("N")[..6] + ".mdb");
        _connection.Dispose();
        try { File.Copy(PathOnDisk, destination, overwrite: false); }
        finally { _connection = Connect(); _openedStamp = GetStamp(PathOnDisk); }
        return destination;
    }

    // Save directly modifies the active database selected explicitly by the user.
    // A backup is created only when the user explicitly chooses Create Backup.
    public int Save(int workerId, IReadOnlyCollection<FieldEdit> edits)
    {
        if (edits.Count == 0) return 0;
        EnsureUnchanged();
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
            _openedStamp = GetStamp(PathOnDisk);
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
