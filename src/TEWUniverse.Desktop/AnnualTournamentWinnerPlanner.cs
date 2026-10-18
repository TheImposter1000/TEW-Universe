namespace TEWUniverse.Desktop;

/// <summary>
/// Selects name-only winners for annual cups without allowing a tiny recurring
/// NPC cast to dominate long histories. Existing pinned superstar editions are
/// never modified. The planner only supplies names, not TEW worker records.
/// </summary>
internal static class AnnualTournamentWinnerPlanner
{
    /// <param name="pinnedByEdition">
    /// One entry per annual edition. Null denotes an NPC slot; a non-null
    /// name is a fixed, linked-worker winner that must be preserved.
    /// </param>
    internal static List<string> FillWinners(
        IReadOnlyList<string?> pinnedByEdition,
        IReadOnlyList<string> availableNpcNames,
        Random? random = null)
    {
        random ??= Random.Shared;
        int npcSlots = pinnedByEdition.Count(string.IsNullOrWhiteSpace);
        if (npcSlots == 0)
            return pinnedByEdition.Select(name => name!).ToList();

        // Exclude every real-worker alias so a generated name cannot silently
        // impersonate a pinned winner with Holder1 = -1.
        var pinnedNames = pinnedByEdition
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pool = availableNpcNames
            .Where(name => !string.IsNullOrWhiteSpace(name) &&
                !pinnedNames.Contains(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (pool.Count == 0)
            throw new InvalidOperationException(
                "There are no available NPC names distinct from the pinned superstars.");

        // With sufficient source names, at least 75% of the NPC victories
        // belong to different people. The other slots allow believable
        // two-time winners without forcing the same few names every year.
        // A filtered source with fewer names can only use its available cast.
        int uniqueTarget = Math.Min(pool.Count,
            (int)Math.Ceiling(npcSlots * 0.75));
        int repeatCount = npcSlots - uniqueTarget;

        // Reserve repeat editions, interleaved with new winners rather than
        // stacking all repeat victories at the end. Allow at least three
        // newcomers first when the NPC source has enough distinct names.
        int initialNewcomers = Math.Min(3, uniqueTarget);
        var possibleRepeatSlots = Enumerable.Range(initialNewcomers,
            npcSlots - initialNewcomers).ToArray();
        for (int i = possibleRepeatSlots.Length - 1; i > 0; i--)
        {
            int other = random.Next(i + 1);
            (possibleRepeatSlots[i], possibleRepeatSlots[other]) =
                (possibleRepeatSlots[other], possibleRepeatSlots[i]);
        }
        var repeatSlots = possibleRepeatSlots.Take(repeatCount).ToHashSet();

        var unused = pool;
        var victories = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var winners = new List<string>(pinnedByEdition.Count);
        int npcIndex = 0;
        foreach (var fixedWinner in pinnedByEdition)
        {
            if (!string.IsNullOrWhiteSpace(fixedWinner))
            {
                winners.Add(fixedWinner);
                continue;
            }

            string selected;
            if (repeatSlots.Contains(npcIndex))
            {
                var choices = victories.Keys.ToList();
                // Prefer winners absent from the previous two editions,
                // including years occupied by fixed superstar victories.
                var cooled = choices.Where(name =>
                    !winners.TakeLast(2).Any(recent =>
                        recent.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
                if (cooled.Count > 0)
                    choices = cooled;
                else if (choices.Count > 1 && winners.Count > 0)
                    choices = choices.Where(name =>
                        !name.Equals(winners[^1], StringComparison.OrdinalIgnoreCase))
                        .ToList();

                // Reuse people with the fewest prior victories first;
                // most repeat champions will therefore win only twice.
                int fewest = choices.Min(name => victories[name]);
                choices = choices.Where(name => victories[name] == fewest).ToList();
                selected = choices[random.Next(choices.Count)];
            }
            else
            {
                int index = random.Next(unused.Count);
                selected = unused[index];
                unused.RemoveAt(index);
            }

            victories[selected] = victories.GetValueOrDefault(selected) + 1;
            winners.Add(selected);
            npcIndex++;
        }
        return winners;
    }
}
