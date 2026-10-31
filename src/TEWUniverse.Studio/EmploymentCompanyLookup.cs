namespace TEWUniverse.Studio;

/// <summary>
/// Company identities used by the employment history form. Initials come
/// from tblFed, not from a guessed abbreviation of the company name.
/// </summary>
internal sealed record EmploymentCompanyChoice(int Id, string Name, string Initials)
{
    public override string ToString() => Name;
}

internal static class EmploymentCompanyLookup
{
    /// <summary>
    /// Auto-select only when trimmed initials identify exactly one company.
    /// Null also covers no match and duplicate initials: never choose arbitrarily.
    /// </summary>
    internal static EmploymentCompanyChoice? FindUniqueByInitials(
        string? input, IEnumerable<EmploymentCompanyChoice> companies)
    {
        string initials = (input ?? "").Trim();
        if (initials.Length == 0) return null;
        EmploymentCompanyChoice? found = null;
        foreach (var company in companies)
        {
            if (!string.Equals(initials, company.Initials.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                continue;
            if (found != null) return null;
            found = company;
        }
        return found;
    }

    internal static int CountMatches(
        string? input, IEnumerable<EmploymentCompanyChoice> companies)
    {
        string initials = (input ?? "").Trim();
        return initials.Length == 0 ? 0 : companies.Count(c =>
            string.Equals(c.Initials.Trim(), initials,
                StringComparison.OrdinalIgnoreCase));
    }
}
