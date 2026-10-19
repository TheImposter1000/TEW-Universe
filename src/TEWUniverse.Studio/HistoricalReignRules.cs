namespace TEWUniverse.Studio;

/// <summary>
/// Pure safety rules shared by real MDB title-history insertion and regression tests.
/// -1 denotes a name-only NPC; positive IDs must resolve to actual workers.
/// </summary>
internal static class HistoricalReignRules
{
    public static void ValidateHolders(
        IReadOnlyList<int> holderIds, IReadOnlyList<string> holderNames,
        int memberCount, IReadOnlySet<int> realWorkerIds)
    {
        if (memberCount is < 1 or > 3 ||
            holderIds.Count != 3 || holderNames.Count != 3)
            throw new InvalidOperationException("The title requires one to three holder positions.");
        var selectedWorkers = new HashSet<int>();
        for (int i = 0; i < 3; i++)
        {
            int id = holderIds[i];
            string name = holderNames[i].Trim();
            if (i >= memberCount)
            {
                if (id != -1 || name.Length != 0)
                    throw new InvalidOperationException("Unused team positions must be empty.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(name) || name.Length > 30 ||
                name.Any(char.IsControl))
                throw new InvalidOperationException("Historical champion names must be 1–30 characters.");
            if (id != -1 && !realWorkerIds.Contains(id))
                throw new InvalidOperationException("The selected linked worker does not exist.");
            if (id > 0 && !selectedWorkers.Add(id))
                throw new InvalidOperationException("Each linked team member must be different.");
        }
    }

    /// <summary>
    /// Compute where a new reign belongs in an existing chronological title history.
    /// Existing capture/loss dates are never adjusted to make room.
    /// </summary>
    public static int InsertionPosition(
        IReadOnlyList<(DateTime Captured, DateTime Lost)> existing,
        DateTime captured, DateTime lost, bool annual)
    {
        if (existing.Any((r) => r.Captured == default || r.Lost == default))
            throw new InvalidOperationException("Existing title history contains invalid dates.");
        if (!existing.Select(r => r.Captured).SequenceEqual(
                existing.Select(r => r.Captured).OrderBy(d => d)))
            throw new InvalidOperationException("Existing title history is not chronological.");
        if (annual
            ? existing.Any(r => r.Captured == captured)
            : existing.Any(r => captured <= r.Lost && lost >= r.Captured))
            throw new InvalidOperationException(
                "The proposed reign overlaps an existing one. Correct its dates first.");
        for (int i = 0; i < existing.Count; i++)
            if (existing[i].Captured > captured) return i;
        return existing.Count;
    }
}
