using System.Globalization;

namespace TEWUniverse.Studio;

/// <summary>
/// Preset chemistry is unordered by worker pair, but a pair can carry
/// distinct RelType values. TEW's code meanings must not be guessed.
/// </summary>
internal static class ChemistryRules
{
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
        if (!observedTypes.Contains(type) || !observedQualities.Contains(quality))
            throw new InvalidOperationException(
                "Choose a type and quality code already present in tblChemistryForce. " +
                "Unverified TEW IX numeric meanings cannot be invented.");
    }

    internal static string RecordName(string first, string second, int relationType,
        IEnumerable<string> existing, int maxLength)
    {
        string left = (first ?? "").Trim(), right = (second ?? "").Trim();
        if (left.Length == 0 || right.Length == 0 ||
            left.Any(char.IsControl) || right.Any(char.IsControl))
            throw new InvalidOperationException("Both workers need valid names.");
        string suffix = " Chemistry " + relationType.ToString(CultureInfo.InvariantCulture);
        string stem = left + " / " + right;
        var reserved = new HashSet<string>(existing,
            StringComparer.OrdinalIgnoreCase);
        for (int count = 1; count < 1000000; count++)
        {
            string extra = count == 1 ? "" : " #" + count.ToString(CultureInfo.InvariantCulture);
            int allowance = maxLength > 0 ? maxLength - suffix.Length - extra.Length : stem.Length;
            if (allowance < 1)
                throw new InvalidOperationException("The chemistry RecordName field is too short.");
            string candidate = (stem.Length > allowance ? stem[..allowance].TrimEnd() : stem) +
                suffix + extra;
            if (!reserved.Contains(candidate)) return candidate;
        }
        throw new InvalidOperationException("Could not allocate a unique chemistry RecordName.");
    }
}
