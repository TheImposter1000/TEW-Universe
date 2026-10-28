using System.Data;
using System.Globalization;

namespace TEWUniverse.Studio;

/// <summary>
/// A TEW IX scenario starts at the beginning of StartMonth/StartYear in tblInfo.
/// Pre-game history therefore stops on the last day of the preceding month.
/// Company founding/closure may be actual dates (CompanyOpening/CompanyClosing)
/// or legacy month/year fields. Never discard available day precision.
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

    // TEW IX tblFed's actual full-date columns take precedence over legacy
    // month/year columns. A zero/1666/null closing date means still active.
    internal static (DateTime? Opened, DateTime? Closed) ReadCompanyDates(DataRow row)
    {
        DateTime? Exact(string name)
        {
            if (!row.Table.Columns.Contains(name)) return null;
            object value = row[name];
            if (value is null or DBNull) return null;
            if (value is DateTime date)
                return date.Year < 1800 ? null : date.Date;
            string raw = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? "";
            if (raw.Length == 0 || raw is "0" or "None") return null;
            if (!DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsed))
                throw new InvalidOperationException(
                    "Company " + name + " has an invalid date: " + raw);
            return parsed.Year < 1800 ? null : parsed.Date;
        }

        DateTime? Legacy(string yearName, string monthName, bool closing)
        {
            if (!row.Table.Columns.Contains(yearName) ||
                !row.Table.Columns.Contains(monthName)) return null;
            int Value(string column) => row[column] is DBNull ? 0 :
                Convert.ToInt32(row[column], CultureInfo.InvariantCulture);
            int year = Value(yearName), month = Value(monthName);
            if (year is 0 or 1666) return null;
            return closing ? MonthEnd(year, month) : MonthStart(year, month);
        }

        // If the precise field exists, it is authoritative, even when blank:
        // do not fall back to stale legacy values for a currently active company.
        DateTime? opened = row.Table.Columns.Contains("CompanyOpening")
            ? Exact("CompanyOpening") : Legacy("OpenYear", "OpenMonth", false);
        DateTime? closed = row.Table.Columns.Contains("CompanyClosing")
            ? Exact("CompanyClosing") : Legacy("ClosedYear", "ClosedMonth", true);
        if (opened.HasValue && closed.HasValue && opened.Value > closed.Value)
            throw new InvalidOperationException(
                "Company closing date precedes its opening date. Correct the company record.");
        return (opened, closed);
    }

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
