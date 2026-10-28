using System.Data;

namespace TEWUniverse.Studio;

/// <summary>
/// Company fields are discovered from the selected MDB, rather than copied
/// from a guessed TEW version. IDs and unresolved links are always displayed
/// but never writable. Ordinary scalar company settings can be edited.
/// </summary>
internal static class CompanyFieldRules
{
    internal static readonly (string Table, string Label)[] RelatedTables = {
        ("tblFedOver", "Regional popularity"),
        ("tblFedStyle", "Product and style"),
        ("tblFedHiring", "Hiring rules"),
        ("tblFedBackstage", "Backstage and policies"),
        ("tblFedBio", "Biography and company text"),
        ("tblFedPower", "Power structure"),
        ("tblFedTour", "Touring"),
        ("tblFedSchedule", "Show schedules"),
        ("tblFedBrand", "Brands"),
        ("tblFedDept", "Departments and divisions")
    };

    internal static bool IsRelatedTable(string table) =>
        RelatedTables.Any(x => x.Table.Equals(table, StringComparison.OrdinalIgnoreCase));

    internal static bool IsProtected(string column)
    {
        string name = column.Trim();
        if (name.Equals("UID", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Recordname", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("FedUID", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("CompanyUID", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Fed", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("UID", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("_ID", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("ID", StringComparison.OrdinalIgnoreCase))
            return true;
        // These columns can represent foreign records, not simple numerical
        // preferences. Editing requires a future verified picker and FK checks.
        return new[] {
            "Owner", "Booker", "HeadBooker", "Figurehead", "FigureheadHeir",
            "HomeArena", "HomeVenue", "PropertyOf", "Broadcaster",
            "MediaGroup", "Alliance", "PreAlliance", "ParentCompany",
            "ChildCompany", "CurrentProduct", "CoreProduct", "Product",
            "PreProduct", "HomeRegion", "BasedIn", "BaseRegion",
            "HomeArea", "Region", "Nation", "Country", "BookingTeam"
        }.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    internal static bool IsScalar(Type type) =>
        type == typeof(string) || type == typeof(bool) ||
        type == typeof(short) || type == typeof(int) ||
        type == typeof(long) || type == typeof(byte) ||
        type == typeof(decimal) || type == typeof(double) ||
        type == typeof(float) || type == typeof(DateTime);

    internal static bool CanEdit(DataColumn column) =>
        !IsProtected(column.ColumnName) &&
        IsScalar(column.DataType) &&
        !column.ReadOnly && !column.AutoIncrement &&
        string.IsNullOrEmpty(column.Expression);

    internal static string Group(string column, string table = "tblFed")
    {
        if (!table.Equals("tblFed", StringComparison.OrdinalIgnoreCase))
            return RelatedTables.FirstOrDefault(x =>
                x.Table.Equals(table, StringComparison.OrdinalIgnoreCase)).Label
                ?? "Related company information";

        string name = column.ToLowerInvariant();
        if (name is "uid" or "recordname" or "name" or "initials" or "shortname" or
            "url" or "website" or "logo" or "backdrop" or "banner" or
            "picture" or "profile" or "description" or "commentaryname")
            return "Identity and presentation";
        if (name.Contains("open") || name.Contains("clos") ||
            name.Contains("active") || name.Contains("found") ||
            name.Contains("start") || name.Contains("dormant") ||
            name.Contains("debut"))
            return "Operating history and dates";
        if (name.Contains("money") || name.Contains("financ") ||
            name.Contains("prestige") || name.Contains("momentum") ||
            name.Contains("influence") || name.Contains("merch") ||
            name.Contains("ticket") || name.Contains("revenue") ||
            name.Contains("expense") || name.Contains("econom"))
            return "Business and finances";
        if (name.Contains("product") || name.Contains("style") ||
            name.Contains("match") || name.Contains("angle") ||
            name.Contains("gender") || name.Contains("show") ||
            name.Contains("tour") || name.Contains("tv") ||
            name.Contains("event") || name.Contains("broadcast") ||
            name.Contains("schedule"))
            return "Product and presentation settings";
        if (name.Contains("hire") || name.Contains("hiring") ||
            name.Contains("firing") || name.Contains("drug") ||
            name.Contains("roster") || name.Contains("loyal") ||
            name.Contains("young") || name.Contains("belts") ||
            name.Contains("booker") || name.Contains("owner") ||
            name.Contains("brand") || name.Contains("figurehead") ||
            name.Contains("policy"))
            return "Personnel and policies";
        if (name.Contains("region") || name.Contains("nation") ||
            name.Contains("country") || name.Contains("area") ||
            name.Contains("based") || name.Contains("home"))
            return "Location and reach";
        return "Other TEW IX company settings";
    }
}
