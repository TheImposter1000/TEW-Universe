namespace TEWUniverse.Desktop;

/// <summary>
/// Schedules each superstar's wins within its specified (or automatically
/// assigned) year. Repeated years are valid. The scheduler reserves exact-day
/// reigns and the MINIMUM duration of approximate month/year reigns before
/// placing any subsequent champion.
/// </summary>
internal static class ChampionshipWinScheduler
{
    internal sealed record Win(int WorkerId, string Name, int Year,
        DateTime? NotBefore = null, int? ReignDays = null, int? ReignYears = null,
        int? ReignMonths = null, int SourceOrder = 0);

    internal sealed record Scheduled(DateTime Date, int WorkerId, string Name,
        int? ReignDays = null, int? ReignYears = null,
        int? ReignMonths = null, DateTime? NextCapture = null,
        int SourceOrder = 0);

    /// <summary>
    /// Reign days are inclusive. The specified next-capture date is EXCLUSIVE:
    /// BeltLost = NextCapture - 1 day, so 719d means exactly 719 days held.
    /// A 2y / 6m request is a minimum calendar duration, with optional extra
    /// days chosen only when the other required wins leave enough room.
    /// </summary>
    internal static DateTime? RequiredNextCaptureDate(Scheduled win) => win.NextCapture;

    private static DateTime? MinimumNextCapture(Win win, DateTime captured) =>
        win.ReignDays is int days ? captured.AddDays(days) :
        win.ReignYears is int years ? captured.AddYears(years) :
        win.ReignMonths is int months ? captured.AddMonths(months) : null;

    private static DateTime EarliestFollowingCapture(Win win, DateTime date, bool sameWorker)
    {
        var expiry = MinimumNextCapture(win, date);
        if (expiry.HasValue)
            return expiry.Value.AddDays(sameWorker ? 7 : 0);
        return date.AddDays(sameWorker ? 14 : 7);
    }

    internal static List<Scheduled> Schedule(IReadOnlyList<Win> wins,
        DateTime? historyStart = null, DateTime? historyEnd = null)
    {
        var input = wins
            .GroupBy(w => w.Year).OrderBy(g => g.Key)
            .SelectMany(g => g.OrderBy(w => w.NotBefore?.Date ?? new DateTime(g.Key, 1, 1))
                .ThenBy(w => w.SourceOrder).ThenBy(w => w.WorkerId))
            .ToArray();
        if (input.Length == 0) return new();

        foreach (var win in input)
        {
            if (win.Year is < 1800 or > 2100)
                throw new InvalidOperationException("Championship winning years must be within 1800–2100.");
            if (win.NotBefore.HasValue && win.NotBefore.Value.Year != win.Year)
                throw new InvalidOperationException(
                    $"Tournament chronology for {win.Name} must be in {win.Year}.");
            int supplied = (win.ReignDays.HasValue ? 1 : 0) +
                (win.ReignYears.HasValue ? 1 : 0) +
                (win.ReignMonths.HasValue ? 1 : 0);
            if (supplied > 1 ||
                win.ReignDays is int d && d is < 1 or > 36525 ||
                win.ReignYears is int y && y is < 1 or > 100 ||
                win.ReignMonths is int m && m is < 1 or > 1200)
                throw new InvalidOperationException(
                    $"Invalid reign duration for {win.Name} ({win.Year}). " +
                    "Use one positive day, month, or year duration.");
            if (historyStart.HasValue && win.Year < historyStart.Value.Year ||
                historyEnd.HasValue && win.Year > historyEnd.Value.Year)
                throw new InvalidOperationException(
                    $"{win.Name}'s winning year is outside the title's history.");
        }

        // Work backwards from the timeline end to discover the latest feasible
        // capture for each pinned win. Approximate durations reserve their
        // minimum, never their optional extra, avoiding false conflicts.
        var latest = new DateTime[input.Length];
        for (int i = input.Length - 1; i >= 0; i--)
        {
            var win = input[i];
            var lower = historyStart.HasValue && win.Year == historyStart.Value.Year
                ? historyStart.Value.Date : new DateTime(win.Year, 1, 1);
            var upper = new DateTime(win.Year, 12, 31);
            if (historyEnd.HasValue && upper > historyEnd.Value.Date)
                upper = historyEnd.Value.Date;
            DateTime? deadline = i + 1 < input.Length ? latest[i + 1]
                : historyEnd?.Date.AddDays(1);

            if (deadline.HasValue)
                while (upper >= lower &&
                    EarliestFollowingCapture(win, upper, i + 1 < input.Length &&
                        input[i + 1].WorkerId == win.WorkerId) > deadline.Value)
                    upper = upper.AddDays(-1);
            if (upper < lower)
                throw new InvalidOperationException(
                    $"Cannot fit {win.Name}'s {win.Year} victory and requested reign " +
                    "within the history and other superstar wins. Adjust winning years, " +
                    "the active window, or a duration.");
            latest[i] = upper;
        }

        var output = new List<Scheduled>();
        for (int i = 0; i < input.Length; i++)
        {
            var win = input[i];
            var lower = historyStart.HasValue && win.Year == historyStart.Value.Year
                ? historyStart.Value.Date : new DateTime(win.Year, 1, 1);
            if (win.NotBefore.HasValue && lower < win.NotBefore.Value.Date)
                lower = win.NotBefore.Value.Date;
            if (i > 0)
            {
                var previous = output[^1];
                var following = previous.NextCapture.HasValue
                    ? previous.NextCapture.Value.AddDays(
                        previous.WorkerId == win.WorkerId ? 7 : 0)
                    : previous.Date.AddDays(
                        previous.WorkerId == win.WorkerId ? 14 : 7);
                if (following > lower) lower = following;
            }
            if (lower > latest[i])
                throw new InvalidOperationException(
                    $"Cannot schedule {win.Name}'s victory in {win.Year} while preserving " +
                    "the requested duration(s). Repeated years are allowed, but " +
                    "the intervening reigns must fit chronologically.");

            var group = input.Where(x => x.Year == win.Year).ToArray();
            int rank = Array.IndexOf(group, win);
            var yearStart = historyStart.HasValue && historyStart.Value.Year == win.Year
                ? historyStart.Value.Date : new DateTime(win.Year, 1, 1);
            int span = (new DateTime(win.Year, 12, 31) - yearStart).Days + 1;
            var preferred = yearStart.AddDays(span * (rank + 1) / (group.Length + 1) +
                Random.Shared.Next(-Math.Min(21, span / (group.Length + 1) / 4),
                    Math.Min(21, span / (group.Length + 1) / 4) + 1));
            var date = preferred < lower ? lower :
                preferred > latest[i] ? latest[i] : preferred;

            DateTime? nextCapture = MinimumNextCapture(win, date);
            if (nextCapture.HasValue && (win.ReignYears.HasValue || win.ReignMonths.HasValue))
            {
                int permittedExtra = win.ReignYears.HasValue ? 90 : 30;
                // Clamp natural overrun to the free space before the NEXT
                // reserved win, or to the end of the selected history.
                var deadline = i + 1 < input.Length ? latest[i + 1] :
                    historyEnd?.Date.AddDays(1);
                if (deadline.HasValue)
                {
                    int bridge = i + 1 < input.Length &&
                        input[i + 1].WorkerId == win.WorkerId ? 7 : 0;
                    permittedExtra = Math.Min(permittedExtra,
                        (deadline.Value - nextCapture.Value).Days - bridge);
                }
                if (permittedExtra < 0)
                    throw new InvalidOperationException(
                        $"{win.Name}'s approximate reign cannot meet its minimum duration.");
                // 2y = two calendar years plus 0–90 days if space allows.
                // 6m = six calendar months plus 0–30 days if space allows.
                nextCapture = nextCapture.Value.AddDays(
                    Random.Shared.Next(permittedExtra + 1));
            }
            output.Add(new Scheduled(date, win.WorkerId, win.Name,
                win.ReignDays, win.ReignYears, win.ReignMonths,
                nextCapture, win.SourceOrder));
        }
        return output;
    }
}
