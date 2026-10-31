using System.Globalization;

namespace TEWUniverse.Studio;

/// <summary>
/// Verified TEW IX forced chemistry codes and readable RecordName conventions.
/// Values stored in tblChemistryForce remain the original numeric codes.
/// </summary>
internal static class ChemistryRules
{
    internal static readonly int[] KnownCodes = { 1, 2, 3, 4, 5 };

    internal static bool KnownType(int code) => code is >= 1 and <= 5;
    internal static bool KnownQuality(int code) => code is >= 1 and <= 5;

    internal static string TypeLabel(int code) => code switch
    {
        1 => "Fighting Against", 2 => "Teaming With",
        3 => "Managing", 4 => "Commentating With", 5 => "Booking",
        _ => "Code " + code.ToString(CultureInfo.InvariantCulture)
    };

    internal static string QualityLabel(int code) => code switch
    {
        1 => "Great", 2 => "Good", 3 => "Neutral",
        4 => "Poor", 5 => "Awful",
        _ => "Code " + code.ToString(CultureInfo.InvariantCulture)
    };

    internal static string TypeAbbreviation(int code) => code switch
    {
        1 => "FA", 2 => "TW", 3 => "MW", 4 => "CW", 5 => "BW",
        _ => throw new InvalidOperationException("Unknown chemistry type code: " + code)
    };

    // GT is the specified shorthand for Great. The other quality
    // abbreviations distinguish Good, Neutral, Poor and Awful.
    internal static string QualityAbbreviation(int code) => code switch
    {
        1 => "GT", 2 => "GD", 3 => "NT", 4 => "PR", 5 => "AW",
        _ => throw new InvalidOperationException("Unknown chemistry quality code: " + code)
    };

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
        // Known codes are explicitly confirmed; they need not have appeared
        // in the current scenario before the first preset is created.
        if (!KnownType(type) || !KnownQuality(quality))
            throw new InvalidOperationException(
                "Choose a confirmed chemistry type and quality (codes 1 through 5).");
    }

    internal static string CompactWorkerName(string name)
    {
        string full = (name ?? "").Trim();
        if (full.Length == 0 || full.Any(char.IsControl))
            throw new InvalidOperationException("Both workers must have valid names.");

        var tokens = full.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 1) return tokens[0];
        // Preserve compound family names and suffixes, e.g. J.Van Dam.
        char initial = char.ToUpperInvariant(tokens[0][0]);
        return initial + "." + string.Join(" ", tokens.Skip(1));
    }

    internal static string RecordName(string first, string second, int relationType,
        int quality, IEnumerable<string> existing, int maxLength)
    {
        string stem = TypeAbbreviation(relationType) + "-" +
            QualityAbbreviation(quality) + ": " +
            CompactWorkerName(first) + " / " + CompactWorkerName(second);
        var reserved = new HashSet<string>(existing,
            StringComparer.OrdinalIgnoreCase);

        for (int number = 1; number < 1000000; number++)
        {
            string candidate = number == 1 ? stem :
                stem + " #" + number.ToString(CultureInfo.InvariantCulture);
            if (maxLength > 0 && candidate.Length > maxLength)
                throw new InvalidOperationException(
                    "The chemistry RecordName exceeds the MDB text limit (" +
                    maxLength + " characters). Choose shorter worker names.");
            if (!reserved.Contains(candidate)) return candidate;
        }
        throw new InvalidOperationException("Could not allocate a unique chemistry RecordName.");
    }
}
