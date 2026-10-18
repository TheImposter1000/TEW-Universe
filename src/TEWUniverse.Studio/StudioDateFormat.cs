using System.Globalization;

namespace TEWUniverse.Studio;

/// <summary>
/// User-facing dates throughout TEW Universe use month/day/year in US order.
/// The MDB continues to store native DateTime values; this formatter only
/// governs displayed text, manual input, previews, and CSV exports.
/// </summary>
internal static class StudioDateFormat
{
    internal const string Pattern = "MM/dd/yyyy";
    internal const string Hint = "MM/DD/YYYY";

    internal static string Format(DateTime date) =>
        date.ToString(Pattern, CultureInfo.InvariantCulture);

    internal static bool TryParse(string? text, out DateTime date) =>
        DateTime.TryParseExact(text?.Trim(), Pattern,
            CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    internal static DateTime Parse(string text)
    {
        if (TryParse(text, out var date)) return date;
        throw new FormatException(
            $"Enter a valid date in {Hint} format (for example, 09/23/1990).");
    }
}
