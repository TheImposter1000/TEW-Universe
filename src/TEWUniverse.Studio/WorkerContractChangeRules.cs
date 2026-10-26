using System.Globalization;

namespace TEWUniverse.Studio;

/// <summary>
/// Contract editors compare meaningful typed values, not just text formatting.
/// "True" and "1", "10" and "010", or equivalent MM/DD/YYYY dates are
/// the same original database value. Genuine edits must always be detected.
/// </summary>
internal static class WorkerContractChangeRules
{
    private static bool TryBoolean(string raw, out bool value)
    {
        if (bool.TryParse(raw, out value)) return true;
        if (raw == "1") { value = true; return true; }
        if (raw == "0") { value = false; return true; }
        value = false;
        return false;
    }

    public static bool Equivalent(string oldValue, string newValue, Type fieldType)
    {
        if (string.Equals(oldValue, newValue, StringComparison.Ordinal)) return true;
        if (fieldType == typeof(string)) return false;
        if (fieldType == typeof(bool))
            return TryBoolean(oldValue, out bool oldFlag) &&
                TryBoolean(newValue, out bool newFlag) && oldFlag == newFlag;
        if (fieldType == typeof(DateTime))
        {
            return DateTime.TryParseExact(oldValue,
                    new[] { "MM/dd/yyyy", "M/d/yyyy", "yyyy-MM-dd" },
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var before) &&
                DateTime.TryParseExact(newValue,
                    new[] { "MM/dd/yyyy", "M/d/yyyy", "yyyy-MM-dd" },
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var after) &&
                before == after;
        }
        if (fieldType == typeof(byte) || fieldType == typeof(short) ||
            fieldType == typeof(int) || fieldType == typeof(long) ||
            fieldType == typeof(decimal) || fieldType == typeof(double) ||
            fieldType == typeof(float))
            return decimal.TryParse(oldValue, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out decimal before) &&
                decimal.TryParse(newValue, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out decimal after) &&
                before == after;
        return false;
    }
}
