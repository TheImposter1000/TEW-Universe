using System.Text.RegularExpressions;

namespace TEWUniverse.Studio;

/// <summary>
/// Diagnostics never write to a scenario. Structured link failures are errors;
/// biography text is unstructured, so discrepancies are review prompts only.
/// </summary>
internal static class DataCheckRules
{
    internal static int? ClaimedReigns(string biography, string titleName)
    {
        if (string.IsNullOrWhiteSpace(biography) ||
            string.IsNullOrWhiteSpace(titleName) || titleName.Length < 6)
            return null;
        int at = biography.IndexOf(titleName, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return null;
        string context = biography.Substring(Math.Max(0, at - 70),
            Math.Min(biography.Length - Math.Max(0, at - 70),
                titleName.Length + 140));
        // A number of title reigns is ambiguous unless directly tied to a
        // specific championship mention. Never treat this as a hard error.
        var matches = Regex.Matches(context,
            @"\b(\d{1,2})\s*(?:[-– ]\s*)?time\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (matches.Count != 1) return null;
        if (!int.TryParse(matches[0].Groups[1].Value, out int value) || value < 1)
            return null;
        return value;
    }

    internal static bool MentionsTeam(string biography, string teamName)
    {
        if (string.IsNullOrWhiteSpace(biography) ||
            string.IsNullOrWhiteSpace(teamName) || teamName.Length < 6) return false;
        return biography.Contains(teamName, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool MissingLinkedId(int uid, ISet<int> valid) =>
        uid > 0 && !valid.Contains(uid);

    internal static bool IsNameOnlyHolder(int uid) => uid == -1;

    internal static string Csv(string value) =>
        "\"" + value.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
}
