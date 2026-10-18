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

    // Resolve a recurring event's calendar date separately for each year.
    // Occurrences 1–4 are ordinal weekdays; 5 means the last such weekday.
    // Not offering "fifth" avoids silently moving an event into another month.
    internal static DateTime ResolveAnnualEventDate(int year, int month, int occurrence,
        DayOfWeek weekday)
    {
        if (year is < 1800 or > 2100 || month is < 1 or > 12 ||
            occurrence is < 1 or > 5 || weekday is < DayOfWeek.Sunday or > DayOfWeek.Saturday)
            throw new InvalidOperationException(
                "Annual tournaments require a month (1–12), a week (first, second, third, fourth or last), " +
                "and a weekday (Sunday–Saturday).");

        if (occurrence == 5)
        {
            var end = new DateTime(year, month, DateTime.DaysInMonth(year, month));
            int back = ((int)end.DayOfWeek - (int)weekday + 7) % 7;
            return end.AddDays(-back);
        }

        var start = new DateTime(year, month, 1);
        int offset = ((int)weekday - (int)start.DayOfWeek + 7) % 7;
        return start.AddDays(offset + 7 * (occurrence - 1));
    }
    /// <summary>
    /// One historical winner per calendar year by default. A linked superstar
    /// occupies a pinned winning year; every otherwise unoccupied year gets a
    /// name-only NPC from tblGenerateWorker. No NPC reign count is requested.
    /// Multiple fixed human wins in a year are supported for non-annual titles,
    /// but never inferred from NPC controls.
    /// </summary>
    internal static List<GeneratedTitleReign> GenerateFromYears(
        IReadOnlyList<Anchor> fixedWins, IReadOnlyList<NpcNameSeed> npcNames,
        int firstYear, int lastYear, bool annualTournament = false,
        int eventMonth = 0, int eventWeek = 0,
        DayOfWeek eventWeekday = DayOfWeek.Sunday)
    {
        if (firstYear < 1800 || lastYear > 2100 || firstYear > lastYear)
            throw new InvalidOperationException(
                "Choose an NPC history range between 1800 and 2100 with start year before end year.");

        // An NPC-only history is also valid when no linked superstar has a pinned victory.
        if (npcNames.Count == 0)
            throw new InvalidOperationException(
                "tblGenerateWorker did not provide any usable NPC names with these filters.");

        if (annualTournament)
            _ = ResolveAnnualEventDate(firstYear, eventMonth, eventWeek, eventWeekday);

        var reserved = new Dictionary<int, List<Anchor>>();
        foreach (var win in fixedWins)
        {
            if (win.WorkerId <= 0 || string.IsNullOrWhiteSpace(win.Name) ||
                win.Name.Length > 30 || win.Name.Any(char.IsControl) ||
                win.WinYear < firstYear || win.WinYear > lastYear ||
                win.FirstEligibleYear > win.WinYear || win.LastEligibleYear < win.WinYear ||
                win.FirstEligibleYear > win.LastEligibleYear)
                throw new InvalidOperationException(
                    $"The pinned win for {win.Name} ({win.WinYear}) is outside its allowed years.");
            if (!reserved.TryGetValue(win.WinYear, out var wins))
                reserved[win.WinYear] = wins = new List<Anchor>();
            if (wins.Any(x => x.WorkerId == win.WorkerId))
                throw new InvalidOperationException(
                    $"{win.Name} has the year {win.WinYear} entered more than once.");
            wins.Add(win);
        }

        if (annualTournament && reserved.Any(pair => pair.Value.Count != 1))
            throw new InvalidOperationException(
                "Annual Tournament mode permits only one winner in each calendar year.");

        // Championship histories use independent, varied reign durations.
        // Only annual cups use the fixed one-winner-per-year model below.
        if (!annualTournament)
            return GenerateVariedChampionshipReigns(fixedWins, npcNames, firstYear, lastYear);

        int total = Enumerable.Range(firstYear, lastYear - firstYear + 1)
            .Sum(y => Math.Max(1, reserved.TryGetValue(y, out var w) ? w.Count : 0));
        if (total > 500)
            throw new InvalidOperationException(
                "The selected years and superstar wins require more than 500 records.");

        // Populate a recurring historical cast, rather than creating a unique
        // name for every year. All NPCs are title-history names only.
        var pool = npcNames
            .Select(x => x.Name)
            .Where(name => !fixedWins.Any(win =>
                name.Equals(win.Name, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(_ => Random.Shared.Next())
            .ToList();
        int npcYears = Enumerable.Range(firstYear, lastYear - firstYear + 1)
            .Count(y => !reserved.ContainsKey(y));
        if (npcYears > 0 && pool.Count == 0)
            throw new InvalidOperationException(
                "There are no available NPC names distinct from the pinned superstars.");
        int castSize = Math.Min(pool.Count, Math.Max(3,
            (int)Math.Ceiling(npcYears / 3.0)));
        pool = pool.Take(castSize).ToList();

        var events = new List<(DateTime Date, int UID, string Name)>();
        string? previous = null;
        foreach (int year in Enumerable.Range(firstYear, lastYear - firstYear + 1))
        {
            if (reserved.TryGetValue(year, out var wins))
            {
                // Each specified winning year is fixed. Multiple anchored wins
                // in one year get separate dates without generating NPC winners.
                for (int i = 0; i < wins.Count; i++)
                {
                    DateTime day = ResolveAnnualEventDate(
                        year, eventMonth, eventWeek, eventWeekday);
                    events.Add((day, wins[i].WorkerId, wins[i].Name));
                    previous = wins[i].Name;
                }
            }
            else
            {
                var choices = pool.Where(name => !string.Equals(name, previous,
                    StringComparison.OrdinalIgnoreCase)).ToArray();
                if (choices.Length == 0) choices = pool.ToArray();
                var selected = choices[Random.Shared.Next(choices.Length)];
                events.Add((ResolveAnnualEventDate(
                    year, eventMonth, eventWeek, eventWeekday), -1, selected));
                previous = selected;
            }
        }
        var reigns = new List<GeneratedTitleReign>(events.Count);
        for (int i = 0; i < events.Count; i++)
        {
            var e = events[i];
            // Annual cups record the one winning date only. TEW's existing
            // tournament histories use 1666-01-01 and -1 as the no-reign
            // sentinels (unlike an ongoing championship's BeltLost/Defences).
            reigns.Add(new GeneratedTitleReign(
                i + 1, e.UID, e.Name, -1, "", e.Date,
                new DateTime(1666, 1, 1), -1));
        }
        return reigns;
    }


    /// <summary>
    /// Used by the original fixed-count lineage tool: divide a total calendar
    /// span into exactly N reigns without giving them identical lengths.
    /// A minimum number of days is protected for each holder and the randomly
    /// sampled durations are normalized to fill the selected span precisely.
    /// </summary>
    internal static int[] AllocateVariedReignDays(int totalDays, int reignCount)
    {
        if (reignCount <= 0 || totalDays < reignCount)
            throw new InvalidOperationException(
                "The championship date range is too short for the requested reign count.");

        int minimum = totalDays >= reignCount * 7 ? 7 : 1;
        int available = totalDays - minimum * reignCount;
        var result = Enumerable.Repeat(minimum, reignCount).ToArray();
        if (available == 0) return result;

        int[] weights = Enumerable.Range(0, reignCount).Select(_ =>
        {
            double roll = Random.Shared.NextDouble();
            return roll switch
            {
                < 0.10 => Random.Shared.Next(14, 43),
                < 0.33 => Random.Shared.Next(43, 106),
                < 0.69 => Random.Shared.Next(106, 251),
                < 0.92 => Random.Shared.Next(251, 451),
                _ => Random.Shared.Next(451, 771)
            };
        }).ToArray();
        long weightSum = weights.Sum(w => (long)w);
        var remainders = new (int Index, long Remainder)[reignCount];
        int assigned = 0;
        for (int i = 0; i < reignCount; i++)
        {
            long numerator = (long)available * weights[i];
            int allocation = (int)(numerator / weightSum);
            result[i] += allocation;
            assigned += allocation;
            remainders[i] = (i, numerator % weightSum);
        }
        foreach (var entry in remainders.OrderByDescending(x => x.Remainder)
                     .Take(available - assigned))
            result[entry.Index]++;
        return result;
    }

    internal static short EstimateReignDefences(int heldDays) => heldDays switch
    {
        < 28 => (short)Random.Shared.Next(0, 2),
        < 90 => (short)Random.Shared.Next(0, 5),
        < 240 => (short)Random.Shared.Next(1, 9),
        < 450 => (short)Random.Shared.Next(3, 16),
        _ => (short)Random.Shared.Next(7, 25)
    };

    /// <summary>
    /// Regular titles are held continuously: an outgoing holder loses the belt
    /// the day before the next holder wins it. The number of NPC reigns is
    /// derived from randomly varied reign lengths, not one reign per year.
    /// Real-worker winning YEARS remain fixed; exact dates are generated.
    /// Annual cups use the separate, unchanged one-edition-per-year logic.
    /// </summary>
    private static List<GeneratedTitleReign> GenerateVariedChampionshipReigns(
        IReadOnlyList<Anchor> fixedWins, IReadOnlyList<NpcNameSeed> npcNames,
        int firstYear, int lastYear)
    {
        const int maxReigns = 500;
        const int minimumTransitionDays = 7;
        var firstDay = new DateTime(firstYear, 1, 1);
        var lastDay = new DateTime(lastYear, 12, 31);

        // Spread wins within their fixed year. Never put every title change
        // on January 1; multiple real winners in one year keep separate dates.
        var pinned = new List<(DateTime Date, int UID, string Name)>();
        foreach (var group in fixedWins.GroupBy(w => w.WinYear).OrderBy(g => g.Key))
        {
            var items = group.OrderBy(w => w.WorkerId).ToArray();
            int daysInYear = DateTime.IsLeapYear(group.Key) ? 366 : 365;
            if (items.Length > daysInYear / minimumTransitionDays)
                throw new InvalidOperationException(
                    $"Too many fixed championship wins in {group.Key} to space them safely.");
            for (int i = 0; i < items.Length; i++)
            {
                int baseDay = daysInYear * (i + 1) / (items.Length + 1);
                int jitterRange = Math.Min(21, daysInYear / (items.Length + 1) / 4);
                int offset = jitterRange > 0
                    ? Random.Shared.Next(-jitterRange, jitterRange + 1) : 0;
                var captured = new DateTime(group.Key, 1, 1).AddDays(baseDay + offset);
                pinned.Add((captured, items[i].WorkerId, items[i].Name));
            }
        }
        pinned.Sort((a, b) => a.Date.CompareTo(b.Date));

        var cast = npcNames.Select(n => n.Name)
            .Where(name => !fixedWins.Any(w =>
                w.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(_ => Random.Shared.Next())
            .Take(Math.Max(5, (lastYear - firstYear + 1) * 2))
            .ToArray();
        if (cast.Length == 0)
            throw new InvalidOperationException(
                "No generated NPC names remain after excluding the linked superstar aliases.");

        var events = new List<(DateTime Date, int UID, string Name)>();
        string PickNpc(string? current, string? upcoming)
        {
            var choices = cast.Where(name =>
                !name.Equals(current, StringComparison.OrdinalIgnoreCase) &&
                !name.Equals(upcoming, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (choices.Length == 0)
                choices = cast.Where(name =>
                    !name.Equals(current, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (choices.Length == 0)
                throw new InvalidOperationException(
                    "The NPC name pool is too small to create distinct consecutive champions.");
            return choices[Random.Shared.Next(choices.Length)];
        }
        void AddNpc(DateTime date, string? upcoming = null)
        {
            if (events.Count >= maxReigns)
                throw new InvalidOperationException(
                    "The requested title-history span exceeds 500 varied reigns. Reduce its years.");
            events.Add((date, -1,
                PickNpc(events.Count == 0 ? null : events[^1].Name, upcoming)));
        }

        if (pinned.Count == 0 || pinned[0].Date > firstDay)
            AddNpc(firstDay, pinned.Count > 0 ? pinned[0].Name : null);

        // Sample actual reign lengths from different tiers: short transitional
        // reigns, several-month reigns, and occasional one-to-two-year reigns.
        static int NextReignDays()
        {
            double roll = Random.Shared.NextDouble();
            return roll switch
            {
                < 0.10 => Random.Shared.Next(14, 43),   // 2–6 weeks
                < 0.33 => Random.Shared.Next(43, 106),  // 1–3 months
                < 0.69 => Random.Shared.Next(106, 251), // 3–8 months
                < 0.92 => Random.Shared.Next(251, 451), // 8–15 months
                _ => Random.Shared.Next(451, 771)      // occasional 15–25 months
            };
        }

        // Insert generated transitions before each fixed real-worker win.
        // If the same worker wins again, a different champion MUST hold the
        // title between those wins (otherwise it is not a second reign).
        foreach (var next in pinned)
        {
            if (events.Count == 0)
            {
                events.Add(next);
                continue;
            }
            var cursor = events[^1];
            while (true)
            {
                int availableDays = (next.Date - cursor.Date).Days;
                if (availableDays < minimumTransitionDays)
                    throw new InvalidOperationException(
                        "Two pinned wins are too close together to be separate title reigns.");
                int length = NextReignDays();
                if (length + minimumTransitionDays > availableDays) break;
                var nextDate = cursor.Date.AddDays(length);
                AddNpc(nextDate, next.Name);
                cursor = events[^1];
            }

            // Fixed wins by the same worker require at least one intervening NPC.
            if (cursor.Name.Equals(next.Name, StringComparison.OrdinalIgnoreCase))
            {
                int availableDays = (next.Date - cursor.Date).Days;
                if (availableDays < 2 * minimumTransitionDays)
                    throw new InvalidOperationException(
                        $"{next.Name} has repeated winning years too close to fit a different champion between them.");
                AddNpc(cursor.Date.AddDays(availableDays / 2), next.Name);
            }

            if (events.Count >= maxReigns)
                throw new InvalidOperationException(
                    "The selected history exceeds 500 reigns. Shorten its date range.");
            events.Add(next);
        }

        // After the final pinned victory, keep building a plausible history
        // through the end of the requested championship timeline.
        if (events.Count == 0)
            AddNpc(firstDay);
        var last = events[^1];
        while (true)
        {
            int daysRemaining = (lastDay - last.Date).Days + 1;
            int length = NextReignDays();
            if (length + minimumTransitionDays > daysRemaining) break;
            var date = last.Date.AddDays(length);
            AddNpc(date);
            last = events[^1];
        }

        var results = new List<GeneratedTitleReign>(events.Count);
        for (int i = 0; i < events.Count; i++)
        {
            var curr = events[i];
            var lost = i + 1 < events.Count
                ? events[i + 1].Date.AddDays(-1) : lastDay;
            if (lost < curr.Date)
                throw new InvalidOperationException("Generated championship reign dates overlap.");
            int held = (lost - curr.Date).Days + 1;
            // Defence counts vary independently of lengths. Long reigns are
            // generally defended more often but not at a fixed 1-per-N-days.
            short defenses = EstimateReignDefences(held);
            results.Add(new GeneratedTitleReign(
                i + 1, curr.UID, curr.Name, -1, "", curr.Date, lost, defenses));
        }

        foreach (var win in fixedWins)
        {
            if (results.Count(r => r.Holder1 == win.WorkerId &&
                    r.HolderName1 == win.Name && r.Captured.Year == win.WinYear) != 1)
                throw new InvalidOperationException(
                    $"The fixed winning year {win.WinYear} for {win.Name} was not preserved.");
        }
        return results;
    }

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
