using System.Globalization;
using System.Text.RegularExpressions;

namespace TEWUniverse.Studio;

/// <summary>
/// All manual dates use US month/day/year order. Users may type numeric dates
/// (5/12/1998 or 05/12/1998) or English month names (May 12th 1998).
/// The MDB still stores native DateTime values; previews and CSV exports stay
/// consistently formatted as MM/dd/yyyy.
/// </summary>
internal static class StudioDateFormat
{
    internal const string Pattern = "MM/dd/yyyy";
    internal const string Hint = "M/D/YYYY or Month Day Year";

    private static readonly Regex WrittenDate = new(
        @"^(?<month>[A-Za-z]+)\s+(?<day>\d{1,2})(?<suffix>st|nd|rd|th)?(?:\s*,\s*|\s+)(?<year>\d{4})$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    internal static string Format(DateTime date) =>
        date.ToString(Pattern, CultureInfo.InvariantCulture);

    internal static bool TryParse(string? text, out DateTime date)
    {
        date = default;
        string input = text?.Trim() ?? "";
        if (input.Length == 0) return false;

        // This explicitly keeps US month-first ordering, including both
        // padded and unpadded numbers; ISO year-first is not manual input.
        if (DateTime.TryParseExact(input, "M/d/yyyy",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            return true;

        var match = WrittenDate.Match(input);
        if (!match.Success) return false;

        string dayText = match.Groups["day"].Value;
        if (!int.TryParse(dayText, NumberStyles.None,
                CultureInfo.InvariantCulture, out int day) ||
            day is < 1 or > 31)
            return false;
        string ordinal = match.Groups["suffix"].Value;
        if (ordinal.Length > 0)
        {
            string expected = day % 100 is >= 11 and <= 13 ? "th" :
                day % 10 switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
            if (!ordinal.Equals(expected, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        string month = match.Groups["month"].Value;
        if (month.Equals("Sept", StringComparison.OrdinalIgnoreCase))
            month = "Sep";
        string normalized = month + " " + day.ToString(CultureInfo.InvariantCulture) +
            " " + match.Groups["year"].Value;
        return DateTime.TryParseExact(normalized,
            new[] { "MMMM d yyyy", "MMM d yyyy" },
            CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    internal static DateTime Parse(string text)
    {
        if (TryParse(text, out var date)) return date;
        throw new FormatException(
            $"Enter a valid date as {Hint} (e.g. 5/12/1998 or May 12th 1998).");
    }
}
