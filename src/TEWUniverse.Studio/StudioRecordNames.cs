using System.Globalization;

namespace TEWUniverse.Studio;

/// <summary>
/// Human-readable TEW scenario record identifiers. These are names stored in
/// the MDB (not worker/title UIDs). They must be unique within their table.
/// </summary>
internal static class StudioRecordNames
{
    internal static string TitleHistory(string titleName, int sequence)
    {
        string title = (titleName ?? "").Trim();
        if (title.Length == 0 || title.Any(char.IsControl) || sequence < 1)
            throw new InvalidOperationException("Choose a valid championship name and sequence.");
        // BCW Championship001, BCW Championship002, ...
        return title + sequence.ToString("D3", CultureInfo.InvariantCulture);
    }

    internal static string Employment(string initials, DateTime from, DateTime until)
    {
        string company = (initials ?? "").Trim();
        if (company.Length == 0 || company.Any(char.IsControl) ||
            from.Date > until.Date || from.Year < 1800 || until.Year > 2100)
            throw new InvalidOperationException(
                "Choose company initials and valid employment dates before generating a record name.");
        return $"{company.ToUpperInvariant()} ({from.Year:D4}-{until.Year:D4})";
    }

    /// <summary>
    /// Generate a unique, readable database record identifier. The trailing
    /// semantic part (such as " 2022") is always preserved even when the MDB
    /// imposes a shorter text limit. Numeric UIDs remain separate.
    /// </summary>
    private static string UniqueReadable(string heading, string trailing,
        IEnumerable<string> existingNames, int maxLength)
    {
        heading = (heading ?? "").Trim();
        if (heading.Length == 0 || heading.Any(char.IsControl) ||
            trailing.Any(char.IsControl))
            throw new InvalidOperationException("A readable record name needs a valid name.");
        var existing = new HashSet<string>(
            existingNames.Where(x => !string.IsNullOrWhiteSpace(x)),
            StringComparer.OrdinalIgnoreCase);
        for (int sequence = 1; sequence < 1000000; sequence++)
        {
            string collisionSuffix = sequence == 1 ? "" :
                " #" + sequence.ToString(CultureInfo.InvariantCulture);
            int allowance = maxLength > 0
                ? maxLength - trailing.Length - collisionSuffix.Length
                : heading.Length;
            if (allowance < 1)
                throw new InvalidOperationException(
                    "The MDB Recordname field is too short for a readable identifier.");
            string prefix = heading.Length <= allowance ? heading :
                heading[..allowance].TrimEnd();
            string candidate = prefix + trailing + collisionSuffix;
            if (existing.Add(candidate))
                return candidate;
        }
        throw new InvalidOperationException("Too many duplicate record names.");
    }

    internal static string Award(string companyInitials, int year,
        IEnumerable<string> existingNames, int maxLength)
    {
        if (year is < 1800 or > 2100)
            throw new InvalidOperationException("Choose a valid award year.");
        string initials = (companyInitials ?? "").Trim().ToUpperInvariant();
        if (initials.Length == 0 || initials.Any(char.IsControl))
            throw new InvalidOperationException(
                "Choose a company with valid initials before creating an award.");
        string name = initials + " " +
            year.ToString("D4", CultureInfo.InvariantCulture);
        var used = new HashSet<string>(
            existingNames.Where(n => !string.IsNullOrWhiteSpace(n)),
            StringComparer.OrdinalIgnoreCase);
        for (int number = 1; number < 1000000; number++)
        {
            string proposal = number == 1
                ? name : WithCollisionSuffix(name, number);
            if (maxLength > 0 && proposal.Length > maxLength)
                throw new InvalidOperationException(
                    "The award Recordname text limit cannot hold the company initials, year and unique suffix.");
            if (!used.Contains(proposal))
                return proposal;
        }
        throw new InvalidOperationException("Too many duplicate award names.");
    }

    internal static string WorldAward(int year,
        IEnumerable<string> existingNames, int maxLength)
    {
        if (year is < 1800 or > 2100)
            throw new InvalidOperationException("Choose a valid award year.");
        return UniqueReadable("World Awards",
            " " + year.ToString("D4", CultureInfo.InvariantCulture),
            existingNames, maxLength);
    }

    internal static string TagTeam(string teamName,
        IEnumerable<string> existingNames, int maxLength) =>
        UniqueReadable(teamName, "", existingNames, maxLength);

    internal static string Contract(string workerName, string companyName,
        IEnumerable<string> existingNames, int maxLength)
    {
        string worker = (workerName ?? "").Trim();
        string company = (companyName ?? "").Trim();
        if (worker.Length == 0 || company.Length == 0)
            throw new InvalidOperationException(
                "A contract record needs a real worker and company name.");
        return UniqueReadable(worker + " (" + company + ")",
            "", existingNames, maxLength);
    }

    internal static string WithCollisionSuffix(string stem, int suffix)
    {
        if (suffix < 2) throw new ArgumentOutOfRangeException(nameof(suffix));
        return stem + " #" + suffix.ToString(CultureInfo.InvariantCulture);
    }
}
