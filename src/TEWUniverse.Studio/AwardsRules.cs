namespace TEWUniverse.Studio;

/// <summary>
/// Only columns with verified interpretations are writable. A year-end award
/// record has ONE row for several winners, so arbitrary numeric columns must
/// not be treated as interchangeable worker references.
/// </summary>
internal static class AwardsRules
{
    internal const string GlobalTable = "tblEOYAward";
    internal const string CompanyTable = "tblEOYFedAward";

    private static readonly HashSet<string> WorkerWinnerColumns =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Worker", "Young", "Veteran", "Female", "Tag1", "Tag2",
            "Improved", "Manager", "Announcer", "Colour", "Referee",
            "Indy", "MaleOverall", "MaleVeteran"
        };

    internal static bool IsAwardTable(string table) =>
        table.Equals(GlobalTable, StringComparison.OrdinalIgnoreCase) ||
        table.Equals(CompanyTable, StringComparison.OrdinalIgnoreCase);

    internal static bool IsYearField(string name, Type type) =>
        name.Equals("Year", StringComparison.OrdinalIgnoreCase) &&
        (type == typeof(short) || type == typeof(int) || type == typeof(long));

    internal static bool IsWorkerField(string table, string name, Type type) =>
        table.Equals(GlobalTable, StringComparison.OrdinalIgnoreCase) &&
        WorkerWinnerColumns.Contains(name) &&
        (type == typeof(int) || type == typeof(short) || type == typeof(long));

    internal static bool IsCompanyField(string table, string name, Type type) =>
        table.Equals(GlobalTable, StringComparison.OrdinalIgnoreCase) &&
        name.Equals("Fed", StringComparison.OrdinalIgnoreCase) &&
        (type == typeof(int) || type == typeof(short) || type == typeof(long));

    internal static bool CanEdit(string table, string name, Type type) =>
        IsAwardTable(table) &&
        (IsYearField(name, type) ||
         IsWorkerField(table, name, type) ||
         IsCompanyField(table, name, type));

    internal static int ValidateYear(string raw)
    {
        if (!int.TryParse(raw, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out int year) ||
            year < 1800 || year > 2100)
            throw new InvalidOperationException("Award year must be an integer from 1800 to 2100.");
        return year;
    }

    internal static int ValidateLinkedId(string raw, string name)
    {
        if (!int.TryParse(raw, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out int id) || id <= 0)
            throw new InvalidOperationException(name +
                " needs a positive existing worker/company ID. Unverified blank sentinels are not written.");
        return id;
    }
}
