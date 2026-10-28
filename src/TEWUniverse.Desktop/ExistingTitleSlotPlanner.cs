using System.Globalization;
using System.Text.RegularExpressions;

namespace TEWUniverse.Desktop;

internal enum TitleCompetitorFormat { Singles = 1, TagTeam = 2, Trios = 3 }

internal sealed record ExistingTitleSlot(
    int Uid, int Order, DateTime Captured, DateTime Lost, short Defences,
    int Holder1, string Name1, int Holder2, string Name2, int Holder3, string Name3)
{
    internal int[] LinkedWorkers => new[] { Holder1, Holder2, Holder3 }
        .Where(uid => uid > 0).OrderBy(uid => uid).ToArray();
    internal bool IsNameOnly => Holder1 <= 0 && Holder2 <= 0 && Holder3 <= 0;
}

internal sealed record TitleSlotRequest(
    string Name, IReadOnlyList<int> WorkerIds, IReadOnlyList<string> Names,
    int FirstYear, int LastYear, int Reigns, string YearsWon, string ReignLengths);

internal sealed record TitleSlotPatch(
    int Uid, int Order, DateTime Captured, DateTime Lost, short Defences,
    int Holder1, string Name1, int Holder2, string Name2, int Holder3, string Name3);

internal static class ExistingTitleSlotPlanner
{
    private sealed record Wanted(TitleSlotRequest Owner, int? Year,
        int? Days, int? Months, int? Years, int ReignIndex,
        int EarliestYear, int LatestYear);

    private static bool SameMembers(ExistingTitleSlot slot, TitleSlotRequest request)
    {
        var savedIds = new[] { slot.Holder1, slot.Holder2, slot.Holder3 };
        var savedNames = new[] { slot.Name1, slot.Name2, slot.Name3 };
        var stored = savedIds.Take(request.WorkerIds.Count)
            .Select((id, i) => id > 0 ? "W" + id : "N" + savedNames[i].ToUpperInvariant())
            .OrderBy(x => x).ToArray();
        var wanted = request.WorkerIds
            .Select((id, i) => id > 0 ? "W" + id : "N" + request.Names[i].ToUpperInvariant())
            .OrderBy(x => x).ToArray();
        return stored.SequenceEqual(wanted);
    }

    private static DateTime? MinimumEnd(ExistingTitleSlot slot, Wanted wanted) =>
        wanted.Years.HasValue ? slot.Captured.AddYears(wanted.Years.Value) :
        wanted.Months.HasValue ? slot.Captured.AddMonths(wanted.Months.Value) :
        wanted.Days.HasValue ? slot.Captured.AddDays(wanted.Days.Value) : null;

    private static bool LengthMatches(ExistingTitleSlot slot, Wanted wanted)
    {
        if (wanted.Days.HasValue)
            return (slot.Lost - slot.Captured).Days + 1 == wanted.Days.Value;
        var minimum = MinimumEnd(slot, wanted);
        if (!minimum.HasValue) return true;
        int extra = (slot.Lost.AddDays(1) - minimum.Value).Days;
        return extra >= 0 && extra <= (wanted.Years.HasValue ? 90 : 30);
    }

    private static List<Wanted> Expand(TitleSlotRequest request, TitleCompetitorFormat format)
    {
        if (request.WorkerIds.Count != (int)format ||
            request.Names.Count != (int)format ||
            request.WorkerIds.Any(uid => uid <= 0) ||
            request.WorkerIds.Distinct().Count() != request.WorkerIds.Count ||
            request.Names.Any(name => string.IsNullOrWhiteSpace(name) || name.Length > 30 ||
                name.Any(char.IsControl)) ||
            request.FirstYear > request.LastYear || request.Reigns is < 1 or > 500)
            throw new InvalidOperationException(
                $"Invalid worker lineup, active years, or reign count for {request.Name}. " +
                $"This title is classified as {format}.");

        var yearTokens = request.YearsWon.Trim().Length == 0 ? Array.Empty<string>() :
            request.YearsWon.Split(new[] { ',', ';', ' ', '\n', '\r', '\t' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var lengthTokens = request.ReignLengths.Trim().Length == 0 ? Array.Empty<string>() :
            request.ReignLengths.Split(new[] { ',', ';' }, StringSplitOptions.TrimEntries);
        if (yearTokens.Length > request.Reigns || lengthTokens.Length > request.Reigns)
            throw new InvalidOperationException(
                $"{request.Name}: specified years or lengths exceed the {request.Reigns} requested reigns.");

        var result = new List<Wanted>();
        int previous = request.FirstYear;
        for (int i = 0; i < request.Reigns; i++)
        {
            int? year = null, days = null, months = null, years = null;
            if (i < yearTokens.Length && yearTokens[i] is string token &&
                token != "-" && !token.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(token, NumberStyles.None,
                        CultureInfo.InvariantCulture, out int candidate) ||
                    candidate < request.FirstYear || candidate > request.LastYear ||
                    candidate < previous)
                    throw new InvalidOperationException(
                        $"{request.Name}: list known winning years in chronological order " +
                        "within the active period; repeated years are allowed.");
                year = candidate;
                previous = candidate;
            }
            if (i < lengthTokens.Length && lengthTokens[i] is string duration &&
                duration.Length > 0 && duration != "-" &&
                !duration.Equals("random", StringComparison.OrdinalIgnoreCase))
            {
                var match = Regex.Match(duration,
                    @"^(?<number>\d+)\s*(?<unit>d|days?|m|months?|y|years?)?$",
                    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
                if (!match.Success || !int.TryParse(match.Groups["number"].Value,
                        NumberStyles.None, CultureInfo.InvariantCulture, out int count))
                    throw new InvalidOperationException(
                        $"{request.Name}: use 719d (exact), 6m, 2y, or - for a random tenure.");
                string unit = match.Groups["unit"].Value.ToLowerInvariant();
                if (unit.StartsWith("y")) years = count;
                else if (unit.StartsWith("m")) months = count;
                else days = count;
                if (days is int d && d is < 1 or > 36525 ||
                    months is int m && m is < 1 or > 1200 ||
                    years is int y && y is < 1 or > 100)
                    throw new InvalidOperationException(
                        $"{request.Name}: reign duration must be positive and within the supported range.");
            }
            result.Add(new Wanted(request, year, days, months, years, i,
                request.FirstYear, request.LastYear));
        }

        // Blank positions before / after known winning years cannot cross
        // those pinned years. For example, '2011' followed by five omitted
        // slots must not backdate the other five reigns into 2008–2010.
        for (int i = 0; i < result.Count; i++)
        {
            int min = result.Take(i + 1).Where(w => w.Year.HasValue)
                .Select(w => w.Year!.Value).DefaultIfEmpty(request.FirstYear).Max();
            int max = result.Skip(i).Where(w => w.Year.HasValue)
                .Select(w => w.Year!.Value).DefaultIfEmpty(request.LastYear).Min();
            result[i] = result[i] with { EarliestYear = min, LatestYear = max };
        }
        return result;
    }

    internal static IReadOnlyList<TitleSlotPatch> Plan(
        IReadOnlyList<ExistingTitleSlot> slots,
        IReadOnlyList<TitleSlotRequest> requests, TitleCompetitorFormat format,
        Func<TitleSlotRequest, ExistingTitleSlot, int>? employmentScore = null)
    {
        if (slots.Count == 0)
            throw new InvalidOperationException("There are no existing lineage rows to replace.");
        if (requests.Count == 0)
            throw new InvalidOperationException("Add at least one linked superstar or team.");
        if (slots.Select(s => s.Uid).Distinct().Count() != slots.Count ||
            slots.Select(s => s.Order).Distinct().Count() != slots.Count)
            throw new InvalidOperationException(
                "Existing title-history UIDs or reign-order numbers are duplicated.");

        // Never overwrite ANY row with a linked real-worker UID. If such a row
        // is already one of the requested wrestler's reigns, it counts toward
        // their total instead of being recreated.
        foreach (var slot in slots)
        {
            int linked = slot.LinkedWorkers.Length;
            if (linked > 0 && linked != (int)format)
                throw new InvalidOperationException(
                    $"Existing reign #{slot.Order} has {linked} linked holder(s), " +
                    $"but the title is classified as {format}. " +
                    "Choose the correct format before generating.");
        }
        var groups = requests
            .Select(r => string.Join(",", r.WorkerIds.OrderBy(x => x)))
            .ToArray();
        if (groups.Distinct(StringComparer.Ordinal).Count() != groups.Length)
            throw new InvalidOperationException(
                "The same wrestler/team lineup was added twice. Edit its total reign count instead.");

        var missing = new List<Wanted>();
        foreach (var request in requests)
        {
            var wanted = Expand(request, format);
            var recorded = slots.Where(slot => slot.LinkedWorkers.Length > 0 &&
                    SameMembers(slot, request))
                .OrderBy(s => s.Captured).ToList();
            if (recorded.Count > wanted.Count)
                throw new InvalidOperationException(
                    $"{request.Name} already has {recorded.Count} linked reigns, " +
                    $"more than the requested total of {wanted.Count}. " +
                    "Existing linked champions will not be removed.");

            // Consume fixed-year + duration matches first, then unspecified
            // years. This ensures already-recorded championships are included
            // in the requested TOTAL rather than duplicated.
            var unclaimed = new List<ExistingTitleSlot>(recorded);
            var pending = new List<Wanted>();
            foreach (var rule in wanted.Where(w => w.Year.HasValue))
            {
                int index = unclaimed.FindIndex(slot =>
                    slot.Captured.Year == rule.Year && LengthMatches(slot, rule));
                if (index < 0) pending.Add(rule);
                else unclaimed.RemoveAt(index);
            }
            foreach (var rule in wanted.Where(w => !w.Year.HasValue))
            {
                int index = unclaimed.FindIndex(slot => LengthMatches(slot, rule));
                if (index < 0) pending.Add(rule);
                else unclaimed.RemoveAt(index);
            }
            if (unclaimed.Count != 0)
                throw new InvalidOperationException(
                    $"{request.Name} already holds one or more historical reigns " +
                    "that conflict with the supplied winning-year or length requirements. " +
                    "Existing linked reigns cannot be changed automatically.");
            missing.AddRange(pending);
        }
        if (missing.Count == 0)
            throw new InvalidOperationException(
                "All requested reigns are already linked in this title history. " +
                "There are no missing placeholder reigns to replace.");

        var placeholders = slots.Where(s => s.IsNameOnly).OrderBy(s => s.Captured).ToArray();
        if (missing.Count > placeholders.Length)
            throw new InvalidOperationException(
                $"Need {missing.Count} unused reign(s), but only {placeholders.Length} " +
                "name-only placeholder slots are available. Existing real wrestlers are protected.");

        var choices = missing.Select(w => placeholders
                .Select((slot, index) => (slot, index))
                .Where(pair => pair.slot.Captured.Year >= w.EarliestYear &&
                    pair.slot.Captured.Year <= w.LatestYear &&
                    (!w.Year.HasValue || pair.slot.Captured.Year == w.Year.Value) &&
                    LengthMatches(pair.slot, w))
                // If the existing history already names a worker but lacks
                // their UID, prefer linking THAT historical reign before
                // displacing an unrelated name-only champion.
                .OrderByDescending(pair => new[] {
                    pair.slot.Name1, pair.slot.Name2, pair.slot.Name3
                }.Where(name => !string.IsNullOrWhiteSpace(name))
                    .Count(name => w.Owner.Names.Contains(
                        name, StringComparer.OrdinalIgnoreCase)))
                .ThenByDescending(pair => employmentScore?.Invoke(w.Owner, pair.slot) ?? 0)
                .ThenBy(pair => pair.slot.Captured)
                .Select(pair => pair.index).ToArray())
            .ToArray();
        for (int i = 0; i < missing.Count; i++)
            if (choices[i].Length == 0)
                throw new InvalidOperationException(
                    $"No unused title-history reign matches {missing[i].Owner.Name}'s " +
                    (missing[i].Year.HasValue ? $"{missing[i].Year} win" : "active years") +
                    " and optional reign length. Existing title dates will not be changed.");

        var slotOwner = Enumerable.Repeat(-1, placeholders.Length).ToArray();
        bool Assign(int wantedIndex, HashSet<int> visited)
        {
            foreach (int candidate in choices[wantedIndex])
            {
                if (!visited.Add(candidate)) continue;
                if (slotOwner[candidate] < 0 || Assign(slotOwner[candidate], visited))
                {
                    slotOwner[candidate] = wantedIndex;
                    return true;
                }
            }
            return false;
        }
        foreach (int task in Enumerable.Range(0, missing.Count)
                     .OrderBy(i => choices[i].Length).ThenBy(i => missing[i].ReignIndex))
            if (!Assign(task, new HashSet<int>()))
                throw new InvalidOperationException(
                    "The existing name-only reigns cannot accommodate every " +
                    "requested worker without overwriting another linked champion. " +
                    "Adjust winning years, active periods, or requested lengths.");

        var proposed = new List<TitleSlotPatch>();
        for (int i = 0; i < placeholders.Length; i++)
        {
            int task = slotOwner[i];
            if (task < 0) continue;
            var slot = placeholders[i];
            var request = missing[task].Owner;
            var ids = request.WorkerIds.ToArray();
            var names = request.Names.ToArray();
            proposed.Add(new TitleSlotPatch(slot.Uid, slot.Order,
                slot.Captured, slot.Lost, slot.Defences,
                ids[0], names[0],
                ids.Length > 1 ? ids[1] : -1, ids.Length > 1 ? names[1] : "",
                ids.Length > 2 ? ids[2] : -1, ids.Length > 2 ? names[2] : ""));
        }
        return proposed.OrderBy(p => p.Order).ToArray();
    }
}
