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

    internal static string WithCollisionSuffix(string stem, int suffix)
    {
        if (suffix < 2) throw new ArgumentOutOfRangeException(nameof(suffix));
        return stem + " #" + suffix.ToString(CultureInfo.InvariantCulture);
    }
}
