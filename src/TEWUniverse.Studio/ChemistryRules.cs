using System.Globalization;

namespace TEWUniverse.Studio;

/// <summary>
/// Maintainer-verified TEW IX chemistry code labels. Unknown legacy codes
/// remain readable and are not rewritten automatically.
/// </summary>
internal static class ChemistryRules
{
    internal static readonly IReadOnlyDictionary<int, (string Label, string Abbreviation)> Types =
        new Dictionary<int, (string, string)> {
            [1] = ("Fighting Against", "FA"),
            [2] = ("Teaming With", "TW"),
            [3] = ("Managing", "MG"),
            [4] = ("Commentating With", "CW"),
            [5] = ("Booking", "BK")
        };

    internal static readonly IReadOnlyDictionary<int, (string Label, string Abbreviation)> Qualities =
        new Dictionary<int, (string, string)> {
            [1] = ("Great", "GT"),
            [2] = ("Good", "GD"),
            [3] = ("Neutral", "NT"),
            [4] = ("Poor", "PR"),
            [5] = ("Awful", "AW")
        };

    internal static string TypeLabel(int code) =>
        Types.TryGetValue(code, out var value) ? value.Label :
        "Code " + code.ToString(CultureInfo.InvariantCulture);

    internal static string QualityLabel(int code) =>
        Qualities.TryGetValue(code, out var value) ? value.Label :
        "Code " + code.ToString(CultureInfo.InvariantCulture);

    internal static (int First, int Second) Pair(int a, int b) =>
        a <= b ? (a, b) : (b, a);

    internal static void ValidatePair(int first, int second, IReadOnlySet<int> workerIds)
    {
        if (first < 1 || second < 1 || first == second ||
            !workerIds.Contains(first) || !workerIds.Contains(second))
            throw new InvalidOperationException("Select two different existing workers.");
    }

    internal static bool SamePairAndType(int a, int b, int type,
        int otherA, int otherB, int otherType) =>
        Pair(a, b) == Pair(otherA, otherB) && type == otherType;

    internal static void ValidateObserved(int type, int quality,
        IReadOnlyCollection<int> observedTypes, IReadOnlyCollection<int> observedQualities)
    {
        if ((!Types.ContainsKey(type) && !observedTypes.Contains(type)) ||
            (!Qualities.ContainsKey(quality) && !observedQualities.Contains(quality)))
            throw new InvalidOperationException(
                "Select a verified chemistry type and quality. Unknown historical " +
                "codes may only be preserved from existing database records.");
    }

    private static string WorkerShortName(string full)
    {
        string[] words = (full ?? "").Trim().Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0 || words.Any(w => w.Any(char.IsControl)))
            throw new InvalidOperationException("Both workers need valid names.");
        return words.Length == 1 ? words[0] :
            char.ToUpperInvariant(words[0][0]) + "." + string.Join(" ", words.Skip(1));
    }

    internal static string RecordName(string first, string second,
        int relationType, int quality, IEnumerable<string> existing, int maxLength)
    {
        if (!Types.TryGetValue(relationType, out var type) ||
            !Qualities.TryGetValue(quality, out var grade))
            throw new InvalidOperationException(
                "Automatic Chemistry RecordName requires a verified type and quality.");
        string stem = type.Abbreviation + "-" + grade.Abbreviation + ": " +
            WorkerShortName(first) + " / " + WorkerShortName(second);
        var reserved = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        for (int sequence = 1; sequence < 1000000; sequence++)
        {
            string suffix = sequence == 1 ? "" :
                " #" + sequence.ToString(CultureInfo.InvariantCulture);
            string candidate = stem + suffix;
            if (maxLength > 0 && candidate.Length > maxLength)
                throw new InvalidOperationException(
                    "The chemistry RecordName text field cannot fit '" + candidate + "'.");
            if (!reserved.Contains(candidate)) return candidate;
        }
        throw new InvalidOperationException("Could not allocate a unique chemistry RecordName.");
    }
}
