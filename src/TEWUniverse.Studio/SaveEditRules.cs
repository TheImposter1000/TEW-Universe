using System.Data;

namespace TEWUniverse.Studio;

/// <summary>
/// Explicit first-pass allowlist for edits to active save games.
/// A column is not writable merely because Access reports it as numeric.
/// </summary>
internal static class SaveEditRules
{
    internal const string SkillTable = "tblWorkerSkill";
    internal const string SkillIdentity = "WorkerUID";

    private static readonly HashSet<string> KnownSkillColumns = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "Brawling", "Puroresu", "Hardcore", "Technical", "Aerial",
        "Flashiness", "Psychology", "Experience", "Respect", "Reputation",
        "Charisma", "Microphone", "Acting", "StarQuality", "SexAppeal",
        "Menace", "Basics", "Selling", "Consistency", "Safety", "Stamina",
        "Athleticism", "Power", "Toughness", "Resilience",
        "PlayByPlay", "Colour", "Refereeing", "Scouting", "Potential"
    };

    internal static bool CanEdit(string tableName, DataColumn column)
        => tableName.Equals(SkillTable, StringComparison.OrdinalIgnoreCase)
           && KnownSkillColumns.Contains(column.ColumnName)
           && IsNumeric(column.DataType)
           && !column.ReadOnly
           && !column.AutoIncrement
           && string.IsNullOrEmpty(column.Expression);

    internal static string? IdentityColumn(string tableName)
        => tableName.Equals(SkillTable, StringComparison.OrdinalIgnoreCase)
            ? SkillIdentity : null;

    internal static bool IsValidValue(string table, string field, object value)
    {
        if (!table.Equals(SkillTable, StringComparison.OrdinalIgnoreCase)
            || !KnownSkillColumns.Contains(field)
            || value == null || value is DBNull)
            return false;
        try
        {
            var number = Convert.ToDecimal(value,
                System.Globalization.CultureInfo.InvariantCulture);
            return number >= 0 && number <= 100;
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException
            or OverflowException)
        {
            return false;
        }
    }

    private static bool IsNumeric(Type type)
    {
        return Type.GetTypeCode(type) is
            TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16 or
            TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64 or
            TypeCode.Single or TypeCode.Double or TypeCode.Decimal;
    }
}
