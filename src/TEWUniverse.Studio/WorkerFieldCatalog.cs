using System.Globalization;
using System.Text.RegularExpressions;

namespace TEWUniverse.Studio;

/// <summary>
/// Presentation-only labels. The original database column names remain the keys
/// for every read/write; no undocumented TEW IX enum values are invented.
/// </summary>
internal static class WorkerFieldCatalog
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["UID"] = "Worker ID",
        ["Name"] = "Worker Name",
        ["Shortname"] = "Short Name",
        ["Birthday"] = "Date of Birth",
        ["Active"] = "Active",
        ["User"] = "User Flag",
        ["Regen"] = "Regeneration Flag",
        ["Outsiderel"] = "Outside Relationship",
        ["CompetesAgainst"] = "Competes Against",
        ["Pronouns"] = "Pronouns",
        ["Gender"] = "Gender",
        ["Bio"] = "Biography",
        ["Profile"] = "Biography",
        ["WorkerUID"] = "Worker ID"
    };

    private static readonly HashSet<string> UnverifiedCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Pronouns", "Sexuality", "CompetesAgainst", "Outsiderel"
    };

    public static bool IsUnknownCode(string name, Type type) =>
        type != typeof(bool) && type != typeof(string) &&
        UnverifiedCodes.Contains(name);

    public static string FriendlyName(string name, Type type)
    {
        var display = Names.TryGetValue(name, out var mapped) ? mapped :
            Regex.Replace(name.Replace("_", " "), @"(?<=[a-z0-9])(?=[A-Z])", " ");
        return IsUnknownCode(name, type) ? display + " (code)" : display;
    }

    public static string FieldDescription(string name, Type type)
    {
        var raw = "TEW IX column: " + name;
        if (TewCodeLabels.IsMappedField("tblWorker", name))
            return raw + ". The label is display-only; the original numeric code is saved.";
        if (IsUnknownCode(name, type))
            return raw + ". Stored numeric code: the dropdown mapping has not been verified, so edit with care.";
        if (type == typeof(bool)) return raw + ". True, False, or blank (database null).";
        if (type == typeof(DateTime)) return raw + ". Date stored in the MDB.";
        if (type == typeof(string)) return raw + ". Text field.";
        return IsNumeric(type) ? raw + ". Numeric value; storage type checked when saving. Original value is unchanged until edited." : raw + ". Original database value is preserved until edited.";
    }

    public static int ProfileOrder(string name) => name.ToLowerInvariant() switch
    {
        "name" => 0,
        "shortname" => 1,
        "active" => 2,
        "birthday" => 3,
        "gender" => 4,
        "pronouns" => 5,
        "sexuality" => 6,
        "user" => 7,
        "regen" => 8,
        _ => 100
    };

    public static bool IsNumeric(Type type) =>
        type == typeof(byte) || type == typeof(short) || type == typeof(int) ||
        type == typeof(long) || type == typeof(float) || type == typeof(double) ||
        type == typeof(decimal);

    public static bool Validate(string value, Type type, out string reason)
    {
        reason = "";
        // The Access engine remains responsible for enforcing NOT NULL constraints.
        if (string.IsNullOrEmpty(value)) return true;
        var culture = CultureInfo.InvariantCulture;
        bool valid =
            type == typeof(string) ||
            (type == typeof(bool) && (bool.TryParse(value, out _) || value is "1" or "0")) ||
            (type == typeof(DateTime) && StudioDateFormat.TryParse(value, out _)) ||
            (type == typeof(byte) && byte.TryParse(value, NumberStyles.Integer, culture, out _)) ||
            (type == typeof(short) && short.TryParse(value, NumberStyles.Integer, culture, out _)) ||
            (type == typeof(int) && int.TryParse(value, NumberStyles.Integer, culture, out _)) ||
            (type == typeof(long) && long.TryParse(value, NumberStyles.Integer, culture, out _)) ||
            (type == typeof(float) && float.TryParse(value, NumberStyles.Float, culture, out _)) ||
            (type == typeof(double) && double.TryParse(value, NumberStyles.Float, culture, out _)) ||
            (type == typeof(decimal) && decimal.TryParse(value, NumberStyles.Float, culture, out _));
        if (!valid)
        {
            reason = type == typeof(DateTime) ? "Enter a valid date in MM/DD/YYYY format, for example 09/23/1990." :
                type == typeof(bool) ? "Choose True, False, or leave blank." :
                "Enter a value compatible with the original numeric database column.";
        }
        return valid;
    }
}
