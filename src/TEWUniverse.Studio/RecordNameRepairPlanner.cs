namespace TEWUniverse.Studio;

/// <summary>
/// A dry-run plan for repairing human-facing RecordName identifiers. No
/// physical MDB data is modified until a separate, confirmed apply call.
/// </summary>
internal sealed record TitleRecordNameRow(
    int Uid, int BeltUid, int BeltOrder, string TitleName, string OldName);

internal sealed record EmploymentRecordNameRow(
    int WorkerUid, int FedUid, string Initials, DateTime From, DateTime Until,
    string OldName);

internal sealed record RecordNameRepair(
    string Table, int? Uid, int OwnerUid, int? ReferenceUid, int? Order,
    DateTime? From, DateTime? Until,
    string OldName, string NewName, string Context);

internal sealed record RecordNameRepairPlan(
    string Table, int? SelectedOwnerUid, int ScannedCount,
    IReadOnlyList<RecordNameRepair> Changes, IReadOnlyList<string> Warnings);

internal static class RecordNameRepairPlanner
{
    private static void VerifyExistingNames(IEnumerable<string> existing, string table)
    {
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in existing.Where(name => !string.IsNullOrEmpty(name)))
            if (!unique.Add(name))
                throw new InvalidOperationException(
                    $"Existing {table} has duplicate RecordName '{name}'. " +
                    "This ambiguous schema must be repaired manually before batch renaming.");
    }

    internal static RecordNameRepairPlan Titles(
        IReadOnlyList<TitleRecordNameRow> all, int? selectedBeltUid)
    {
        if (all.Select(row => row.Uid).Distinct().Count() != all.Count)
            throw new InvalidOperationException(
                "Title history contains duplicate UIDs. Batch renaming is disabled.");
        VerifyExistingNames(all.Select(row => row.OldName), "tblBeltPreHistory");
        var targets = all.Where(row =>
            selectedBeltUid == null || row.BeltUid == selectedBeltUid)
            .OrderBy(row => row.BeltUid).ThenBy(row => row.BeltOrder)
            .ThenBy(row => row.Uid).ToArray();
        var reserved = all.Where(row =>
            selectedBeltUid != null && row.BeltUid != selectedBeltUid)
            .Select(row => row.OldName)
            .Where(name => !string.IsNullOrEmpty(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Preserve all problematic records before allocating any new name;
        // otherwise a target generated earlier could collide with a later
        // invalid record that must remain unchanged.
        foreach (var invalid in targets.Where(row =>
            string.IsNullOrWhiteSpace(row.OldName) ||
            string.IsNullOrWhiteSpace(row.TitleName) ||
            row.TitleName.Any(char.IsControl) ||
            row.Uid <= 0 || row.BeltUid <= 0 || row.BeltOrder < 0))
            if (!string.IsNullOrEmpty(invalid.OldName))
                reserved.Add(invalid.OldName);
        var changes = new List<RecordNameRepair>();
        var warnings = new List<string>();

        foreach (var title in targets.GroupBy(row => row.BeltUid))
        {
            int sequence = 1;
            foreach (var row in title)
            {
                if (string.IsNullOrWhiteSpace(row.OldName) ||
                    string.IsNullOrWhiteSpace(row.TitleName) ||
                    row.TitleName.Any(char.IsControl) ||
                    row.Uid <= 0 || row.BeltUid <= 0 || row.BeltOrder < 0)
                {
                    warnings.Add($"Title {row.BeltUid}, history UID {row.Uid}: " +
                        "missing name, title, or valid record identity—left unchanged.");
                    if (!string.IsNullOrEmpty(row.OldName))
                        reserved.Add(row.OldName);
                    continue;
                }
                string proposed;
                do
                {
                    proposed = StudioRecordNames.TitleHistory(row.TitleName, sequence++);
                }
                while (!reserved.Add(proposed));
                if (!string.Equals(row.OldName, proposed, StringComparison.Ordinal))
                    changes.Add(new RecordNameRepair(
                        "tblBeltPreHistory", row.Uid, row.BeltUid, null, row.BeltOrder,
                        null, null, row.OldName, proposed,
                        $"{row.TitleName} • reign order {row.BeltOrder}"));
            }
        }
        return new RecordNameRepairPlan(
            "tblBeltPreHistory", selectedBeltUid, targets.Length, changes, warnings);
    }

    internal static RecordNameRepairPlan Employment(
        IReadOnlyList<EmploymentRecordNameRow> all, int? selectedWorkerUid)
    {
        VerifyExistingNames(all.Select(row => row.OldName), "tblCV");
        var targets = all.Where(row =>
            selectedWorkerUid == null || row.WorkerUid == selectedWorkerUid)
            .OrderBy(row => row.WorkerUid).ThenBy(row => row.From)
            .ThenBy(row => row.Until).ThenBy(row => row.FedUid)
            .ThenBy(row => row.OldName, StringComparer.OrdinalIgnoreCase).ToArray();
        var reserved = all.Where(row =>
            selectedWorkerUid != null && row.WorkerUid != selectedWorkerUid)
            .Select(row => row.OldName)
            .Where(name => !string.IsNullOrEmpty(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var invalid in targets.Where(row =>
            string.IsNullOrWhiteSpace(row.OldName) || row.WorkerUid <= 0 ||
            row.FedUid <= 0 || string.IsNullOrWhiteSpace(row.Initials) ||
            row.Initials.Any(char.IsControl) ||
            row.From.Year is < 1800 or > 2100 ||
            (!StudioDateFormat.IsOngoingEndDate(row.Until) &&
             ((row.Until.Year is < 1800 or > 2100) || row.Until < row.From))))
            if (!string.IsNullOrEmpty(invalid.OldName))
                reserved.Add(invalid.OldName);
        var changes = new List<RecordNameRepair>();
        var warnings = new List<string>();

        foreach (var row in targets)
        {
            if (string.IsNullOrWhiteSpace(row.OldName) || row.WorkerUid <= 0 ||
                row.FedUid <= 0 || string.IsNullOrWhiteSpace(row.Initials) ||
                row.Initials.Any(char.IsControl) ||
                row.From.Year is < 1800 or > 2100 ||
            (!StudioDateFormat.IsOngoingEndDate(row.Until) &&
             ((row.Until.Year is < 1800 or > 2100) || row.Until < row.From)))
            {
                warnings.Add($"Worker {row.WorkerUid}, '{row.OldName}': " +
                    "missing initials, dates, or a valid record identity—left unchanged.");
                if (!string.IsNullOrEmpty(row.OldName))
                    reserved.Add(row.OldName);
                continue;
            }

            string stem = StudioRecordNames.Employment(
                row.Initials, row.From, row.Until);
            string proposed = stem;
            int suffix = 2;
            while (!reserved.Add(proposed))
                proposed = StudioRecordNames.WithCollisionSuffix(stem, suffix++);

            if (!string.Equals(row.OldName, proposed, StringComparison.Ordinal))
                changes.Add(new RecordNameRepair(
                    "tblCV", null, row.WorkerUid, row.FedUid, null,
                    row.From, row.Until, row.OldName, proposed,
                    $"Worker {row.WorkerUid} • {row.Initials} • " +
                    $"{row.From.Year}-" +
                    (StudioDateFormat.IsOngoingEndDate(row.Until)
                        ? "Present" : row.Until.Year.ToString())));
        }
        return new RecordNameRepairPlan(
            "tblCV", selectedWorkerUid, targets.Length, changes, warnings);
    }
}
