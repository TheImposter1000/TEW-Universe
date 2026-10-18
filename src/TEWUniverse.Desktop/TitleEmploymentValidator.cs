namespace TEWUniverse.Desktop;

internal enum EmploymentVerification { Covered, Missing, Unverified, CompanyUnknown }

internal sealed record EmploymentWindow(int CompanyUid, DateTime From, DateTime Until);

/// <summary>
/// Compares the full inclusive championship tenure against a worker's
/// historical CV periods. Disjoint but adjacent periods may cover a title
/// reign. 1666/unknown dates do not prove coverage and must be flagged.
/// </summary>
internal static class TitleEmploymentValidator
{
    internal static EmploymentVerification Check(
        int? companyUid, DateTime captured, DateTime lost,
        IReadOnlyList<EmploymentWindow> periods)
    {
        if (!companyUid.HasValue || companyUid.Value <= 0)
            return EmploymentVerification.CompanyUnknown;
        if (lost < captured || lost.Year < 1800)
            lost = captured; // Annual Cup edition: only the winning day matters.

        var matching = periods.Where(p => p.CompanyUid == companyUid.Value).ToArray();
        if (matching.Length == 0) return EmploymentVerification.Missing;
        var valid = matching.Where(p => p.From.Year >= 1800 &&
            p.Until.Year >= 1800 && p.Until >= p.From)
            .OrderBy(p => p.From).ToArray();
        // Merge contiguous historical employment periods; no artificial gaps.
        var coveredThrough = captured.AddDays(-1);
        foreach (var period in valid)
        {
            if (period.From.Date > coveredThrough.AddDays(1))
                continue;
            if (period.Until.Date > coveredThrough)
                coveredThrough = period.Until.Date;
            if (coveredThrough >= lost.Date)
                return EmploymentVerification.Covered;
        }
        return matching.Any(p => p.From.Year < 1800 || p.Until.Year < 1800 ||
            p.Until < p.From) ? EmploymentVerification.Unverified :
            EmploymentVerification.Missing;
    }

    internal static string Label(EmploymentVerification result) => result switch
    {
        EmploymentVerification.Covered => "Covered by employment history",
        EmploymentVerification.Missing => "FLAG: No matching employment period",
        EmploymentVerification.Unverified => "FLAG: CV dates incomplete / unverified",
        _ => "FLAG: Title company is not identified"
    };
}
