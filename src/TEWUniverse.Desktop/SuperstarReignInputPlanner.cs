using System.Globalization;
using System.Text.RegularExpressions;

namespace TEWUniverse.Desktop;

/// <summary>
/// Maps a wrestler's requested count to ordered wins. Year entries and
/// durations are BOTH optional per slot, rather than forcing a distinct year
/// and a fixed length for each of N wins. The unassigned years are distributed
/// within the specified eligible window; repeated years are permitted.
/// </summary>
internal static class SuperstarReignInputPlanner
{
    internal sealed record Request(
        string Name, int WorkerId, int FirstYear, int LastYear,
        int Reigns, string YearsWon, string ReignLengths);

    internal static List<AnchoredHistoryPlanner.Anchor> Expand(
        IReadOnlyList<Request> requests, int firstYear, int lastYear,
        bool annualTournament = false, Random? random = null)
    {
        random ??= Random.Shared;
        var output = new List<AnchoredHistoryPlanner.Anchor>();
        int order = 0;
        foreach (var request in requests)
        {
            if (request.WorkerId <= 0 || string.IsNullOrWhiteSpace(request.Name) ||
                request.Name.Length > 30 || request.Name.Any(char.IsControl) ||
                request.Reigns is < 1 or > 500 ||
                request.FirstYear > request.LastYear ||
                Math.Max(request.FirstYear, firstYear) >
                    Math.Min(request.LastYear, lastYear))
                throw new InvalidOperationException(
                    $"Invalid wrestler, active years, or reign count for {request.Name}.");

            // A supplied year locks that specific reign; a dash or missing
            // entry leaves the year to the generator. Repeated years are valid
            // for championships, but not annual Cup editions.
            var yearTokens = request.YearsWon.Trim().Length == 0
                ? Array.Empty<string>()
                : request.YearsWon.Split(new[] { ',', ';', ' ', '\n', '\r', '\t' },
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (yearTokens.Length > request.Reigns)
                throw new InvalidOperationException(
                    $"{request.Name} has {request.Reigns} reign(s), but " +
                    $"{yearTokens.Length} winning-year entries.");

            int earliestYear = Math.Max(request.FirstYear, firstYear);
            int latestYear = Math.Min(request.LastYear, lastYear);
            int?[] years = new int?[request.Reigns];
            int previous = earliestYear;
            for (int i = 0; i < yearTokens.Length; i++)
            {
                var token = yearTokens[i];
                if (token == "-" || token.Equals("auto", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!int.TryParse(token, NumberStyles.None,
                        CultureInfo.InvariantCulture, out int year) ||
                    year < earliestYear || year > latestYear)
                    throw new InvalidOperationException(
                        $"Invalid winning year '{token}' for {request.Name}. " +
                        "Use a year in their active window, '-' or auto.");
                if (year < previous)
                    throw new InvalidOperationException(
                        $"List {request.Name}'s winning years in chronological order, " +
                        "including repeated years such as 2016, 2016, 2017.");
                years[i] = year;
                previous = year;
            }

            var lengthTokens = request.ReignLengths.Trim().Length == 0
                ? Array.Empty<string>()
                : request.ReignLengths.Split(new[] { ',', ';' },
                    StringSplitOptions.TrimEntries);
            if (lengthTokens.Length > request.Reigns)
                throw new InvalidOperationException(
                    $"{request.Name} has {request.Reigns} reign(s) but " +
                    $"{lengthTokens.Length} duration entries. Leave unneeded " +
                    "durations empty or use '-'.");
            var days = new int?[request.Reigns];
            var yearsLength = new int?[request.Reigns];
            var months = new int?[request.Reigns];
            for (int i = 0; i < lengthTokens.Length; i++)
            {
                string token = lengthTokens[i];
                if (token.Length == 0 || token == "-" ||
                    token.Equals("random", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (annualTournament)
                    throw new InvalidOperationException(
                        "Annual Cup editions cannot have a continuous championship reign length.");
                var match = Regex.Match(token,
                    @"^(?<n>\d+)\s*(?<unit>d|days?|m|months?|y|years?)?$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (!match.Success ||
                    !int.TryParse(match.Groups["n"].Value,
                        NumberStyles.None, CultureInfo.InvariantCulture, out int amount))
                    throw new InvalidOperationException(
                        $"Invalid duration '{token}' for {request.Name}. " +
                        "Use 719d, 2y, 6m or '-'.");
                string unit = match.Groups["unit"].Value.ToLowerInvariant();
                if (unit.StartsWith("y")) yearsLength[i] = amount;
                else if (unit.StartsWith("m")) months[i] = amount;
                else days[i] = amount;
                if (days[i] is int d && d is < 1 or > 36525 ||
                    yearsLength[i] is int y && y is < 1 or > 100 ||
                    months[i] is int m && m is < 1 or > 1200)
                    throw new InvalidOperationException(
                        $"Reign duration '{token}' for {request.Name} is out of range.");
            }

            if (annualTournament && (yearTokens.Length != request.Reigns ||
                years.Any(y => !y.HasValue) ||
                years.Select(y => y!.Value).Distinct().Count() != years.Length))
                throw new InvalidOperationException(
                    $"Annual Cups require one distinct winning year for every win by {request.Name}. " +
                    "Flexible years and repeat victories in the same edition apply to championships only.");

            // Each open run is bounded by nearest specified years; remaining
            // reigns can share a year. Sampling and sorting makes it possible
            // to have four championship reigns within three years, etc.
            for (int i = 0; i < years.Length;)
            {
                if (years[i].HasValue) { i++; continue; }
                int start = i;
                while (i < years.Length && !years[i].HasValue) i++;
                int lower = start > 0 ? years[start - 1]!.Value : earliestYear;
                int upper = i < years.Length ? years[i]!.Value : latestYear;
                if (upper < lower)
                    throw new InvalidOperationException(
                        $"Unassigned winning years for {request.Name} have no valid interval.");
                var sampled = Enumerable.Range(start, i - start)
                    .Select(_ => random.Next(lower, upper + 1)).OrderBy(y => y).ToArray();
                for (int j = 0; j < sampled.Length; j++)
                    years[start + j] = sampled[j];
            }
            for (int i = 0; i < request.Reigns; i++)
                output.Add(new AnchoredHistoryPlanner.Anchor(
                    request.Name.Trim(), request.WorkerId, years[i]!.Value,
                    request.FirstYear, request.LastYear,
                    ReignDays: days[i], ReignYears: yearsLength[i],
                    ReignMonths: months[i], SourceOrder: order++));
        }
        if (output.Count > 500)
            throw new InvalidOperationException(
                "The requested linked superstar wins exceed the 500-record title history limit.");
        return output;
    }
}
