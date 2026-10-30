using System.Data;
using System.Data.OleDb;
using System.Globalization;

namespace TEWUniverse.Studio;

internal sealed record ChemistryEntry(
    int Uid, string RecordName, int Worker1, int Worker2,
    string Worker1Name, string Worker2Name, int RelType, int Quality)
{
    public string PairLabel => Worker1Name + " / " + Worker2Name;
    public string TypeLabel => "Code " + RelType.ToString(CultureInfo.InvariantCulture);
    public string QualityLabel => "Code " + Quality.ToString(CultureInfo.InvariantCulture);
    public string SearchText => PairLabel + " " + RecordName + " " + Uid + " " +
        Worker1 + " " + Worker2 + " " + TypeLabel + " " + QualityLabel;
}

internal sealed partial class DatabaseSession
{
    private const string ChemistryTable = "tblChemistryForce";
    private const string ChemistryName = "RecordName";
    private static readonly string[] ChemistryColumns = {
        "UID", ChemistryName, "Worker1", "Worker2", "RelType", "Quality"
    };

    private DataTable VerifiedChemistrySchema()
    {
        var schema = new DataTable(ChemistryTable);
        using (var select = new OleDbCommand(
            "SELECT * FROM [tblChemistryForce] WHERE 1=0", _connection))
        using (var adapter = new OleDbDataAdapter(select))
            adapter.FillSchema(schema, SchemaType.Source);
        foreach (string name in ChemistryColumns)
        {
            if (!schema.Columns.Contains(name))
                throw new InvalidOperationException(
                    "The chemistry table is missing its expected " + name + " column.");
            Type type = schema.Columns[name]!.DataType;
            if (name == ChemistryName ? type != typeof(string) :
                type != typeof(int) && type != typeof(short) &&
                type != typeof(byte) && type != typeof(long))
                throw new InvalidOperationException(
                    "Unsupported tblChemistryForce column type for " + name);
        }
        return schema;
    }

    private static object ChemistryNumber(DataColumn col, int n)
    {
        try { return Convert.ChangeType(n, col.DataType, CultureInfo.InvariantCulture); }
        catch (Exception e) when (e is OverflowException or FormatException)
        {
            throw new InvalidOperationException("Invalid " + col.ColumnName + " numeric value.", e);
        }
    }

    public List<ChemistryEntry> ReadChemistry()
    {
        VerifiedChemistrySchema();
        var names = ListWorkers().ToDictionary(w => w.Id, w => w.Name);
        var result = new List<ChemistryEntry>();
        using var command = new OleDbCommand(
            "SELECT [UID], [RecordName], [Worker1], [Worker2], [RelType], [Quality] " +
            "FROM [tblChemistryForce] ORDER BY [UID]", _connection);
        using var reader = command.ExecuteReader();
        if (reader == null) return result;
        while (reader.Read())
        {
            int Value(string field) => Convert.ToInt32(reader[field], CultureInfo.InvariantCulture);
            int first = Value("Worker1"), second = Value("Worker2");
            string Label(int id) => names.TryGetValue(id, out var n) ? n : "Missing worker " + id;
            result.Add(new ChemistryEntry(Value("UID"),
                reader[ChemistryName] is DBNull ? "" :
                    Convert.ToString(reader[ChemistryName], CultureInfo.InvariantCulture) ?? "",
                first, second, Label(first), Label(second),
                Value("RelType"), Value("Quality")));
        }
        return result;
    }

    private static void CheckChemistrySnapshot(IDataRecord row, ChemistryEntry old)
    {
        int Val(string field) => Convert.ToInt32(row[field], CultureInfo.InvariantCulture);
        string name = row[ChemistryName] is DBNull ? "" :
            Convert.ToString(row[ChemistryName], CultureInfo.InvariantCulture) ?? "";
        if (Val("UID") != old.Uid || Val("Worker1") != old.Worker1 ||
            Val("Worker2") != old.Worker2 || Val("RelType") != old.RelType ||
            Val("Quality") != old.Quality || name != old.RecordName)
            throw new InvalidOperationException(
                "This chemistry record changed outside the editor. Refresh before writing.");
    }

    private void EnsureChemistryUnique(OleDbTransaction tx,
        int first, int second, int type, int excludeUid = -1)
    {
        using var dupe = new OleDbCommand(
            "SELECT [UID] FROM [tblChemistryForce] WHERE [RelType]=? AND " +
            "(([Worker1]=? AND [Worker2]=?) OR ([Worker1]=? AND [Worker2]=?))",
            _connection, tx);
        foreach (int value in new[] { type, first, second, second, first })
            dupe.Parameters.Add("code", OleDbType.Integer).Value = value;
        using var reader = dupe.ExecuteReader();
        while (reader?.Read() == true)
            if (Convert.ToInt32(reader[0], CultureInfo.InvariantCulture) != excludeUid)
                throw new InvalidOperationException(
                    "This worker pair already has chemistry of this type. Edit the existing record.");
    }

    private static void ValidateChemistryCodeChoice(ChemistryEntry? existing,
        int type, int quality, IReadOnlyList<ChemistryEntry> all)
    {
        // An existing unknown value can be left untouched; changed values
        // must be observed in this specific database, not guessed.
        if ((existing == null || type != existing.RelType) &&
            !all.Any(x => x.RelType == type))
            throw new InvalidOperationException("Unverified chemistry RelType code.");
        if ((existing == null || quality != existing.Quality) &&
            !all.Any(x => x.Quality == quality))
            throw new InvalidOperationException("Unverified chemistry Quality code.");
    }

    public int CreateChemistry(int first, int second, int type, int quality)
    {
        DataTable schema = VerifiedChemistrySchema();
        var workers = ListWorkers();
        ChemistryRules.ValidatePair(first, second, workers.Select(w => w.Id).ToHashSet());
        var prior = ReadChemistry();
        ValidateChemistryCodeChoice(null, type, quality, prior);
        foreach (DataColumn col in schema.Columns)
            if (!ChemistryColumns.Contains(col.ColumnName, StringComparer.OrdinalIgnoreCase) &&
                !col.AutoIncrement && !col.AllowDBNull && col.DefaultValue is DBNull)
                throw new InvalidOperationException(
                    "Unknown required chemistry field: " + col.ColumnName);
        EnsureUnchanged();
        using var tx = _connection.BeginTransaction();
        try
        {
            EnsureChemistryUnique(tx, first, second, type);
            int uid;
            using (var max = new OleDbCommand(
                "SELECT MAX([UID]) FROM [tblChemistryForce]", _connection, tx))
            {
                object? raw = max.ExecuteScalar();
                uid = checked((raw is null or DBNull ? 0 :
                    Convert.ToInt32(raw, CultureInfo.InvariantCulture)) + 1);
            }
            var names = new List<string>();
            using (var cmd = new OleDbCommand(
                "SELECT [RecordName] FROM [tblChemistryForce]", _connection, tx))
            using (var reader = cmd.ExecuteReader())
                while (reader?.Read() == true)
                    if (!reader.IsDBNull(0))
                        names.Add(Convert.ToString(reader[0], CultureInfo.InvariantCulture) ?? "");
            string firstName = workers.First(w => w.Id == first).Name;
            string secondName = workers.First(w => w.Id == second).Name;
            string recordName = ChemistryRules.RecordName(
                firstName, secondName, type, names, schema.Columns[ChemistryName]!.MaxLength);
            bool auto = schema.Columns["UID"]!.AutoIncrement;
            string[] columns = auto
                ? new[] { ChemistryName, "Worker1", "Worker2", "RelType", "Quality" }
                : ChemistryColumns;
            using var insert = new OleDbCommand(
                "INSERT INTO [tblChemistryForce] (" +
                string.Join(",", columns.Select(x => "[" + x + "]")) + ") VALUES (" +
                string.Join(",", columns.Select(_ => "?")) + ")", _connection, tx);
            foreach (string col in columns)
                insert.Parameters.Add(col, ToOleDbType(schema.Columns[col]!.DataType)).Value =
                    col == ChemistryName ? recordName :
                    ChemistryNumber(schema.Columns[col]!, col switch {
                        "UID" => uid, "Worker1" => first, "Worker2" => second,
                        "RelType" => type, "Quality" => quality, _ => throw new InvalidOperationException()
                    });
            if (insert.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("Could not insert the chemistry preset.");
            if (auto)
            {
                using var identity = new OleDbCommand("SELECT @@IDENTITY", _connection, tx);
                uid = Convert.ToInt32(identity.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
            tx.Commit();
            _openedStamp = GetStamp(PathOnDisk);
            return uid;
        }
        catch { try { tx.Rollback(); } catch { } throw; }
    }

    public void UpdateChemistry(ChemistryEntry old, int type, int quality)
    {
        DataTable schema = VerifiedChemistrySchema();
        ValidateChemistryCodeChoice(old, type, quality, ReadChemistry());
        EnsureUnchanged();
        using var tx = _connection.BeginTransaction();
        try
        {
            using (var check = new OleDbCommand(
                "SELECT * FROM [tblChemistryForce] WHERE [UID]=?", _connection, tx))
            {
                check.Parameters.Add("uid", ToOleDbType(schema.Columns["UID"]!.DataType))
                    .Value = ChemistryNumber(schema.Columns["UID"]!, old.Uid);
                using var reader = check.ExecuteReader();
                if (reader == null || !reader.Read()) throw new InvalidOperationException("Record not found.");
                CheckChemistrySnapshot(reader, old);
                if (reader.Read()) throw new InvalidOperationException("Duplicate chemistry UID.");
            }
            EnsureChemistryUnique(tx, old.Worker1, old.Worker2, type, old.Uid);
            using var update = new OleDbCommand(
                "UPDATE [tblChemistryForce] SET [RelType]=?, [Quality]=? WHERE [UID]=?",
                _connection, tx);
            foreach (var (col, n) in new[] { ("RelType", type), ("Quality", quality), ("UID", old.Uid) })
                update.Parameters.Add(col, ToOleDbType(schema.Columns[col]!.DataType))
                    .Value = ChemistryNumber(schema.Columns[col]!, n);
            if (update.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("Chemistry update did not affect one row.");
            tx.Commit();
            _openedStamp = GetStamp(PathOnDisk);
        }
        catch { try { tx.Rollback(); } catch { } throw; }
    }

    public void DeleteChemistry(ChemistryEntry old)
    {
        DataTable schema = VerifiedChemistrySchema();
        EnsureUnchanged();
        using var tx = _connection.BeginTransaction();
        try
        {
            using (var check = new OleDbCommand(
                "SELECT * FROM [tblChemistryForce] WHERE [UID]=?", _connection, tx))
            {
                check.Parameters.Add("uid", ToOleDbType(schema.Columns["UID"]!.DataType))
                    .Value = ChemistryNumber(schema.Columns["UID"]!, old.Uid);
                using var reader = check.ExecuteReader();
                if (reader == null || !reader.Read()) throw new InvalidOperationException("Record not found.");
                CheckChemistrySnapshot(reader, old);
                if (reader.Read()) throw new InvalidOperationException("Duplicate chemistry UID.");
            }
            using var remove = new OleDbCommand(
                "DELETE FROM [tblChemistryForce] WHERE [UID]=?", _connection, tx);
            remove.Parameters.Add("uid", ToOleDbType(schema.Columns["UID"]!.DataType))
                .Value = ChemistryNumber(schema.Columns["UID"]!, old.Uid);
            if (remove.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("Chemistry delete did not affect one row.");
            tx.Commit();
            _openedStamp = GetStamp(PathOnDisk);
        }
        catch { try { tx.Rollback(); } catch { } throw; }
    }
}
