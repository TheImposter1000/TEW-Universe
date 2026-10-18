using System.Globalization;

namespace TEWUniverse.Studio;

/// <summary>
/// Read-only, schema-grounded worker metadata used by the roster browser.
/// A worker can have more than one role and more than one current employer.
/// Raw categorical MDB codes remain raw codes; no unverified mapping is applied.
/// </summary>
internal sealed record WorkerFilterRecord(
    int Id, string Name, bool? IsActive, DateTime? Birthday,
    string Gender, string Nationality, string Region,
    IReadOnlyCollection<string> Roles, IReadOnlyCollection<int> CompanyUids,
    bool HasPicture = false, bool HasCurrentContract = false);

internal sealed record WorkerFilterCriteria(
    string Search = "", bool? Active = null, string? Gender = null,
    int? MinAge = null, int? MaxAge = null, int? CompanyUid = null,
    bool UnemployedOnly = false, string? Role = null,
    string? Nationality = null, string? Region = null, bool? HasPicture = null);

internal static class WorkerRosterFilterEngine
{
    internal static int AgeAt(DateTime birth, DateTime reference)
    {
        int age = reference.Year - birth.Year;
        if (birth.Date > reference.Date.AddYears(-age)) age--;
        return age;
    }

    internal static bool Matches(WorkerFilterRecord worker,
        WorkerFilterCriteria criteria, DateTime today)
    {
        string q = criteria.Search.Trim();
        if (q.Length > 0 &&
            !worker.Name.Contains(q, StringComparison.OrdinalIgnoreCase) &&
            !worker.Id.ToString(CultureInfo.InvariantCulture)
                .Contains(q, StringComparison.OrdinalIgnoreCase)) return false;

        if (criteria.Active.HasValue && worker.IsActive != criteria.Active)
            return false;
        if (criteria.Gender is { Length: > 0 } gender &&
            !string.Equals(worker.Gender, gender, StringComparison.OrdinalIgnoreCase))
            return false;
        if (criteria.Nationality is { Length: > 0 } nationality &&
            !string.Equals(worker.Nationality, nationality, StringComparison.OrdinalIgnoreCase))
            return false;
        if (criteria.Region is { Length: > 0 } region &&
            !string.Equals(worker.Region, region, StringComparison.OrdinalIgnoreCase))
            return false;
        if (criteria.Role is { Length: > 0 } role &&
            !worker.Roles.Contains(role, StringComparer.OrdinalIgnoreCase))
            return false;
        if (criteria.UnemployedOnly &&
            (worker.HasCurrentContract || worker.CompanyUids.Count != 0))
            return false;
        if (criteria.CompanyUid is int company &&
            !worker.CompanyUids.Contains(company))
            return false;
        if (criteria.MinAge.HasValue || criteria.MaxAge.HasValue)
        {
            if (!worker.Birthday.HasValue || worker.Birthday.Value.Year < 1800 ||
                worker.Birthday.Value.Date > today.Date)
                return false;
            int age = AgeAt(worker.Birthday.Value, today);
            if (criteria.MinAge.HasValue && age < criteria.MinAge.Value ||
                criteria.MaxAge.HasValue && age > criteria.MaxAge.Value)
                return false;
        }
        if (criteria.HasPicture.HasValue && worker.HasPicture != criteria.HasPicture.Value)
            return false;
        return true;
    }
}
