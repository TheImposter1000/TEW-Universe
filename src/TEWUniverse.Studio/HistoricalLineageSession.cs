using System.Data;
using System.Data.OleDb;
using System.Globalization;

namespace TEWUniverse.Studio;

/// <summary>
/// A title-history entry can contain actual workers or name-only people.
/// -1 means no linked tblWorker row; the separate Holder_Name field carries the name.
/// </summary>
internal sealed record HistoricalReignDraft(
    int[] HolderIds, string[] HolderNames, DateTime Captured, DateTime Lost, short Defences);

internal sealed partial class DatabaseSession
{
    private static (string First, string Second, string Third) HistoricalNames(DataTable columns)
    {
        string Find(string a, string b) =>
            columns.Columns.Contains(a) ? a :
            columns.Columns.Contains(b) ? b :
            throw new InvalidOperationException("Missing historical name column: " + a);
        return (Find("Holder_Name1", "HolderName1"),
            Find("Holder_Name2", "HolderName2"), Find("Holder_Name3", "HolderName3"));
    }

    private static void ValidateHistoricalSchema(TitleLineageResult history)
    {
        if (history.LinkField != "BeltUID" || history.OrderField != "BeltOrder")
            throw new InvalidOperationException("Unrecognized BeltUID or BeltOrder schema. No changes made.");
        foreach (var name in new[] { "UID", "Recordname", "BeltUID", "BeltOrder",
                     "Holder1", "Holder2", "Holder3", "BeltCaptured", "BeltLost", "Defences" })
            if (!history.Rows.Columns.Contains(name))
                throw new InvalidOperationException("Missing required history field: " + name);
        HistoricalNames(history.Rows);
    }

    private void ValidateHistoricalDraft(HistoricalReignDraft draft, int memberCount, bool annual)
    {
        if (memberCount is < 1 or > 3 ||
            draft.HolderIds.Length != 3 || draft.HolderNames.Length != 3)
            throw new InvalidOperationException("Invalid holder count for this title.");
        HistoricalReignRules.ValidateHolders(draft.HolderIds, draft.HolderNames,
            memberCount, ListWorkers().Select(w => w.Id).ToHashSet());
        if (draft.Captured.Year is < 1800 or > 2100)
            throw new InvalidOperationException("Use a capture year between 1800 and 2100.");
        if (annual)
        {
            if (draft.Lost != new DateTime(1666, 1, 1) || draft.Defences != -1)
                throw new InvalidOperationException(
                    "Annual tournaments use the 01/01/1666 loss sentinel and -1 defences.");
        }
        else if (draft.Lost.Year is < 1800 or > 2100 ||
                 draft.Captured > draft.Lost || draft.Defences < 0)
            throw new InvalidOperationException("Invalid championship loss date or defense count.");
    }

    /// <summary>
    /// Change the champions on one existing historical reign without changing the
    /// underlying reign's dates, UID, order, defenses, or any tblWorker row.
    /// </summary>
    public void EditHistoricalReign(int beltId, int originalUid, int originalOrder,
        HistoricalReignDraft draft, int memberCount, bool annual)
    {
        ValidateHistoricalDraft(draft, memberCount, annual);
        var history = ReadTitleLineage(beltId);
        ValidateHistoricalSchema(history);
        var row = history.Rows.Rows.Cast<DataRow>().SingleOrDefault(r =>
            Convert.ToInt32(r["UID"], CultureInfo.InvariantCulture) == originalUid &&
            Convert.ToInt32(r["BeltOrder"], CultureInfo.InvariantCulture) == originalOrder);
        if (row == null)
            throw new InvalidOperationException("The selected reign has changed. Reload title lineage.");
        if (row["BeltCaptured"] is not DateTime captured ||
            row["BeltLost"] is not DateTime lost ||
            captured != draft.Captured || lost != draft.Lost ||
            Convert.ToInt16(row["Defences"], CultureInfo.InvariantCulture) != draft.Defences)
            throw new InvalidOperationException("Reign dates or defenses changed. Reload before editing.");
        var names = HistoricalNames(history.Rows);
        EnsureUnchanged();
        using var tx = _connection.BeginTransaction();
        try
        {
            // Optimistic check protects the selected row; no other title records are changed.
            using var command = new OleDbCommand(
                "UPDATE [tblBeltPreHistory] SET " +
                "[Holder1]=?, [Holder2]=?, [Holder3]=?, " +
                $"[{names.First}]=?, [{names.Second}]=?, [{names.Third}]=? " +
                "WHERE [UID]=? AND [BeltUID]=? AND [BeltOrder]=? " +
                "AND [BeltCaptured]=? AND [BeltLost]=? AND [Defences]=?",
                _connection, tx);
            for (int i = 0; i < 3; i++)
                command.Parameters.Add("holder" + i, OleDbType.Integer).Value = draft.HolderIds[i];
            for (int i = 0; i < 3; i++)
                command.Parameters.Add("name" + i, OleDbType.VarWChar).Value =
                    draft.HolderNames[i].Trim();
            command.Parameters.Add("uid", OleDbType.Integer).Value = originalUid;
            command.Parameters.Add("belt", OleDbType.Integer).Value = beltId;
            command.Parameters.Add("order", OleDbType.SmallInt).Value = originalOrder;
            command.Parameters.Add("captured", OleDbType.Date).Value = captured;
            command.Parameters.Add("lost", OleDbType.Date).Value = lost;
            command.Parameters.Add("defences", OleDbType.SmallInt).Value = draft.Defences;
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("The targeted history row changed. Nothing saved.");
            tx.Commit();
            _openedStamp = GetStamp(PathOnDisk);
        }
        catch
        {
            try { tx.Rollback(); } catch { }
            throw;
        }
    }

    /// <summary>
    /// Insert exactly one chronological historical reign. Re-number BeltOrder for
    /// this title inside the same transaction; preserve every existing UID, date,
    /// holder and Recordname. Never create a tblWorker or tblTeam record.
    /// </summary>
    public int InsertHistoricalReign(int beltId, HistoricalReignDraft draft,
        int memberCount, bool annual, int expectedRowCount)
    {
        ValidateHistoricalDraft(draft, memberCount, annual);
        var history = ReadTitleLineage(beltId);
        ValidateHistoricalSchema(history);
        var title = ReadSection("tblBelt", beltId);
        if (!title.Exists || !title.Data.Columns.Contains("Name"))
            throw new InvalidOperationException("The title name could not be verified.");
        string titleName = Convert.ToString(title.Data.Rows[0]["Name"],
            CultureInfo.InvariantCulture)?.Trim() ?? "";
        if (titleName.Length == 0)
            throw new InvalidOperationException("Cannot insert lineage for an unnamed championship.");
        var ordered = history.Rows.Rows.Cast<DataRow>()
            .OrderBy(r => Convert.ToInt32(r["BeltOrder"], CultureInfo.InvariantCulture)).ToList();
        if (ordered.Count != expectedRowCount || ordered.Count >= 500)
            throw new InvalidOperationException("Title history changed or exceeds 500 entries.");
        var orderValues = ordered.Select(r =>
            Convert.ToInt32(r["BeltOrder"], CultureInfo.InvariantCulture)).ToArray();
        if (orderValues.Distinct().Count() != ordered.Count ||
            orderValues.Any(n => n < 1 || n > 20000))
            throw new InvalidOperationException("Unsupported existing BeltOrder values.");
        var spans = ordered.Select(r => (
            Captured: r["BeltCaptured"] is DateTime start ? start :
                throw new InvalidOperationException("An existing capture date is invalid."),
            Lost: r["BeltLost"] is DateTime end ? end :
                throw new InvalidOperationException("An existing loss date is invalid.")
        )).ToArray();
        int insertIndex = HistoricalReignRules.InsertionPosition(
            spans, draft.Captured, draft.Lost, annual);

        var schema = new DataTable("tblBeltPreHistory");
        using (var command = new OleDbCommand(
            "SELECT * FROM [tblBeltPreHistory] WHERE 1=0", _connection))
        using (var adapter = new OleDbDataAdapter(command))
            adapter.FillSchema(schema, SchemaType.Source);
        var names = HistoricalNames(history.Rows);
        bool autoUid = schema.Columns["UID"]?.AutoIncrement == true;
        var cols = new[] { "Recordname", "BeltUID", "BeltOrder", "Holder1",
            "Holder2", "Holder3", names.First, names.Second, names.Third,
            "BeltCaptured", "BeltLost", "Defences" };
        var inserted = autoUid ? cols : new[] { "UID" }.Concat(cols).ToArray();
        var used = inserted.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (DataColumn column in schema.Columns)
            if (!used.Contains(column.ColumnName) && !column.AutoIncrement &&
                !column.AllowDBNull && column.DefaultValue is DBNull)
                throw new InvalidOperationException(
                    "Unsupported required title-history column: " + column.ColumnName);

        EnsureUnchanged();
        using var tx = _connection.BeginTransaction();
        try
        {
            using (var count = new OleDbCommand(
                "SELECT COUNT(*) FROM [tblBeltPreHistory] WHERE [BeltUID]=?", _connection, tx))
            {
                count.Parameters.Add("belt", OleDbType.Integer).Value = beltId;
                if (Convert.ToInt32(count.ExecuteScalar(), CultureInfo.InvariantCulture) != expectedRowCount)
                    throw new InvalidOperationException("History changed before insertion. Reload.");
            }
            int uid;
            using (var max = new OleDbCommand(
                "SELECT MAX([UID]) FROM [tblBeltPreHistory]", _connection, tx))
            {
                object? value = max.ExecuteScalar();
                uid = checked((value is DBNull or null ? 0 :
                    Convert.ToInt32(value, CultureInfo.InvariantCulture)) + 1);
            }

            // Stage existing orders above the old maximum so a unique index, if
            // present, cannot conflict with the final reordered positions.
            int stageOffset = Math.Max(1000, (orderValues.Length == 0 ? 0 : orderValues.Max()) + 1);
            int temporaryInsertOrder = checked(stageOffset +
                (orderValues.Length == 0 ? 0 : orderValues.Max()) + 1);
            if (temporaryInsertOrder > short.MaxValue)
                throw new InvalidOperationException("BeltOrder is too large for safe insertion.");
            for (int i = 0; i < ordered.Count; i++)
            {
                using var stage = new OleDbCommand(
                    "UPDATE [tblBeltPreHistory] SET [BeltOrder]=? WHERE [UID]=? AND [BeltUID]=?",
                    _connection, tx);
                stage.Parameters.Add("order", OleDbType.SmallInt).Value = orderValues[i] + stageOffset;
                stage.Parameters.Add("uid", OleDbType.Integer).Value =
                    Convert.ToInt32(ordered[i]["UID"], CultureInfo.InvariantCulture);
                stage.Parameters.Add("belt", OleDbType.Integer).Value = beltId;
                if (stage.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException("Cannot safely re-index title history.");
            }

            // Recordname must be unique across ALL titles, not only this one.
            var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var namesCmd = new OleDbCommand(
                "SELECT [Recordname] FROM [tblBeltPreHistory]", _connection, tx))
            using (var reader = namesCmd.ExecuteReader())
                while (reader?.Read() == true && !reader.IsDBNull(0))
                    reserved.Add(Convert.ToString(reader.GetValue(0),
                        CultureInfo.InvariantCulture) ?? "");
            int sequence = 1;
            string recordName;
            do { recordName = StudioRecordNames.TitleHistory(titleName, sequence++); }
            while (reserved.Contains(recordName));
            int recordNameLimit = schema.Columns["Recordname"]?.MaxLength ?? -1;
            if (recordNameLimit > 0 && recordName.Length > recordNameLimit)
                throw new InvalidOperationException("Generated history Recordname exceeds the MDB limit.");

            using (var insert = new OleDbCommand(
                "INSERT INTO [tblBeltPreHistory] (" +
                string.Join(",", inserted.Select(n => $"[{n}]")) +
                ") VALUES (" + string.Join(",", inserted.Select(_ => "?")) + ")",
                _connection, tx))
            {
                if (!autoUid) insert.Parameters.Add("uid", OleDbType.Integer).Value = uid;
                insert.Parameters.Add("record", OleDbType.VarWChar).Value = recordName;
                insert.Parameters.Add("belt", OleDbType.Integer).Value = beltId;
                insert.Parameters.Add("order", OleDbType.SmallInt).Value = temporaryInsertOrder;
                for (int i = 0; i < 3; i++)
                    insert.Parameters.Add("holder" + i, OleDbType.Integer).Value = draft.HolderIds[i];
                for (int i = 0; i < 3; i++)
                    insert.Parameters.Add("name" + i, OleDbType.VarWChar).Value = draft.HolderNames[i].Trim();
                insert.Parameters.Add("captured", OleDbType.Date).Value = draft.Captured;
                insert.Parameters.Add("lost", OleDbType.Date).Value = draft.Lost;
                insert.Parameters.Add("defences", OleDbType.SmallInt).Value = draft.Defences;
                if (insert.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException("The historical reign was not inserted.");
                if (autoUid)
                {
                    using var identity = new OleDbCommand("SELECT @@IDENTITY", _connection, tx);
                    uid = Convert.ToInt32(identity.ExecuteScalar(), CultureInfo.InvariantCulture);
                }
            }

            for (int i = 0; i < ordered.Count; i++)
            {
                int newOrder = i + 1 + (i >= insertIndex ? 1 : 0);
                using var move = new OleDbCommand(
                    "UPDATE [tblBeltPreHistory] SET [BeltOrder]=? WHERE [UID]=? AND [BeltUID]=?",
                    _connection, tx);
                move.Parameters.Add("order", OleDbType.SmallInt).Value = newOrder;
                move.Parameters.Add("uid", OleDbType.Integer).Value =
                    Convert.ToInt32(ordered[i]["UID"], CultureInfo.InvariantCulture);
                move.Parameters.Add("belt", OleDbType.Integer).Value = beltId;
                if (move.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException("Failed to restore chronological history order.");
            }
            using (var place = new OleDbCommand(
                "UPDATE [tblBeltPreHistory] SET [BeltOrder]=? WHERE [UID]=? AND [BeltUID]=?",
                _connection, tx))
            {
                place.Parameters.Add("order", OleDbType.SmallInt).Value = insertIndex + 1;
                place.Parameters.Add("uid", OleDbType.Integer).Value = uid;
                place.Parameters.Add("belt", OleDbType.Integer).Value = beltId;
                if (place.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException("Could not place the new historic reign.");
            }
            tx.Commit();
            _openedStamp = GetStamp(PathOnDisk);
            return uid;
        }
        catch
        {
            try { tx.Rollback(); } catch { }
            throw;
        }
    }
}
