using TEWUniverse.Studio;

namespace TEWUniverse.Desktop;

/// <summary>
/// Generates an entire singles lineage around FIXED championship wins.
/// Reign transitions imply who the new champion defeated and who later defeated
/// them. NPC champions originate from tblGenerateWorker, never tblWorker.
/// </summary>
internal static class AnchoredHistoryPlanner
{
    internal sealed record Anchor(string Name, int WorkerId, int WinYear,
        int FirstEligibleYear, int LastEligibleYear);
    private sealed record Event(DateTime Won, Anchor? FixedWinner);

    internal static List<GeneratedTitleReign> Generate(
        IReadOnlyList<Anchor> fixedWins,
        IReadOnlyList<NpcNameSeed> npcNames,
        int startYear, int endYear, int requestedReigns)
    {
        if (startYear is < 1800 or > 2100 || endYear is < 1800 or > 2100 ||
            startYear > endYear)
            throw new InvalidOperationException("Enter a valid championship timeline from 1800 to 2100.");
        if (fixedWins.Count == 0)
            throw new InvalidOperationException(
                "Enter a wrestler's exact title-winning years in the Win years column first.");
        if (requestedReigns is < 1 or > 500)
            throw new InvalidOperationException("Choose between 1 and 500 total reigns.");
        if (npcNames.Count == 0)
            throw new InvalidOperationException(
                "No NPC names were found in tblGenerateWorker with the chosen filters.");

        var events = new List<Event>();
        foreach (var byYear in fixedWins.GroupBy(a => a.WinYear).OrderBy(g => g.Key))
        {
            var inYear = byYear.ToArray();
            for (int i = 0; i < inYear.Length; i++)
            {
                var a = inYear[i];
                if (a.WorkerId <= 0 ||
                    a.WinYear < startYear || a.WinYear > endYear ||
                    a.WinYear < a.FirstEligibleYear || a.WinYear > a.LastEligibleYear ||
                    string.IsNullOrWhiteSpace(a.Name) || a.Name.Length > 30)
                    throw new InvalidOperationException(
                        $"Fixed title win {a.Name} ({a.WinYear}) has an invalid name, worker ID or eligibility year.");
                // Distinct dates even when several people win during the same year.
                DateTime day = new DateTime(a.WinYear, 1, 1)
                    .AddDays((int)Math.Round((i + 1.0) * 364 / (inYear.Length + 1.0)));
                events.Add(new Event(day, a));
            }
        }
        events.Sort((a, b) => a.Won.CompareTo(b.Won));

        DateTime firstDay = new(startYear, 1, 1);
        DateTime lastDay = new(endYear, 12, 31);
        if ((lastDay - firstDay).Days + 1 < requestedReigns)
            throw new InvalidOperationException(
                "Not enough calendar days for the requested number of reigns.");

        // Ensure name-only champions before the first featured title win,
        // between every pair of featured reigns, and after the last featured win.
        var requiredNpcDates = new List<DateTime> { firstDay };
        for (int i = 0; i + 1 < events.Count; i++)
        {
            var left = events[i].Won;
            var right = events[i + 1].Won;
            var available = (right - left).Days;
            if (available < 2)
                throw new InvalidOperationException(
                    "Two fixed title wins are too close to fit an intervening champion. " +
                    "Choose a different date range or reduce the fixed wins in that year.");
            requiredNpcDates.Add(left.AddDays(available / 2));
        }
        var tail = (lastDay - events[^1].Won).Days;
        if (tail >= 2)
            requiredNpcDates.Add(events[^1].Won.AddDays(Math.Max(1, tail / 2)));

        // If the first pinned win is already Jan 1, no earlier reign can fit.
        requiredNpcDates.RemoveAll(d => d >= events[0].Won &&
            d == firstDay);
        if (events[0].Won > firstDay)
            requiredNpcDates[0] = firstDay;
        requiredNpcDates = requiredNpcDates.Where(d =>
            !events.Any(a => a.Won == d)).Distinct().ToList();

        int minimum = events.Count + requiredNpcDates.Count;
        if (requestedReigns < minimum)
            throw new InvalidOperationException(
                $"This storyline needs at least {minimum} reigns: {events.Count} fixed title wins " +
                $"and {requiredNpcDates.Count} surrounding NPC reigns. Increase Total reigns.");
        events.AddRange(requiredNpcDates.Select(d => new Event(d, null)));
        events.Sort((a,b) => a.Won.CompareTo(b.Won));

        // Distribute the remaining background reigns into the longest available
        // gaps, keeping fixed winning years and existing transition dates intact.
        while (events.Count < requestedReigns)
        {
            int best = -1, widest = 1;
            for (int i = 0; i < events.Count; i++)
            {
                var right = i + 1 < events.Count ? events[i+1].Won : lastDay.AddDays(1);
                int width = (right - events[i].Won).Days;
                if (width > widest) { widest = width; best = i; }
            }
            if (best < 0)
                throw new InvalidOperationException(
                    "The requested reign count is too high to fit distinct title changes in this period.");
            var next = best + 1 < events.Count ? events[best+1].Won : lastDay.AddDays(1);
            var mid = events[best].Won.AddDays((next - events[best].Won).Days / 2);
            events.Insert(best + 1, new Event(mid, null));
        }

        // A repeating cast of NPCs produces a cohesive fictional roster, rather
        // than giving every historical reign to a different unknown person.
        var names = npcNames
            .Where(n => !fixedWins.Any(a => a.Name.Equals(n.Name,
                StringComparison.OrdinalIgnoreCase)))
            .Select(n => n.Name).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(_ => Random.Shared.Next()).ToList();
        if (names.Count == 0)
            throw new InvalidOperationException("All NPC names conflict with the fixed real-worker names.");
        int npcEvents = events.Count(e => e.FixedWinner == null);
        int castSize = Math.Min(names.Count,
            Math.Max(3, (int)Math.Ceiling(npcEvents / 2.5)));
        names = names.Take(castSize).ToList();

        var output = new List<GeneratedTitleReign>(events.Count);
        string? previous = null;
        for (int i = 0; i < events.Count; i++)
        {
            var ev = events[i];
            string champion;
            int uid;
            if (ev.FixedWinner is { } anchor)
            {
                champion = anchor.Name; uid = anchor.WorkerId;
            }
            else
            {
                var choices = names.Where(n => !n.Equals(previous,
                    StringComparison.OrdinalIgnoreCase)).ToArray();
                if (choices.Length == 0) choices = names.ToArray();
                champion = choices[Random.Shared.Next(choices.Length)];
                uid = -1; // TEW IX name-only lineage, NOT a new tblWorker row.
            }
            DateTime lost = i + 1 < events.Count ?
                events[i + 1].Won.AddDays(-1) : lastDay;
            if (ev.Won > lost)
                throw new InvalidOperationException("Generated title transition dates overlap.");
            short defenses = (short)Math.Min(short.MaxValue,
                Math.Max(0, (lost - ev.Won).Days / 48));
            output.Add(new GeneratedTitleReign(
                i + 1, uid, champion, -1, "", ev.Won, lost, defenses));
            previous = champion;
        }
        foreach (var anchor in fixedWins)
            if (output.Count(r => r.Holder1 == anchor.WorkerId &&
                r.HolderName1 == anchor.Name &&
                r.Captured.Year == anchor.WinYear) == 0)
                throw new InvalidOperationException("A pinned championship win was lost during generation.");
        return output;
    }
}
