namespace TEWUniverse.Studio;

/// <summary>
/// A TEW IX scenario starts at the beginning of StartMonth/StartYear in tblInfo.
/// Pre-game history therefore stops on the last day of the preceding month.
/// Company founding and closure are month/year fields, not exact dates.
/// </summary>
internal sealed record ScenarioChronology(
    DateTime ScenarioStart, int? CompanyUid, string CompanyName,
    DateTime? CompanyOpened, DateTime? CompanyClosed);

internal sealed record HistoricalDateWindow(
    DateTime From, DateTime Through, string Explanation);

internal static class ScenarioDateBounds
{
    internal static DateTime MonthStart(int year, int month) =>
        year is >= 1800 and <= 2100 && month is >= 1 and <= 12
            ? new DateTime(year, month, 1)
            : throw new InvalidOperationException(
                "Dates must have a year 1800–2100 and month 1–12.");

    internal static DateTime MonthEnd(int year, int month) =>
        MonthStart(year, month).AddMonths(1).AddDays(-1);

    internal static HistoricalDateWindow Resolve(
        DateTime requestedFrom, DateTime requestedThrough,
        ScenarioChronology calendar, bool useCompanyDates,
        DateTime? manualThrough = null)
    {
        if (requestedFrom.Year is < 1800 or > 2100 ||
            requestedThrough.Year is < 1800 or > 2100 ||
            requestedFrom > requestedThrough)
            throw new InvalidOperationException("Choose a valid history range.");
        DateTime start = requestedFrom;
        DateTime end = manualThrough ?? requestedThrough;
        if (manualThrough.HasValue && manualThrough.Value > requestedThrough)
            throw new InvalidOperationException(
                "The manual end date cannot extend beyond the selected Through year.");
        var details = new List<string>();
        if (useCompanyDates && calendar.CompanyOpened is DateTime opened &&
            start < opened)
        {
            start = opened;
            details.Add("Company began " + opened.ToString("MMMM yyyy"));
        }
        if (useCompanyDates && calendar.CompanyClosed is DateTime closed && end > closed)
        {
            end = closed;
            details.Add("Company closed " + closed.ToString("MMMM yyyy"));
        }
        DateTime scenarioCutoff = calendar.ScenarioStart.AddDays(-1);
        if (end > scenarioCutoff)
        {
            end = scenarioCutoff;
            details.Add("Scenario starts " + calendar.ScenarioStart.ToString("MMMM yyyy"));
        }
        if (start > end)
            throw new InvalidOperationException(
                "No eligible championship history dates remain in the chosen range. " +
                "Check the company opening/closure dates, scenario start and manual override.");
        return new HistoricalDateWindow(start, end,
            details.Count == 0 ? "Requested period used unchanged." :
            string.Join(". ", details) + ".");
    }
}
