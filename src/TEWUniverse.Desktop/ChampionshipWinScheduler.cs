namespace TEWUniverse.Desktop;

/// <summary>
/// Schedules linked worker championship victories within their exact winning
/// years. Optional reign lengths reserve a protected, continuous title reign
/// and constrain subsequent capture dates across calendar-year boundaries.
/// This is scheduling only; it does not edit any recorded tournament lineage.
/// </summary>
internal static class ChampionshipWinScheduler
{
    internal sealed record Win(int WorkerId, string Name, int Year,
        DateTime? NotBefore = null, int? ReignDays = null, int? ReignYears = null);

    internal sealed record Scheduled(DateTime Date, int WorkerId, string Name,
        int? ReignDays = null, int? ReignYears = null);

    /// <summary>
    /// Returns the date of the NEXT capture, i.e. the exclusive end of the
    /// champion's reign. BeltLost is the day before this date, so a 719-day
    /// reign means (BeltLost - BeltCaptured).Days + 1 == 719.
    /// A calendar-year duration uses AddYears, respecting leap years.
    /// </summary>
    internal static DateTime? RequiredNextCaptureDate(Scheduled win) =>
        win.ReignDays.HasValue ? win.Date.AddDays(win.ReignDays.Value)
        : win.ReignYears.HasValue ? win.Date.AddYears(win.ReignYears.Value)
        : null;

    private static DateTime EarliestFollowingCapture(Win win, DateTime capture, bool sameWorker)
    {
        DateTime? requiredNext = win.ReignDays.HasValue
            ? capture.AddDays(win.ReignDays.Value)
            : win.ReignYears.HasValue ? capture.AddYears(win.ReignYears.Value) : null;
        // If the same worker wins again, somebody else needs a short intervening
        // reign after the protected reign ends.
        if (requiredNext.HasValue)
            return requiredNext.Value.AddDays(sameWorker ? 7 : 0);
        return capture.AddDays(sameWorker ? 14 : 7);
    }

    internal static List<Scheduled> Schedule(IReadOnlyList<Win> wins,
        DateTime? historyStart = null, DateTime? historyEnd = null)
    {
        var input = wins
            .GroupBy(w => w.Year)
            .OrderBy(g => g.Key)
            .SelectMany(g => g.OrderBy(w => w.NotBefore?.Date ?? new DateTime(g.Key, 1, 1))
                .ThenBy(w => w.WorkerId))
            .ToArray();
        if (input.Length == 0) return new List<Scheduled>();

        foreach (var win in input)
        {
            if (win.Year is < 1800 or > 2100)
                throw new InvalidOperationException("Fixed championship wins require years between 1800 and 2100.");
            if (win.NotBefore.HasValue && win.NotBefore.Value.Year != win.Year)
                throw new InvalidOperationException(
                    $"The tournament chronology constraint for {win.Name} must fall in {win.Year}.");
            if (win.ReignDays.HasValue && win.ReignYears.HasValue)
                throw new InvalidOperationException(
                    $"Specify either days or calendar years, not both, for {win.Name} ({win.Year}).");
            if (win.ReignDays.HasValue && win.ReignDays.Value is < 1 or > 36525 ||
                win.ReignYears.HasValue && win.ReignYears.Value is < 1 or > 100)
                throw new InvalidOperationException(
                    $"Enter a positive duration (up to 36,525 days or 100 calendar years) for {win.Name} ({win.Year}).");
            if (historyStart.HasValue && win.Year < historyStart.Value.Year)
                throw new InvalidOperationException("A fixed championship-winning year predates the history start.");
            if (historyEnd.HasValue && win.Year > historyEnd.Value.Year)
                throw new InvalidOperationException("A fixed championship-winning year exceeds the history end.");
        }

        // Backward pass: reserve enough time for every protected reign and all
        // subsequent pinned captures before placing any random within-year dates.
        // This avoids choosing a late capture and then silently cutting a
        // requested 719-day reign short.
        var latest = new DateTime[input.Length];
        for (int i = input.Length - 1; i >= 0; i--)
        {
            var win = input[i];
            var lower = historyStart.HasValue && win.Year == historyStart.Value.Year
                ? historyStart.Value.Date : new DateTime(win.Year, 1, 1);
            var upper = new DateTime(win.Year, 12, 31);
            if (historyEnd.HasValue && upper > historyEnd.Value.Date)
                upper = historyEnd.Value.Date;

            // A fixed-duration last champion must fit through the timeline end,
            // even if nobody is explicitly pinned as the successor.
            DateTime? deadline = i + 1 < input.Length ? latest[i + 1]
                : historyEnd?.Date.AddDays(1);
            if (deadline.HasValue)
            {
                while (upper >= lower &&
                    EarliestFollowingCapture(win, upper, i + 1 < input.Length &&
                        win.WorkerId == input[i + 1].WorkerId) > deadline.Value)
                    upper = upper.AddDays(-1);
            }
            if (upper < lower)
                throw new InvalidOperationException(
                    $"The exact reign length for {win.Name} ({win.Year}) cannot fit " +
                    "between the selected history dates and the other fixed superstar wins. " +
                    "Change the winning years, history range, or reign lengths.");
            latest[i] = upper;
        }

        var scheduled = new List<Scheduled>(input.Length);
        for (int i = 0; i < input.Length; i++)
        {
            var win = input[i];
            var lower = historyStart.HasValue && win.Year == historyStart.Value.Year
                ? historyStart.Value.Date : new DateTime(win.Year, 1, 1);
            if (win.NotBefore.HasValue && lower < win.NotBefore.Value.Date)
                lower = win.NotBefore.Value.Date;

            if (i > 0)
            {
                var prev = input[i - 1];
                var prevDate = scheduled[^1].Date;
                var afterPrevious = EarliestFollowingCapture(prev, prevDate,
                    prev.WorkerId == win.WorkerId);
                if (lower < afterPrevious) lower = afterPrevious;
            }
            if (lower > latest[i])
                throw new InvalidOperationException(
                    $"Cannot fit {win.Name}'s {win.Year} title victory without violating " +
                    "a fixed reign length, tournament chronology, or the history range. " +
                    "Move an existing winning year or change the requested duration.");

            // Keep the familiar randomly varied within-year date when possible,
            // but clamp to the reserved feasible interval. Group positions are
            // computed from the actual opening month for the first year.
            var sameYear = input.Where(x => x.Year == win.Year).ToArray();
            int position = Array.IndexOf(sameYear, win);
            var yearStart = historyStart.HasValue && win.Year == historyStart.Value.Year
                ? historyStart.Value.Date : new DateTime(win.Year, 1, 1);
            int totalDays = (new DateTime(win.Year, 12, 31) - yearStart).Days + 1;
            int baseDay = totalDays * (position + 1) / (sameYear.Length + 1);
            int jitterRange = Math.Min(21, totalDays / (sameYear.Length + 1) / 4);
            int jitter = jitterRange > 0
                ? Random.Shared.Next(-jitterRange, jitterRange + 1) : 0;
            var preferred = yearStart.AddDays(baseDay + jitter);
            var date = preferred < lower ? lower :
                preferred > latest[i] ? latest[i] : preferred;

            scheduled.Add(new Scheduled(date, win.WorkerId, win.Name,
                win.ReignDays, win.ReignYears));
        }
        return scheduled;
    }
}
