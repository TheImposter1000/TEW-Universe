using System.Data;
using System.Data.OleDb;
using System.Globalization;

namespace TEWUniverse.Studio;

/// <summary>
/// One actual tblRelation row. Personal, romantic, family and Mentor can coexist.
/// The original worker order is preserved when editing or deleting this record.
/// </summary>
internal sealed record RelationshipEntry(
    int Uid, string RecordName, int Worker1, int Worker2,
    string Worker1Name, string Worker2Name,
    int Personal, int Romantic, int Family, bool Mentor)
{
    public string PairLabel => Worker1Name + " / " + Worker2Name;
    public string PersonalLabel => TewCodeLabels.Display("tblRelation", "P_Rel", Personal.ToString(CultureInfo.InvariantCulture));
    public string RomanticLabel => TewCodeLabels.Display("tblRelation", "R_Rel", Romantic.ToString(CultureInfo.InvariantCulture));
    public string FamilyLabel => TewCodeLabels.Display("tblRelation", "F_Rel", Family.ToString(CultureInfo.InvariantCulture));
    public string MentorLabel => Mentor ? "Yes" : "No";
    public string CategorySummary
    {
        get
        {
            var labels = new List<string>();
            if (Personal != 0) labels.Add("Personal: " + PersonalLabel);
            if (Romantic != 0) labels.Add("Romantic: " + RomanticLabel);
            if (Family != 0) labels.Add("Family: " + FamilyLabel);
            if (Mentor) labels.Add("Mentor");
            return labels.Count == 0 ? "None" : string.Join("; ", labels);
        }
    }
}

internal sealed partial class DatabaseSession
{
    private static readonly string[] RelationshipColumns = {
        "UID", "Worker1", "Worker2", "P_Rel", "R_Rel", "F_Rel", "Mentor"
    };
    private static bool RelationshipNumber(Type t) =>
        t == typeof(byte) || t == typeof(short) || t == typeof(int) || t == typeof(long);

    private (DataTable Schema, string NameColumn) VerifiedRelationshipSchema()
    {
        var schema = new DataTable("tblRelation");
        using (var select = new OleDbCommand("SELECT * FROM [tblRelation] WHERE 1=0", _connection))
        using (var adapter = new OleDbDataAdapter(select))
            adapter.FillSchema(schema, SchemaType.Source);
        foreach (string name in RelationshipColumns)
        {
            if (!schema.Columns.Contains(name) ||
                (name == "Mentor"
                    ? schema.Columns[name]!.DataType != typeof(bool)
                    : !RelationshipNumber(schema.Columns[name]!.DataType)))
                throw new InvalidOperationException("Unsupported tblRelation column: " + name);
        }
        string nameField = schema.Columns.Contains("Name") ? "Name" :
            schema.Columns.Contains("Recordname") ? "Recordname" :
            throw new InvalidOperationException("tblRelation has no verified name column.");
        if (schema.Columns[nameField]!.DataType != typeof(string))
            throw new InvalidOperationException("tblRelation name column is not text.");
        return (schema, nameField);
    }

    public List<RelationshipEntry> ReadRelationships()
    {
        var (_, nameField) = VerifiedRelationshipSchema();
        var people = ListWorkers().ToDictionary(w => w.Id, w => w.Name);
        var records = new List<RelationshipEntry>();
        using var cmd = new OleDbCommand("SELECT * FROM [tblRelation] ORDER BY [UID]", _connection);
        using var reader = cmd.ExecuteReader();
        if (reader == null) return records;
        while (reader.Read())
        {
            int Id(string column) => Convert.ToInt32(reader[column], CultureInfo.InvariantCulture);
            int worker1 = Id("Worker1");
            int worker2 = Id("Worker2");
            string Label(int id) => people.TryGetValue(id, out string? name)
                ? name : "Missing worker UID " + id;
            records.Add(new RelationshipEntry(Id("UID"),
                reader[nameField] is DBNull ? "" :
                    Convert.ToString(reader[nameField], CultureInfo.InvariantCulture) ?? "",
                worker1, worker2, Label(worker1), Label(worker2),
                Id("P_Rel"), Id("R_Rel"), Id("F_Rel"),
                Convert.ToBoolean(reader["Mentor"], CultureInfo.InvariantCulture)));
        }
        return records;
    }

    private static object NumericColumnValue(DataColumn column, int code)
    {
        if (!RelationshipNumber(column.DataType))
            throw new InvalidOperationException("Relationship code column is not an integer.");
        try { return Convert.ChangeType(code, column.DataType, CultureInfo.InvariantCulture); }
        catch (Exception ex) when (ex is OverflowException or FormatException)
        {
            throw new InvalidOperationException(
                "Relationship code does not fit " + column.ColumnName, ex);
        }
    }

    private static void VerifyRelationshipSnapshot(IDataRecord reader,
        RelationshipEntry original, string nameField)
    {
        int Int(string field) => Convert.ToInt32(reader[field], CultureInfo.InvariantCulture);
        string name = reader[nameField] is DBNull ? "" :
            Convert.ToString(reader[nameField], CultureInfo.InvariantCulture) ?? "";
        if (Int("UID") != original.Uid || Int("Worker1") != original.Worker1 ||
            Int("Worker2") != original.Worker2 || Int("P_Rel") != original.Personal ||
            Int("R_Rel") != original.Romantic || Int("F_Rel") != original.Family ||
            Convert.ToBoolean(reader["Mentor"], CultureInfo.InvariantCulture) != original.Mentor ||
            name != original.RecordName)
            throw new InvalidOperationException(
                "This relationship has changed. Reload before editing or deleting.");
    }

    private static void ValidateRelationshipChanges(RelationshipEntry original,
        int personal, int romantic, int family, bool mentor)
    {
        // Unknown existing codes may be retained unchanged, but cannot be newly assigned.
        foreach (var pair in new[] {
            ("P_Rel", original.Personal, personal),
            ("R_Rel", original.Romantic, romantic),
            ("F_Rel", original.Family, family)
        })
            if (pair.Item2 != pair.Item3 &&
                !TewCodeLabels.Choices("tblRelation", pair.Item1)
                    .Any(x => x.Code == pair.Item3))
                throw new InvalidOperationException(
                    "Cannot set unverified code " + pair.Item3 + " in " + pair.Item1);
        if (personal == 0 && romantic == 0 && family == 0 && !mentor)
            throw new InvalidOperationException(
                "Use Delete Relationship to remove a record with no active elements.");
    }

    public void UpdateRelationship(RelationshipEntry original,
        int personal, int romantic, int family, bool mentor)
    {
        var (schema, nameField) = VerifiedRelationshipSchema();
        ValidateRelationshipChanges(original, personal, romantic, family, mentor);
        EnsureUnchanged();
        using var tx = _connection.BeginTransaction();
        try
        {
            using (var check = new OleDbCommand(
                "SELECT * FROM [tblRelation] WHERE [UID]=?", _connection, tx))
            {
                check.Parameters.Add("uid", ToOleDbType(schema.Columns["UID"]!.DataType))
                    .Value = NumericColumnValue(schema.Columns["UID"]!, original.Uid);
                using var reader = check.ExecuteReader();
                if (reader == null || !reader.Read())
                    throw new InvalidOperationException("Relationship record no longer exists.");
                VerifyRelationshipSnapshot(reader, original, nameField);
                if (reader.Read())
                    throw new InvalidOperationException("Duplicate relationship UID. Nothing saved.");
            }
            using var update = new OleDbCommand(
                "UPDATE [tblRelation] SET [P_Rel]=?, [R_Rel]=?, [F_Rel]=?, [Mentor]=? WHERE [UID]=?",
                _connection, tx);
            foreach (var (col, value) in new[] {
                ("P_Rel", personal), ("R_Rel", romantic), ("F_Rel", family)
            })
                update.Parameters.Add(col, ToOleDbType(schema.Columns[col]!.DataType))
                    .Value = NumericColumnValue(schema.Columns[col]!, value);
            update.Parameters.Add("mentor", OleDbType.Boolean).Value = mentor;
            update.Parameters.Add("uid", ToOleDbType(schema.Columns["UID"]!.DataType))
                .Value = NumericColumnValue(schema.Columns["UID"]!, original.Uid);
            if (update.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("Relationship changed while saving. No edits committed.");
            tx.Commit();
            _openedStamp = GetStamp(PathOnDisk);
        }
        catch
        {
            try { tx.Rollback(); } catch { }
            throw;
        }
    }

    public int CreateRelationship(int worker1, int worker2,
        int personal, int romantic, int family, bool mentor)
    {
        var (schema, nameField) = VerifiedRelationshipSchema();
        RelationshipRules.ValidatePair(worker1, worker2,
            ListWorkers().Select(w => w.Id).ToHashSet());
        RelationshipRules.ValidateCodes(personal, romantic, family, mentor);
        var cols = new[] { nameField, "Worker1", "Worker2",
            "P_Rel", "R_Rel", "F_Rel", "Mentor" };
        bool autoUid = schema.Columns["UID"]!.AutoIncrement;
        var used = cols.Append("UID").ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (DataColumn col in schema.Columns)
            if (!used.Contains(col.ColumnName) && !col.AutoIncrement &&
                !col.AllowDBNull && col.DefaultValue is DBNull)
                throw new InvalidOperationException(
                    "Unknown required tblRelation field: " + col.ColumnName);

        EnsureUnchanged();
        using var tx = _connection.BeginTransaction();
        try
        {
            using (var dupe = new OleDbCommand(
                "SELECT COUNT(*) FROM [tblRelation] WHERE " +
                "([Worker1]=? AND [Worker2]=?) OR ([Worker1]=? AND [Worker2]=?)",
                _connection, tx))
            {
                foreach (int id in new[] { worker1, worker2, worker2, worker1 })
                    dupe.Parameters.Add("worker", OleDbType.Integer).Value = id;
                if (Convert.ToInt32(dupe.ExecuteScalar(), CultureInfo.InvariantCulture) > 0)
                    throw new InvalidOperationException(
                        "This worker pair already has a relationship record. Edit it instead.");
            }
            int nextUid;
            using (var max = new OleDbCommand("SELECT MAX([UID]) FROM [tblRelation]", _connection, tx))
            {
                object? raw = max.ExecuteScalar();
                nextUid = checked((raw is DBNull or null ? 0 :
                    Convert.ToInt32(raw, CultureInfo.InvariantCulture)) + 1);
            }
            if (nextUid < 1)
                throw new InvalidOperationException("Could not allocate relationship UID.");
            string internalName = $"TUSREL{nextUid:D8}";
            if (schema.Columns[nameField]!.MaxLength > 0 &&
                internalName.Length > schema.Columns[nameField]!.MaxLength)
                throw new InvalidOperationException("Generated relationship name exceeds MDB text length.");
            using (var exists = new OleDbCommand(
                $"SELECT COUNT(*) FROM [tblRelation] WHERE [{nameField}]=?", _connection, tx))
            {
                exists.Parameters.Add("name", OleDbType.VarWChar).Value = internalName;
                if (Convert.ToInt32(exists.ExecuteScalar(), CultureInfo.InvariantCulture) > 0)
                    throw new InvalidOperationException("Generated relationship record name is already used.");
            }
            var insertionCols = autoUid ? cols : new[] { "UID" }.Concat(cols).ToArray();
            using (var insert = new OleDbCommand(
                "INSERT INTO [tblRelation] (" +
                string.Join(", ", insertionCols.Select(x => "[" + x + "]")) +
                ") VALUES (" + string.Join(", ", insertionCols.Select(_ => "?")) + ")",
                _connection, tx))
            {
                if (!autoUid)
                    insert.Parameters.Add("uid", ToOleDbType(schema.Columns["UID"]!.DataType))
                        .Value = NumericColumnValue(schema.Columns["UID"]!, nextUid);
                insert.Parameters.Add("name", OleDbType.VarWChar).Value = internalName;
                foreach (var (col, value) in new[] { ("Worker1", worker1), ("Worker2", worker2),
                    ("P_Rel", personal), ("R_Rel", romantic), ("F_Rel", family) })
                    insert.Parameters.Add(col, ToOleDbType(schema.Columns[col]!.DataType))
                        .Value = NumericColumnValue(schema.Columns[col]!, value);
                insert.Parameters.Add("mentor", OleDbType.Boolean).Value = mentor;
                if (insert.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException("Relationship insertion did not write one row.");
            }
            int created = nextUid;
            if (autoUid)
            {
                using var identity = new OleDbCommand("SELECT @@IDENTITY", _connection, tx);
                created = Convert.ToInt32(identity.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
            tx.Commit();
            _openedStamp = GetStamp(PathOnDisk);
            return created;
        }
        catch
        {
            try { tx.Rollback(); } catch { }
            throw;
        }
    }

    public void DeleteRelationship(RelationshipEntry original)
    {
        var (schema, nameField) = VerifiedRelationshipSchema();
        EnsureUnchanged();
        using var tx = _connection.BeginTransaction();
        try
        {
            using (var check = new OleDbCommand(
                "SELECT * FROM [tblRelation] WHERE [UID]=?", _connection, tx))
            {
                check.Parameters.Add("uid", ToOleDbType(schema.Columns["UID"]!.DataType))
                    .Value = NumericColumnValue(schema.Columns["UID"]!, original.Uid);
                using var reader = check.ExecuteReader();
                if (reader == null || !reader.Read())
                    throw new InvalidOperationException("Relationship no longer exists.");
                VerifyRelationshipSnapshot(reader, original, nameField);
                if (reader.Read())
                    throw new InvalidOperationException("Duplicate UID; deletion is blocked.");
            }
            using var command = new OleDbCommand(
                "DELETE FROM [tblRelation] WHERE [UID]=?", _connection, tx);
            command.Parameters.Add("uid", ToOleDbType(schema.Columns["UID"]!.DataType))
                .Value = NumericColumnValue(schema.Columns["UID"]!, original.Uid);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("Relationship changed during deletion.");
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
