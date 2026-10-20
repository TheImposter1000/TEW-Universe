using System.Globalization;

namespace TEWUniverse.Studio;

/// <summary>
/// TEW IX's attribute field may store a numeric category code, not a name.
/// Never write "Code N" labels to the MDB: persist the original typed number.
/// No unverified numeric code is assigned a gameplay meaning here.
/// </summary>
internal static class WorkerAttributeValue
{
    internal static bool IsNumeric(Type type) =>
        type == typeof(byte) || type == typeof(short) ||
        type == typeof(int) || type == typeof(long);

    internal static bool IsSupported(Type type) =>
        type == typeof(string) || IsNumeric(type);

    internal static object Parse(string raw, Type type)
    {
        string value = raw.Trim();
        if (value.Length == 0 || value.Any(char.IsControl))
            throw new InvalidOperationException("Select an existing attribute value.");
        if (type == typeof(string)) return value;
        if (!IsNumeric(type))
            throw new InvalidOperationException(
                "Unsupported tblAttribute.Attribute storage type: " + type.Name);
        if (!long.TryParse(value, NumberStyles.None,
                CultureInfo.InvariantCulture, out long code) || code < 0)
            throw new InvalidOperationException(
                "This database stores numeric attribute codes. Select an existing numeric code, not a name.");
        try { return Convert.ChangeType(code, type, CultureInfo.InvariantCulture); }
        catch (OverflowException ex)
        {
            throw new InvalidOperationException("Attribute code is outside the MDB column range.", ex);
        }
    }

    internal static string Label(string raw, Type type) =>
        IsNumeric(type) ? "Code " + raw + " (unverified)" : raw;

    internal static object BooleanFlag(bool value, Type columnType)
    {
        if (columnType == typeof(bool)) return value;
        if (IsNumeric(columnType))
            return Convert.ChangeType(value ? 1 : 0, columnType, CultureInfo.InvariantCulture);
        throw new InvalidOperationException(
            "Unsupported attribute flag storage type: " + columnType.Name);
    }
}
