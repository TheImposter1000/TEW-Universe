namespace TEWUniverse.Desktop;

/// <summary>
/// Simple text-file or pasted-name import for historical champions.
/// Lines represent complete 1/2/3-person champion lineups, never roster workers.
/// </summary>
internal static class NameOnlyChampionImport
{
    internal sealed record Line(string[] Names);

    internal static IReadOnlyList<Line> Parse(string input, int memberCount)
    {
        if (memberCount is < 1 or > 3)
            throw new InvalidOperationException("Select Singles, Tag Team, or Trios format first.");
        var result = new List<Line>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in input.Split(new[] { "\r\n", "\n", "\r" },
                     StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            // Text lists: one name per line for singles; full team per line
            // separated by / or CSV commas for Tag Team and Trios.
            char delimiter = memberCount == 1 ? ',' :
                raw.Contains('/') ? '/' : ',';
            foreach (var group in memberCount == 1
                         ? raw.Split(',', StringSplitOptions.TrimEntries |
                             StringSplitOptions.RemoveEmptyEntries)
                               .Select(n => new[] { n })
                         : new[] { raw.Split(delimiter, StringSplitOptions.TrimEntries) })
            {
                if (group.Length != memberCount ||
                    group.Any(n => n.Length is < 1 or > 30 ||
                        n.Any(char.IsControl)) ||
                    group.Distinct(StringComparer.OrdinalIgnoreCase).Count() != memberCount)
                    throw new InvalidOperationException(
                        $"Invalid historical champion entry: '{raw}'. " +
                        $"Each line needs {memberCount} distinct name(s) of 1–30 characters. " +
                        "For team titles separate members with / or commas.");
                string key = string.Join("|", group.Select(s => s.ToUpperInvariant()));
                if (!used.Add(key))
                    throw new InvalidOperationException(
                        "The same named champion/team was entered twice: " + raw);
                result.Add(new Line(group));
                if (result.Count > 500)
                    throw new InvalidOperationException(
                        "Import at most 500 named champion lineups at once.");
            }
        }
        if (result.Count == 0)
            throw new InvalidOperationException("Paste or load at least one historical champion name.");
        return result;
    }
}
