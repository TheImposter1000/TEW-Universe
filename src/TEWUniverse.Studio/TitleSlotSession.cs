using System.Data.OleDb;
using System.Globalization;

namespace TEWUniverse.Studio;

/// <summary>
/// Only identity and holder fields are mutable in a targeted replacement.
/// Captured/Lost/Defences are included for optimistic verification, never SET.
/// </summary>
internal sealed record TitleLineageSlotUpdate(
    int Uid, int Order, DateTime Captured, DateTime Lost, short Defences,
    int Holder1, string Name1, int Holder2, string Name2, int Holder3, string Name3);

internal sealed partial class DatabaseSession
{
    public int? ReadTitleOriginalCompany(int beltId)
    {
        var section = ReadSection("tblBelt", beltId);
        if (!section.Exists || !section.Data.Columns.Contains("OriginalFed"))
            return null;
        object value = section.Data.Rows[0]["OriginalFed"];
        if (value is DBNull) return null;
        int companyId = Convert.ToInt32(value, CultureInfo.InvariantCulture);
        return companyId > 0 ? companyId : null;
    }

    /// <summary>
    /// Replace ONLY name-only unlinked holder slots, leaving dates, defenses,
    /// order, recordname and every linked worker record entirely untouched.
    /// A single transaction protects all user-selected substitutions.
    /// </summary>
    public int PatchTitleLineageSlots(int beltId,
        IReadOnlyList<TitleLineageSlotUpdate> updates,
        int expectedExistingRows, int expectedMembers)
    {
        if (beltId <= 0 || updates.Count == 0 ||
            expectedMembers is < 1 or > 3)
            throw new InvalidOperationException(
                "Choose an existing title, valid format and nonempty targeted substitution.");

        var existing = ReadTitleLineage(beltId);
        if (!string.Equals(existing.LinkField, "BeltUID", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(existing.OrderField, "BeltOrder", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "The title lineage cannot be patched without verified BeltUID and BeltOrder columns.");
        var columns = existing.Rows.Columns;
        string Resolve(string first, string second) =>
            columns.Contains(first) ? first :
            columns.Contains(second) ? second :
            throw new InvalidOperationException("Required holder-name column is absent: " + first);
        string name1 = Resolve("Holder_Name1", "HolderName1");
        string name2 = Resolve("Holder_Name2", "HolderName2");
        string name3 = Resolve("Holder_Name3", "HolderName3");
        string[] required = { "UID", "BeltUID", "BeltOrder",
            "Holder1", "Holder2", "Holder3",
            "BeltCaptured", "BeltLost", "Defences" };
        if (required.Any(c => !columns.Contains(c)))
            throw new InvalidOperationException(
                "This MDB has an unsupported history schema. No rows were edited.");
        if (existing.Rows.Rows.Count != expectedExistingRows)
            throw new InvalidOperationException(
                "The existing title-history row count changed after preview. Regenerate first.");

        var originals = existing.Rows.Rows.Cast<System.Data.DataRow>()
            .ToDictionary(row => Convert.ToInt32(row["UID"], CultureInfo.InvariantCulture));
        if (updates.Select(u => u.Uid).Distinct().Count() != updates.Count)
            throw new InvalidOperationException(
                "The same original title-history slot cannot be replaced twice.");

        var availableWorkers = ListWorkers().Select(w => w.Id).ToHashSet();
        foreach (var patch in updates)
        {
            if (!originals.TryGetValue(patch.Uid, out var row) ||
                Convert.ToInt32(row["BeltOrder"], CultureInfo.InvariantCulture) != patch.Order ||
                row["BeltCaptured"] is not DateTime captured ||
                row["BeltLost"] is not DateTime lost ||
                captured != patch.Captured || lost != patch.Lost ||
                Convert.ToInt16(row["Defences"], CultureInfo.InvariantCulture) != patch.Defences)
                throw new InvalidOperationException(
                    $"History slot #{patch.Order} changed since the preview. Generate again.");

            int OriginalHolder(string field) => row[field] is DBNull ? -1 :
                Convert.ToInt32(row[field], CultureInfo.InvariantCulture);
            if (OriginalHolder("Holder1") > 0 ||
                OriginalHolder("Holder2") > 0 ||
                OriginalHolder("Holder3") > 0)
                throw new InvalidOperationException(
                    $"History slot #{patch.Order} contains an existing real worker. " +
                    "Only name-only NPC placeholder reigns may be replaced.");

            int[] ids = { patch.Holder1, patch.Holder2, patch.Holder3 };
            string[] names = { patch.Name1, patch.Name2, patch.Name3 };
            HistoricalReignRules.ValidateHolders(
                ids, names, expectedMembers, availableWorkers);
            if (!ids.Take(expectedMembers).Any(id => id > 0))
                throw new InvalidOperationException(
                    $"History slot #{patch.Order} needs at least one linked worker " +
                    "for selective replacement. Named historical partners can use UID -1.");
        }

        EnsureUnchanged();
        using var tx = _connection.BeginTransaction();
        try
        {
            foreach (var patch in updates)
            {
                using var cmd = new OleDbCommand(
                    "UPDATE [tblBeltPreHistory] SET " +
                    "[Holder1]=?, [Holder2]=?, [Holder3]=?, " +
                    $"[{name1}]=?, [{name2}]=?, [{name3}]=? " +
                    "WHERE [UID]=? AND [BeltUID]=? AND [BeltOrder]=? " +
                    "AND ([Holder1] IS NULL OR [Holder1]<=0) " +
                    "AND ([Holder2] IS NULL OR [Holder2]<=0) " +
                    "AND ([Holder3] IS NULL OR [Holder3]<=0) " +
                    "AND [BeltCaptured]=? AND [BeltLost]=? AND [Defences]=?",
                    _connection, tx);
                cmd.Parameters.Add("holder1", OleDbType.Integer).Value = patch.Holder1;
                cmd.Parameters.Add("holder2", OleDbType.Integer).Value = patch.Holder2;
                cmd.Parameters.Add("holder3", OleDbType.Integer).Value = patch.Holder3;
                cmd.Parameters.Add("name1", OleDbType.VarWChar).Value = patch.Name1;
                cmd.Parameters.Add("name2", OleDbType.VarWChar).Value = patch.Name2;
                cmd.Parameters.Add("name3", OleDbType.VarWChar).Value = patch.Name3;
                cmd.Parameters.Add("uid", OleDbType.Integer).Value = patch.Uid;
                cmd.Parameters.Add("belt", OleDbType.Integer).Value = beltId;
                cmd.Parameters.Add("order", OleDbType.SmallInt).Value = patch.Order;
                cmd.Parameters.Add("captured", OleDbType.Date).Value = patch.Captured;
                cmd.Parameters.Add("lost", OleDbType.Date).Value = patch.Lost;
                cmd.Parameters.Add("defences", OleDbType.SmallInt).Value = patch.Defences;
                if (cmd.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException(
                        $"Slot #{patch.Order} no longer matches its preview or is protected. " +
                        "All substitutions were rolled back.");
            }
            tx.Commit();
            _openedStamp = GetStamp(PathOnDisk);
            return updates.Count;
        }
        catch
        {
            try { tx.Rollback(); } catch { }
            throw;
        }
    }
}
