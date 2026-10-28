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

    // These are the actual winner columns found in both TEW IX year-end
    // award tables. The numbered tag fields are TWO worker recipients of
    // ONE tag team award, not arbitrary category codes.
    private static readonly HashSet<string> WorkerWinnerColumns =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "MaleOverall", "FemaleOverall",
            "MaleYoung", "FemaleYoung",
            "MaleVeteran", "FemaleVeteran",
            "MaleTag1", "MaleTag2", "FemaleTag1", "FemaleTag2",
            "MaleManager", "FemaleManager",
            "MaleAnnouncer", "FemaleAnnouncer",
            "MaleColour", "FemaleColour",
            "MaleReferee", "FemaleReferee",
            "MaleIndy", "FemaleIndy", "Improved"
        };

    private static readonly Dictionary<string, string> WinnerLabels =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["MaleOverall"] = "Men's Wrestler of the Year",
            ["FemaleOverall"] = "Women's Wrestler of the Year",
            ["MaleYoung"] = "Young Men's Wrestler of the Year",
            ["FemaleYoung"] = "Young Women's Wrestler of the Year",
            ["MaleVeteran"] = "Veteran Men's Wrestler of the Year",
            ["FemaleVeteran"] = "Veteran Women's Wrestler of the Year",
            ["MaleTag1"] = "Men's Tag Team of the Year · Partner 1",
            ["MaleTag2"] = "Men's Tag Team of the Year · Partner 2",
            ["FemaleTag1"] = "Women's Tag Team of the Year · Partner 1",
            ["FemaleTag2"] = "Women's Tag Team of the Year · Partner 2",
            ["MaleManager"] = "Men's Manager of the Year",
            ["FemaleManager"] = "Women's Manager of the Year",
            ["MaleAnnouncer"] = "Men's Announcer of the Year",
            ["FemaleAnnouncer"] = "Women's Announcer of the Year",
            ["MaleColour"] = "Men's Colour Commentator of the Year",
            ["FemaleColour"] = "Women's Colour Commentator of the Year",
            ["MaleReferee"] = "Men's Referee of the Year",
            ["FemaleReferee"] = "Women's Referee of the Year",
            ["MaleIndy"] = "Men's Independent Wrestler of the Year",
            ["FemaleIndy"] = "Women's Independent Wrestler of the Year",
            ["Improved"] = "Most Improved Wrestler"
        };

    internal static string FriendlyName(string field) =>
        WinnerLabels.TryGetValue(field, out var label) ? label :
        field switch
        {
            "Fed" => "Company of the Year",
            "FedUID" => "Promotion",
            "Match" => "Match of the Year",
            "Match_At" => "Match of the Year · Event",
            "Match_Rating" => "Match of the Year · Rating",
            "Card" => "Show of the Year",
            "Card_Rating" => "Show of the Year · Rating",
            "Year" => "Award Year",
            _ => field
        };

    internal static string CareerName(string field) =>
        field is "MaleTag1" or "MaleTag2" ? "Men's Tag Team of the Year" :
        field is "FemaleTag1" or "FemaleTag2" ? "Women's Tag Team of the Year" :
        FriendlyName(field);

    internal static bool IsAwardTable(string table) =>
        table.Equals(GlobalTable, StringComparison.OrdinalIgnoreCase) ||
        table.Equals(CompanyTable, StringComparison.OrdinalIgnoreCase);

    internal static bool IsYearField(string name, Type type) =>
        name.Equals("Year", StringComparison.OrdinalIgnoreCase) &&
        (type == typeof(short) || type == typeof(int) || type == typeof(long));

    // A company award row contains the same named winner columns as
    // the world table. Older/editor-specific variants are permitted only
    // where the column explicitly identifies a worker reference.
    private static readonly HashSet<string> CompanyRecipientColumns =
        new(StringComparer.OrdinalIgnoreCase)
        { "Worker", "WorkerUID", "Winner", "WinnerUID", "Recipient", "RecipientUID" };

    internal static bool IsWorkerField(string table, string name, Type type) =>
        (type == typeof(int) || type == typeof(short) || type == typeof(long)) &&
        ((table.Equals(GlobalTable, StringComparison.OrdinalIgnoreCase) &&
          WorkerWinnerColumns.Contains(name)) ||
         (table.Equals(CompanyTable, StringComparison.OrdinalIgnoreCase) &&
          (WorkerWinnerColumns.Contains(name) && name != "Improved" ||
           CompanyRecipientColumns.Contains(name))));

    internal static bool IsCompanyField(string table, string name, Type type) =>
        ((table.Equals(GlobalTable, StringComparison.OrdinalIgnoreCase) &&
          name.Equals("Fed", StringComparison.OrdinalIgnoreCase)) ||
         (table.Equals(CompanyTable, StringComparison.OrdinalIgnoreCase) &&
          (name.Equals("Fed", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("FedUID", StringComparison.OrdinalIgnoreCase)))) &&
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
