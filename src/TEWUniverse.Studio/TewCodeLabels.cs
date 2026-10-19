using System.Globalization;

namespace TEWUniverse.Studio;

/// <summary>
/// User-verified TEW IX numeric enumerations for display purposes only.
/// These labels never replace the numeric values read from or written to an MDB.
/// </summary>
internal sealed record TewCodeChoice(int Code, string Label)
{
    public override string ToString() => Label;
}

internal static class TewCodeLabels
{
    private static readonly TewCodeChoice[] Genders =
    {
        new(1, "Male"),
        new(2, "Transgender Male"),
        new(3, "Non-binary AMAB"),
        new(4, "Other Male"),
        new(5, "Female"),
        new(6, "Transgender Female"),
        new(7, "Non-binary AFAB"),
        new(8, "Other Female")
    };

    private static readonly TewCodeChoice[] TagTeamTypes =
    {
        new(1, "Permanent Unit"),
        new(2, "Unit"),
        new(3, "Individuals"),
        new(4, "Inactive Permanent Unit"),
        new(5, "Inactive Unit"),
        new(6, "Inactive Individuals")
    };

    public static bool IsMappedField(string table, string column) =>
        (table.Equals("tblWorker", StringComparison.OrdinalIgnoreCase) &&
         column.Equals("Gender", StringComparison.OrdinalIgnoreCase)) ||
        (table.Equals("tblTeam", StringComparison.OrdinalIgnoreCase) &&
         column.Equals("Type", StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<TewCodeChoice> Choices(string table, string column)
    {
        if (table.Equals("tblWorker", StringComparison.OrdinalIgnoreCase) &&
            column.Equals("Gender", StringComparison.OrdinalIgnoreCase))
            return Genders;
        if (table.Equals("tblTeam", StringComparison.OrdinalIgnoreCase) &&
            column.Equals("Type", StringComparison.OrdinalIgnoreCase))
            return TagTeamTypes;
        return Array.Empty<TewCodeChoice>();
    }

    public static bool TryGetCode(string? stored, out int code)
    {
        string text = stored?.Trim() ?? "";
        if (text.StartsWith("Code ", StringComparison.OrdinalIgnoreCase))
            text = text[5..].Trim();
        return int.TryParse(text, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out code);
    }

    public static bool TryGetLabel(string table, string column, string? stored, out string label)
    {
        label = "";
        if (!TryGetCode(stored, out int code)) return false;
        foreach (var choice in Choices(table, column))
        {
            if (choice.Code != code) continue;
            label = choice.Label;
            return true;
        }
        return false;
    }

    public static string Display(string table, string column, string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return "Not recorded";
        if (TryGetLabel(table, column, stored, out string label)) return label;
        string original = stored.Trim();
        return original.StartsWith("Code ", StringComparison.OrdinalIgnoreCase)
            ? original : "Code " + original;
    }
}
