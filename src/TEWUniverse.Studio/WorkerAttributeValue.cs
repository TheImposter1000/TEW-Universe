using System.Globalization;

namespace TEWUniverse.Studio;

/// <summary>
/// Verified attribute labels are separate from the raw numeric MDB field.
/// Exactly five supplied personality codes are 'Unassigned' (reserved);
/// they may be displayed and preserved but never newly assigned.
/// </summary>
internal sealed record WorkerAttributeOption(
    string StoredCode, string DisplayText, bool Verified, bool Assignable)
{
    public override string ToString() => DisplayText;
}

internal static class WorkerAttributeValue
{
    private static IReadOnlyList<TewCodeChoice> VerifiedCodes =>
        TewCodeLabels.Choices("tblAttribute", "Attribute");

    internal static bool IsNumeric(Type type) =>
        type == typeof(byte) || type == typeof(short) ||
        type == typeof(int) || type == typeof(long);

    internal static bool IsSupported(Type type) =>
        type == typeof(string) || IsNumeric(type);

    internal static bool IsReserved(string raw) =>
        TewCodeLabels.TryGetLabel("tblAttribute", "Attribute", raw, out string label) &&
        label == "Unassigned";

    internal static bool IsAssignable(string raw) =>
        TewCodeLabels.TryGetLabel("tblAttribute", "Attribute", raw, out string label) &&
        label != "Unassigned";

    internal static bool MaySaveCode(string proposed, string? original) =>
        IsAssignable(proposed) ||
        (original != null && string.Equals(proposed.Trim(), original.Trim(),
            StringComparison.Ordinal));

    internal static string Category(int code) => code switch
    {
        >= 1 and <= 28 => "Personality",
        >= 310 and <= 315 => "Alignment",
        >= 320 and <= 339 => "Gimmick",
        >= 374 and <= 376 => "Other Performance",
        _ => "Unmapped"
    };

    internal static string Label(string raw, Type type)
    {
        if (!IsNumeric(type)) return raw;
        if (TewCodeLabels.TryGetLabel("tblAttribute", "Attribute", raw, out string label) &&
            TewCodeLabels.TryGetCode(raw, out int code))
            return Category(code) + ": " + label + " (Code " + code +
                (label == "Unassigned" ? ", reserved)" : ")");
        return "Code " + raw + " (unmapped)";
    }

    internal static IReadOnlyList<WorkerAttributeOption> Options(
        IEnumerable<string> observed)
    {
        var options = VerifiedCodes.ToDictionary(
            v => v.Code.ToString(CultureInfo.InvariantCulture),
            v => new WorkerAttributeOption(
                v.Code.ToString(CultureInfo.InvariantCulture),
                Label(v.Code.ToString(CultureInfo.InvariantCulture), typeof(int)),
                Verified: true, Assignable: v.Label != "Unassigned"),
            StringComparer.OrdinalIgnoreCase);
        foreach (string raw in observed)
        {
            string canonical = raw.Trim();
            if (canonical.Length == 0 || !long.TryParse(canonical,
                NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
                continue;
            canonical = value.ToString(CultureInfo.InvariantCulture);
            if (!options.ContainsKey(canonical))
                options.Add(canonical, new WorkerAttributeOption(
                    canonical, Label(canonical, typeof(int)),
                    Verified: false, Assignable: false));
        }
        return options.Values.OrderBy(x =>
            long.Parse(x.StoredCode, CultureInfo.InvariantCulture)).ToArray();
    }

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
                "This database stores numeric attribute codes. Select a numeric code, not a text name.");
        try { return Convert.ChangeType(code, type, CultureInfo.InvariantCulture); }
        catch (OverflowException ex)
        {
            throw new InvalidOperationException("Attribute code is outside the MDB column range.", ex);
        }
    }

    internal static object BooleanFlag(bool value, Type columnType)
    {
        if (columnType == typeof(bool)) return value;
        if (IsNumeric(columnType))
            return Convert.ChangeType(value ? 1 : 0, columnType, CultureInfo.InvariantCulture);
        throw new InvalidOperationException(
            "Unsupported attribute flag storage type: " + columnType.Name);
    }
}
