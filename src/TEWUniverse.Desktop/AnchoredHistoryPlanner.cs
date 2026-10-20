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
        int FirstEligibleYear, int LastEligibleYear, DateTime? NotBefore = null,
        int? ReignDays = null, int? ReignYears = null,
        int? ReignMonths = null, int SourceOrder = 0);
    private sealed record ChampionshipEvent(DateTime Date, int UID, string Name,
        DateTime? ForcedEnd = null, bool ForcedNpcBridge = false);
    private sealed record Event(DateTime Won, Anchor? FixedWinner);

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
        DayOfWeek eventWeekday = DayOfWeek.Sunday,
        int startMonth = 1)
    {
        if (firstYear < 1800 || lastYear > 2100 || firstYear > lastYear)
            throw new InvalidOperationException(
                "Choose an NPC history range between 1800 and 2100 with start year before end year.");

        // An NPC-only history is also valid when no linked superstar has a pinned victory.
        if (npcNames.Count == 0)
            throw new InvalidOperationException(
                "tblGenerateWorker did not provide any usable NPC names with these filters.");

        if (annualTournament)
            _ = AnnualTournamentCalendar.ResolveDate(firstYear, eventMonth, eventWeek, eventWeekday);
        else if (startMonth is < 1 or > 12)
            throw new InvalidOperationException(
                "Championship history starting month must be between January (1) and December (12).");

        var reserved = new Dictionary<int, List<Anchor>>();
        foreach (var win in fixedWins)
        {
            if (win.WorkerId is 0 or -1 || string.IsNullOrWhiteSpace(win.Name) ||
                win.Name.Length > 30 || win.Name.Any(char.IsControl) ||
                win.WinYear < firstYear || win.WinYear > lastYear ||
                win.FirstEligibleYear > win.WinYear || win.LastEligibleYear < win.WinYear ||
                win.FirstEligibleYear > win.LastEligibleYear ||
                (win.NotBefore.HasValue && win.NotBefore.Value.Year != win.WinYear))
                throw new InvalidOperationException(
                    $"The pinned win for {win.Name} ({win.WinYear}) is outside its allowed years.");
            if (!reserved.TryGetValue(win.WinYear, out var wins))
                reserved[win.WinYear] = wins = new List<Anchor>();
            // Ordinary championships can change hands repeatedly within
            // the same calendar year, including twice to the same wrestler.
            // Annual Cups still have just one winner per yearly edition.
            wins.Add(win);
        }

        if (annualTournament && reserved.Any(pair => pair.Value.Count != 1))
            throw new InvalidOperationException(
                "Annual Tournament mode permits only one winner in each calendar year.");
        if (annualTournament && fixedWins.Any(w =>
                w.ReignDays.HasValue || w.ReignYears.HasValue ||
                w.ReignMonths.HasValue))
            throw new InvalidOperationException(
                "Exact reign lengths apply to championships only. Annual Cups record individual victories, not continuing title reigns.");

        // Championship histories use independent, varied reign durations.
        // Only annual cups use the fixed one-winner-per-year model below.
        if (!annualTournament)
            return GenerateVariedChampionshipReigns(fixedWins, npcNames,
                firstYear, lastYear, startMonth);

        int total = Enumerable.Range(firstYear, lastYear - firstYear + 1)
            .Sum(y => Math.Max(1, reserved.TryGetValue(y, out var w) ? w.Count : 0));
        if (total > 500)
            throw new InvalidOperationException(
                "The selected years and superstar wins require more than 500 records.");

        // Assign the pinned editions first. The annual NPC picker then
        // distributes distinct names and occasional balanced repeat winners,
        // without changing fixed worker UIDs or any event calendar dates.
        var years = Enumerable.Range(firstYear, lastYear - firstYear + 1).ToArray();
        var plannedNames = AnnualTournamentWinnerPlanner.FillWinners(
            years.Select(year => reserved.TryGetValue(year, out var wins)
                ? wins[0].Name : null).ToArray(),
            npcNames.Select(seed => seed.Name).ToArray());

        var events = new List<(DateTime Date, int UID, string Name)>(years.Length);
        for (int i = 0; i < years.Length; i++)
        {
            int year = years[i];
            int workerUid = reserved.TryGetValue(year, out var fixedWinner)
                ? fixedWinner[0].WorkerId : -1;
            events.Add((AnnualTournamentCalendar.ResolveDate(
                year, eventMonth, eventWeek, eventWeekday),
                workerUid, plannedNames[i]));
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
        int firstYear, int lastYear, int startMonth)
    {
        const int maxReigns = 500;
        const int minimumTransitionDays = 7;
        // A starting MONTH is the allowed opening window, not a command
        // to create an inaugural champion on the 1st. Pick a new opening
        // day within that month on each regeneration.
        var firstDay = new DateTime(firstYear, startMonth,
            Random.Shared.Next(1, DateTime.DaysInMonth(firstYear, startMonth) + 1));
        var lastDay = new DateTime(lastYear, 12, 31);

        // Fixed winning years are preserved, but any relevant prior tournament
        // victories can move the capture date later inside that same year.
        var pinned = ChampionshipWinScheduler.Schedule(fixedWins.Select(w =>
            new ChampionshipWinScheduler.Win(w.WorkerId, w.Name, w.WinYear,
                w.NotBefore, w.ReignDays, w.ReignYears, w.ReignMonths,
                w.SourceOrder))
            .ToArray(), firstDay, lastDay).Select(w => new ChampionshipEvent(
                w.Date, w.WorkerId, w.Name,
                ChampionshipWinScheduler.RequiredNextCaptureDate(w))).ToList();

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

        var events = new List<ChampionshipEvent>();
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
        void AddNpc(DateTime date, string? upcoming = null, bool forcedBridge = false)
        {
            if (events.Count >= maxReigns)
                throw new InvalidOperationException(
                    "The requested title-history span exceeds 500 varied reigns. Reduce its years.");
            events.Add(new ChampionshipEvent(date, -1,
                PickNpc(events.Count == 0 ? null : events[^1].Name, upcoming),
                ForcedNpcBridge: forcedBridge));
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

        // Before each pinned win, fill any free history with NPC champions.
        // A fixed-duration superstar's tenure is protected: NPCs cannot
        // displace them early, and a successor must capture exactly when
        // their requested number of days or calendar years has elapsed.
        foreach (var next in pinned)
        {
            if (events.Count == 0)
            {
                events.Add(next);
                continue;
            }

            var cursor = events[^1];
            if (cursor.ForcedEnd is DateTime requiredNextCapture)
            {
                if (next.Date < requiredNextCapture)
                    throw new InvalidOperationException(
                        $"The next pinned victory would shorten {cursor.Name}'s exact reign.");
                if (next.Date > requiredNextCapture)
                {
                    // Their exact reign ends on the preceding day. Insert a
                    // transitional NPC precisely on the requested next date.
                    AddNpc(requiredNextCapture, next.Name, forcedBridge: true);
                    cursor = events[^1];
                }
                else if (cursor.Name.Equals(next.Name, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"{next.Name} cannot win another reign without an intervening champion.");
            }

            if (cursor.ForcedEnd == null)
            {
                while (true)
                {
                    int availableDays = (next.Date - cursor.Date).Days;
                    int minimum = cursor.ForcedNpcBridge ? 1 : minimumTransitionDays;
                    if (availableDays < minimum)
                        throw new InvalidOperationException(
                            $"The pinned title win for {next.Name} leaves no room for a valid intervening reign.");
                    int length = NextReignDays();
                    if (length + minimumTransitionDays > availableDays) break;
                    var nextDate = cursor.Date.AddDays(length);
                    AddNpc(nextDate, next.Name);
                    cursor = events[^1];
                }
            }

            // A worker claiming two separate reigns needs an intervening
            // champion, even when their earlier reign length was fixed.
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

        // After the last pinned victory, honor its exact release date too.
        // If the protected reign finishes on the timeline's final day,
        // there is no need to invent a successor in the following year.
        if (events.Count == 0)
            AddNpc(firstDay);
        var last = events[^1];
        if (last.ForcedEnd is DateTime forcedEnd)
        {
            if (forcedEnd > lastDay.AddDays(1))
                throw new InvalidOperationException(
                    $"{last.Name}'s exact reign extends beyond the selected history end year.");
            if (forcedEnd <= lastDay)
            {
                AddNpc(forcedEnd, forcedBridge: true);
                last = events[^1];
            }
        }
        while (last.ForcedEnd == null)
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
            if (curr.ForcedEnd.HasValue && lost.AddDays(1) != curr.ForcedEnd.Value)
                throw new InvalidOperationException(
                    $"Generated title changes would shorten the required reign for {curr.Name}. " +
                    "Move the fixed winning year or adjust its reign duration.");
            // Defence counts vary independently of lengths. Long reigns are
            // generally defended more often but not at a fixed 1-per-N-days.
            short defenses = EstimateReignDefences(held);
            results.Add(new GeneratedTitleReign(
                i + 1, curr.UID, curr.Name, -1, "", curr.Date, lost, defenses));
        }

        // Count by (worker, year) instead of demanding a unique year: title
        // hot-potato feuds can legitimately create multiple wins in 2016.
        foreach (var group in fixedWins.GroupBy(w => (w.WorkerId, w.WinYear)))
        {
            int count = results.Count(r => r.Holder1 == group.Key.WorkerId &&
                r.Captured.Year == group.Key.WinYear);
            if (count != group.Count())
                throw new InvalidOperationException(
                    $"Expected {group.Count()} win(s) for worker {group.Key.WorkerId} " +
                    $"in {group.Key.WinYear}, but generated {count}.");
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
