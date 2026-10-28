using TEWUniverse.Studio;

namespace TEWUniverse.Desktop;

/// <summary>
/// A draft-first, single-champion lineage planner. Required reign counts are
/// exact; any unassigned reign slots are covered by optional historical fillers.
/// A candidate may have a real worker UID or -1 (a name-only historical holder).
/// Company tenure is an explicit user input, never inferred from the MDB.
/// </summary>
internal static class ConstrainedLineagePlanner
{
    internal sealed record Candidate(
        string DisplayName, int WorkerId, int FirstYear, int LastYear,
        int RequiredReigns, bool AllowFiller);

    internal static List<GeneratedTitleReign> Generate(
        IReadOnlyList<Candidate> candidates, int firstYear, int lastYear,
        int slotCount, bool fillUnassigned, string championshipName = "",
        TitleDefenceFrequency defenceFrequency = TitleDefenceFrequency.Automatic)
    {
        if (firstYear is < 1800 or > 2100 || lastYear is < 1800 or > 2100 ||
            firstYear > lastYear)
            throw new InvalidOperationException("The lineage year range must be between 1800 and 2100.");
        if (slotCount is < 1 or > 500)
            throw new InvalidOperationException("Choose between 1 and 500 total reigns.");
        if (candidates.Count == 0)
            throw new InvalidOperationException("Add at least one champion to the participant list.");

        var start = new DateTime(firstYear, 1, 1);
        var end = new DateTime(lastYear, 12, 31);
        int days = (end - start).Days + 1;
        if (days < slotCount)
            throw new InvalidOperationException("The time range is too short for that many reigns.");

        foreach (var c in candidates)
        {
            if (string.IsNullOrWhiteSpace(c.DisplayName) || c.DisplayName.Length > 30)
                throw new InvalidOperationException("Every champion needs a display name of 1–30 characters.");
            if (c.DisplayName.Any(char.IsControl))
                throw new InvalidOperationException("Champion names cannot contain control characters.");
            if (c.WorkerId != -1 && c.WorkerId <= 0)
                throw new InvalidOperationException("Use a real worker UID or -1 for historical names.");
            if (c.FirstYear is < 1800 or > 2100 || c.LastYear is < 1800 or > 2100 ||
                c.FirstYear > c.LastYear)
                throw new InvalidOperationException($"Invalid company tenure for {c.DisplayName}.");
            if (c.RequiredReigns is < 0 or > 500)
                throw new InvalidOperationException($"Invalid number of required reigns for {c.DisplayName}.");
        }

        int mandatoryCount = candidates.Sum(x => x.RequiredReigns);
        if (mandatoryCount > slotCount)
            throw new InvalidOperationException(
                $"The roster requires {mandatoryCount} reigns but only {slotCount} slots exist.");
        if (mandatoryCount < slotCount && !fillUnassigned)
            throw new InvalidOperationException(
                $"The roster requires {mandatoryCount} reigns. Either use that as the total " +
                "or enable Fill remaining slots with background champions.");
        if (mandatoryCount < slotCount && !candidates.Any(x => x.AllowFiller))
            throw new InvalidOperationException(
                "Mark at least one historical or background champion as Fill gaps to cover remaining reigns.");

        var intervals = Enumerable.Range(0, slotCount)
            .Select(i => (
                Captured: start.AddDays((int)((long)i * days / slotCount)),
                Lost: start.AddDays((int)((long)(i + 1) * days / slotCount) - 1)))
            .ToArray();

        int[] eligibleSlots(Candidate c) =>
            Enumerable.Range(0, slotCount)
                .Where(i => intervals[i].Captured.Year >= c.FirstYear &&
                    intervals[i].Lost.Year <= c.LastYear)
                .ToArray();

        var positions = candidates.Select(eligibleSlots).ToArray();
        for (int c = 0; c < candidates.Count; c++)
            if (positions[c].Length < candidates[c].RequiredReigns)
                throw new InvalidOperationException(
                    $"{candidates[c].DisplayName} must have {candidates[c].RequiredReigns} reigns " +
                    $"between {candidates[c].FirstYear} and {candidates[c].LastYear}, but " +
                    $"only {positions[c].Length} reign slots fit. Extend their tenure, " +
                    "reduce their reign count, or adjust the championship date range.");

        // Most time-constrained required participants are placed first.
        var requiredOrder = Enumerable.Range(0, candidates.Count)
            .Where(i => candidates[i].RequiredReigns > 0)
            .OrderBy(i => (double)positions[i].Length / candidates[i].RequiredReigns)
            .ThenByDescending(i => candidates[i].RequiredReigns)
            .ToArray();

        for (int attempt = 0; attempt < 160; attempt++)
        {
            var occupants = new int[slotCount];
            Array.Fill(occupants, -1);
            bool failed = false;
            foreach (int who in requiredOrder)
            {
                int needed = candidates[who].RequiredReigns;
                var positionsForWorker = positions[who];
                int low = positionsForWorker[0];
                int high = positionsForWorker[^1];
                // Spread three reigns across the entire tenure window rather
                // than placing them next to one another or choosing them randomly.
                for (int reign = 0; reign < needed; reign++)
                {
                    double target = low + (high - low) * (reign + 1.0) / (needed + 1.0);
                    var available = positionsForWorker
                        .Where(pos => occupants[pos] == -1)
                        .OrderBy(pos => Math.Abs(pos - target) +
                            (pos > 0 && occupants[pos - 1] == who ? 4 : 0) +
                            (pos + 1 < slotCount && occupants[pos + 1] == who ? 4 : 0) +
                            Random.Shared.NextDouble() * (attempt == 0 ? 0.1 : 3))
                        .ToArray();
                    if (available.Length == 0) { failed = true; break; }
                    occupants[available[0]] = who;
                }
                if (failed) break;
            }
            if (failed) continue;

            if (mandatoryCount < slotCount)
            {
                // Slots at the ends of the timeline often have few possible
                // holders. Fill those most-constrained positions first.
                var unfilled = Enumerable.Range(0, slotCount).Where(i => occupants[i] == -1)
                    .OrderBy(i => Enumerable.Range(0, candidates.Count)
                        .Count(j => candidates[j].AllowFiller && positions[j].Contains(i)))
                    .ToArray();
                foreach (int slot in unfilled)
                {
                    var possible = Enumerable.Range(0, candidates.Count)
                        .Where(i => candidates[i].AllowFiller && positions[i].Contains(slot))
                        .ToArray();
                    if (possible.Length == 0)
                    {
                        failed = true;
                        break;
                    }
                    var preferred = possible.Where(i =>
                        (slot == 0 || occupants[slot - 1] != i) &&
                        (slot + 1 == slotCount || occupants[slot + 1] != i))
                        .ToArray();
                    var pool = preferred.Length > 0 ? preferred : possible;
                    occupants[slot] = pool[Random.Shared.Next(pool.Length)];
                }
            }
            if (failed) continue;

            var results = new List<GeneratedTitleReign>(slotCount);
            for (int i = 0; i < slotCount; i++)
            {
                var candidate = candidates[occupants[i]];
                var (captured, lost) = intervals[i];
                short defenses = TitleDefencePlanner.Estimate(
                    (lost - captured).Days + 1, championshipName, defenceFrequency);
                results.Add(new GeneratedTitleReign(
                    i + 1, candidate.WorkerId, candidate.DisplayName,
                    -1, "", captured, lost, defenses));
            }
            // Verify exact quotas in the final output even if filler entries
            // share names: each candidate is identified by its input index.
            foreach (var index in Enumerable.Range(0, candidates.Count))
            {
                int placed = occupants.Count(pos => pos == index);
                if (placed < candidates[index].RequiredReigns ||
                    (!candidates[index].AllowFiller && placed != candidates[index].RequiredReigns))
                    throw new InvalidOperationException("The generated reign counts failed validation.");
            }
            return results;
        }

        throw new InvalidOperationException(
            "Unable to satisfy every required reign within the given time windows. " +
            "Try extending tenures or the overall date range, reducing reign quotas, " +
            "or adding background champions to cover missing years.");
    }
}
