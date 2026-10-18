namespace TEWUniverse.Desktop;

/// <summary>
/// Places pinned, real-worker championship wins inside the requested year,
/// respecting optional prior tournament victories and seven-day spacing.
/// This is scheduling only; it never changes recorded tournament history.
/// </summary>
internal static class ChampionshipWinScheduler
{
    internal sealed record Win(int WorkerId, string Name, int Year, DateTime? NotBefore = null);
    internal sealed record Scheduled(DateTime Date, int WorkerId, string Name);

    internal static List<Scheduled> Schedule(IReadOnlyList<Win> wins,
        DateTime? historyStart = null)
    {
        const int minimumTransitionDays = 7;
        var result = new List<Scheduled>();
        foreach (var group in wins.GroupBy(w => w.Year).OrderBy(g => g.Key))
        {
            int year = group.Key;
            if (year is < 1800 or > 2100)
                throw new InvalidOperationException("Fixed championship wins require years between 1800 and 2100.");
            if (historyStart.HasValue && year < historyStart.Value.Year)
                throw new InvalidOperationException(
                    "A fixed championship-winning year cannot precede the history start date.");
            var first = historyStart.HasValue && year == historyStart.Value.Year
                ? historyStart.Value.Date : new DateTime(year, 1, 1);
            var last = new DateTime(year, 12, 31);

            // Give workers with prior tournament victories earlier available
            // slots first, rather than allowing a later-eligible worker to
            // consume the only feasible space for an earlier-eligible winner.
            var items = group.OrderBy(w => w.NotBefore.HasValue &&
                    w.NotBefore.Value.Date > first ? w.NotBefore.Value.Date : first)
                .ThenBy(w => w.WorkerId).ToArray();
            int daysInYear = (last - first).Days + 1;
            if (items.Length > daysInYear / minimumTransitionDays)
                throw new InvalidOperationException(
                    $"Too many fixed championship wins in {year} to space them safely.");

            DateTime? previous = null;
            for (int i = 0; i < items.Length; i++)
            {
                var win = items[i];
                if (win.NotBefore.HasValue && win.NotBefore.Value.Year != year)
                    throw new InvalidOperationException(
                        $"The chronology constraint for {win.Name} must be inside {year}.");

                var earliest = win.NotBefore?.Date ?? first;
                if (previous.HasValue && earliest < previous.Value.AddDays(minimumTransitionDays))
                    earliest = previous.Value.AddDays(minimumTransitionDays);
                var latest = last.AddDays(-minimumTransitionDays * (items.Length - i - 1));
                if (earliest > latest)
                    throw new InvalidOperationException(
                        $"Cannot schedule {win.Name}'s {year} championship win within the " +
                        "available dates. The chosen start month, tournament chronology, " +
                        "or other fixed wins leave too little room. Adjust the start month, " +
                        "winning years, or optional tournament-date constraint.");

                int baseDay = daysInYear * (i + 1) / (items.Length + 1);
                int jitterRange = Math.Min(21, daysInYear / (items.Length + 1) / 4);
                int jitter = jitterRange > 0
                    ? Random.Shared.Next(-jitterRange, jitterRange + 1) : 0;
                var proposed = first.AddDays(baseDay + jitter);
                var captured = proposed < earliest ? earliest
                    : proposed > latest ? latest : proposed;
                result.Add(new Scheduled(captured, win.WorkerId, win.Name));
                previous = captured;
            }
        }
        return result.OrderBy(w => w.Date).ToList();
    }
}
