namespace TEWUniverse.Desktop;

/// <summary>
/// Calculates the actual date of an annual event described by a month,
/// ordinal week, and weekday. No fixed day-of-month is stored.
/// </summary>
internal static class AnnualTournamentCalendar
{
    /// <param name="occurrence">1–4 for first through fourth, 5 for last.</param>
    internal static DateTime ResolveDate(int year, int month, int occurrence, DayOfWeek weekday)
    {
        if (year is < 1800 or > 2100 || month is < 1 or > 12 ||
            occurrence is < 1 or > 5 || (int)weekday is < 0 or > 6)
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
}
