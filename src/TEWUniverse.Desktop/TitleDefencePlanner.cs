namespace TEWUniverse.Desktop;

/// <summary>
/// Historical title defenses are estimated from time in possession and the
/// expected booking cadence of THAT championship, not a flat random bracket.
/// Defenses are independent of title changes and do not include winning or
/// losing the championship. These are adjustable estimates, not actual bouts.
/// </summary>
internal enum TitleDefenceFrequency
{
    Automatic = 0,
    Touring = 1,
    Television = 2,
    Regular = 3,
    Occasional = 4,
    SpecialAttraction = 5
}

internal static class TitleDefencePlanner
{
    internal static TitleDefenceFrequency Resolve(string titleName,
        TitleDefenceFrequency requested)
    {
        if (requested != TitleDefenceFrequency.Automatic) return requested;
        string name = titleName ?? "";
        if (System.Text.RegularExpressions.Regex.IsMatch(name,
            @"(?<!\w)(television|tv)(?!\w)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase |
            System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            return TitleDefenceFrequency.Television;
        if (name.Contains("World", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Heavyweight", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Universal", StringComparison.OrdinalIgnoreCase))
            return TitleDefenceFrequency.Touring;
        return TitleDefenceFrequency.Regular;
    }

    internal static string Description(TitleDefenceFrequency frequency) =>
        frequency switch
        {
            TitleDefenceFrequency.Touring => "Touring: defended on most events (roughly 3–6 days apart)",
            TitleDefenceFrequency.Television => "Television: very frequent defenses (roughly 2–5 days apart)",
            TitleDefenceFrequency.Regular => "Regular: typically weekly or biweekly (roughly 7–15 days apart)",
            TitleDefenceFrequency.Occasional => "Occasional: roughly every 2–5 weeks",
            TitleDefenceFrequency.SpecialAttraction => "Special attraction: roughly every 1–2 months",
            _ => "Automatic: TV titles use Television, World titles use Touring, other titles use Regular"
        };

    internal static short Estimate(int heldDays, string titleName,
        TitleDefenceFrequency requested = TitleDefenceFrequency.Automatic,
        Random? random = null)
    {
        if (heldDays < 0)
            throw new ArgumentOutOfRangeException(nameof(heldDays));
        if (heldDays <= 1) return 0;
        var rng = random ?? Random.Shared;
        var frequency = Resolve(titleName, requested);
        (double min, double max) = frequency switch
        {
            TitleDefenceFrequency.Touring => (2.5, 5.5),
            TitleDefenceFrequency.Television => (2.5, 5.0),
            TitleDefenceFrequency.Regular => (7.0, 15.0),
            TitleDefenceFrequency.Occasional => (14.0, 35.0),
            TitleDefenceFrequency.SpecialAttraction => (28.0, 65.0),
            _ => throw new InvalidOperationException("Unknown title-defense frequency.")
        };

        // Each champion has a slightly different schedule, rather than
        // exactly one defense every 7/14/etc days for every reign.
        double interval = (min + rng.NextDouble() * (max - min)) *
            (0.92 + rng.NextDouble() * 0.16);
        int estimated = (int)Math.Round((heldDays - 1) / interval,
            MidpointRounding.AwayFromZero);

        // One defense at most per day, excluding the capture date. Never
        // overflow TEW IX's signed SmallInt defense count.
        return (short)Math.Clamp(estimated, 0,
            Math.Min(heldDays - 1, short.MaxValue));
    }
}
