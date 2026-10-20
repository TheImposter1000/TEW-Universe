using TEWUniverse.Studio;

namespace TEWUniverse.Desktop;

/// <summary>
/// Expands the existing championship date/reign planner from single winners into
/// two- or three-person title holders. This does NOT create tblWorker or tblTeam
/// records. NPCs are historical names stored with holder UID -1.
/// </summary>
internal static class TeamHistoryPlanner
{
    internal sealed record LinkedTeam(int[] WorkerIds, string[] Names);

    internal static List<GeneratedTitleReign> Populate(
        IReadOnlyList<GeneratedTitleReign> singles,
        IReadOnlyList<AnchoredHistoryPlanner.Anchor> anchored,
        IReadOnlyDictionary<int, LinkedTeam> linkedBySourceOrder,
        IReadOnlyList<NpcNameSeed> npcNames,
        int members, Random? random = null)
    {
        if (members is not (2 or 3))
            throw new InvalidOperationException("Full team history requires two or three holders.");
        random ??= Random.Shared;

        // SourceOrder identifies an individual requested victory, even if one
        // worker has repeated wins in the same year.
        var byOrder = anchored.ToDictionary(a => a.SourceOrder);
        if (byOrder.Count != linkedBySourceOrder.Count ||
            byOrder.Keys.Any(key => !linkedBySourceOrder.ContainsKey(key)))
            throw new InvalidOperationException(
                "A requested team lineup is missing for a fixed title-winning slot.");

        foreach (var (order, team) in linkedBySourceOrder)
        {
            var anchor = byOrder[order];
            if (team.WorkerIds.Length != members || team.Names.Length != members ||
                team.WorkerIds.Any(id => id < -1 || id == 0) ||
                team.WorkerIds.Where(id => id > 0).Distinct().Count() !=
                    team.WorkerIds.Count(id => id > 0) ||
                team.WorkerIds[0] != (anchor.WorkerId < -1 ? -1 : anchor.WorkerId) ||
                team.Names.Any(name => string.IsNullOrWhiteSpace(name) ||
                    name.Trim().Length > 30 || name.Any(char.IsControl)))
                throw new InvalidOperationException(
                    "Fixed teams require distinct linked workers or named -1 NPCs, with valid historical names.");
        }

        var scheduledAnchors = anchored
            .GroupBy(a => (a.WorkerId, a.WinYear))
            .ToDictionary(g => g.Key, g => new Queue<AnchoredHistoryPlanner.Anchor>(
                g.OrderBy(a => a.SourceOrder)));

        // Exclude known linked worker names from the historical NPC name pool:
        // a generated, name-only person should not impersonate a linked worker.
        var linkedNames = linkedBySourceOrder.Values
            .SelectMany(t => t.Names).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pool = npcNames.Select(n => n.Name.Trim())
            .Where(name => name.Length is >= 1 and <= 30 &&
                !name.Any(char.IsControl) && !linkedNames.Contains(name))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (singles.Any(s => s.Holder1 == -1) && pool.Length < members)
            throw new InvalidOperationException(
                "Not enough distinct historical NPC names to make a complete " +
                (members == 2 ? "tag team" : "trios") +
                ". Add more tblGenerateWorker names or broaden the NPC filters.");

        var output = new List<GeneratedTitleReign>(singles.Count);
        string[]? previous = null;
        foreach (var slot in singles)
        {
            int[] ids;
            string[] names;
            if (slot.Holder1 > 0)
            {
                var key = (slot.Holder1, slot.Captured.Year);
                if (!scheduledAnchors.TryGetValue(key, out var queue) || queue.Count == 0)
                    throw new InvalidOperationException(
                        "A generated fixed title win does not match its requested worker/year.");
                var anchor = queue.Dequeue();
                var team = linkedBySourceOrder[anchor.SourceOrder];
                ids = team.WorkerIds.ToArray();
                names = team.Names.ToArray();
            }
            else if (slot.Holder1 == -1)
            {
                ids = Enumerable.Repeat(-1, members).ToArray();
                // Keep teammates distinct, and avoid generating exactly the
                // same team in consecutive reigns. Reuse across eras is allowed.
                string[]? picked = null;
                for (int attempt = 0; attempt < 60; attempt++)
                {
                    var chosen = new HashSet<int>();
                    while (chosen.Count < members)
                        chosen.Add(random.Next(pool.Length));
                    var candidate = chosen.Select(index => pool[index]).ToArray();
                    if (previous != null && candidate.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                            .SequenceEqual(previous.OrderBy(n => n, StringComparer.OrdinalIgnoreCase),
                                StringComparer.OrdinalIgnoreCase))
                        continue;
                    picked = candidate;
                    break;
                }
                if (picked == null)
                {
                    // A pool of exactly N members cannot generate a different
                    // subsequent team. Never invent duplicate champions.
                    throw new InvalidOperationException(
                        "NPC pool cannot make a different team for consecutive reigns. " +
                        "Increase the number of generated NPC names.");
                }
                names = picked;
            }
            else
                throw new InvalidOperationException("Invalid pre-generated champion worker ID.");

            var result = slot with {
                Holder1 = ids[0], HolderName1 = names[0],
                Holder2 = ids[1], HolderName2 = names[1],
                Holder3 = members == 3 ? ids[2] : -1,
                HolderName3 = members == 3 ? names[2] : ""
            };
            output.Add(result);
            previous = names;
        }
        if (scheduledAnchors.Values.Any(q => q.Count != 0))
            throw new InvalidOperationException(
                "Some fixed superstar victories were not assigned to their full teams.");
        return output;
    }
}
