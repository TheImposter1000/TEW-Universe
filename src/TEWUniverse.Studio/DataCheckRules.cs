using System.Text.RegularExpressions;

namespace TEWUniverse.Studio;

/// <summary>
/// Read-only, conservative diagnostic rules. Structured references can be
/// definite errors; natural-language biography claims are review hints only.
/// </summary>
internal static class DataCheckRules
{
    internal static int? ClaimedReigns(string biography, string titleName)
    {
        if (string.IsNullOrWhiteSpace(biography) ||
            string.IsNullOrWhiteSpace(titleName) || titleName.Trim().Length < 6)
            return null;
        if (biography.IndexOf(titleName.Trim(), StringComparison.OrdinalIgnoreCase) < 0)
            return null;

        // A nearby "3-time" belonging to a different title must not become
        // a claim for this title. Demand direct adjacency between the count
        // and the championship name. Support "3-time TITLE champion" and
        // "TITLE (3 times)" but not a disconnected number in surrounding text.
        string title = Regex.Escape(titleName.Trim()).Replace(@"\ ", @"\s+");
        string leading = @"(?<!\w)(\d{1,2})\s*(?:[-–]\s*|\s+)time\s+" +
            title + @"(?!\w)";
        string trailing = @"(?<!\w)" + title +
            @"(?!\w)\s*(?:champion\s*)?\(\s*(\d{1,2})\s*times?\s*\)";

        var counts = new List<int>();
        foreach (string pattern in new[] { leading, trailing })
            foreach (Match match in Regex.Matches(biography, pattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                if (int.TryParse(match.Groups[1].Value, out int value) &&
                    value is >= 1 and <= 99)
                    counts.Add(value);

        // Multiple conflicting counts are too ambiguous to report.
        return counts.Count > 0 && counts.Distinct().Count() == 1
            ? counts[0] : null;
    }

    internal static bool MentionsTeam(string biography, string teamName)
    {
        if (string.IsNullOrWhiteSpace(biography) ||
            string.IsNullOrWhiteSpace(teamName) || teamName.Length < 6) return false;
            if (biography.IndexOf(teamName.Trim(), StringComparison.OrdinalIgnoreCase) < 0)
            return false;
        string name = Regex.Escape(teamName.Trim());
        const string introduction =
            @"(?:\b(?:tag\s+team|stable|faction)\s+(?:called\s+|named\s+|known\s+as\s+)?" +
            @"|\b(?:member\s+of|part\s+of|teamed\s+with|formed\s+(?:a\s+)?team\s+(?:called\s+)?|joined\s+)\s+)";
        return Regex.IsMatch(biography,
            introduction + @"(?<!\w)" + name +
            @"(?!\w)(?!\s+(?:division|mask|legacy|title|championship))",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    internal static bool MissingLinkedId(int uid, ISet<int> valid) =>
        uid > 0 && !valid.Contains(uid);

    internal static bool InvalidNegativeId(int uid) => uid < -1;

    internal static bool DuplicateTagWinners(int first, int second) =>
        first > 0 && first == second;

    internal static bool ValidAwardYear(int year) => year is >= 1800 and <= 2100;

    internal static bool IsNameOnlyHolder(int uid) => uid == -1;

    internal static string Csv(string value) =>
        "\"" + value.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
}
