using System.Data;
using System.Globalization;

namespace TEWUniverse.Studio;

/// <summary>
/// Field-level write rules for the TEW IX save record editor. A field is only
/// writable when the Access row has an identifiable key. Structural references
/// and unsupported data types are never exposed as editable cells.
/// </summary>
internal static class SaveEditRules
{
    private static readonly HashSet<string> ReferenceNames = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "UID", "ID", "Recordname", "RecordName", "Worker", "Worker1",
        "Worker2", "Worker3", "Fed", "OriginalFed", "Company", "Belt",
        "Title", "OwnerUID", "ContractUID", "StorylineUID", "PlayerUID",
        "GameUID", "FromUID", "ToUID", "TargetUID"
    };

    private static readonly HashSet<string> PercentageFields = new(
        StringComparer.OrdinalIgnoreCase)
    {
        // TEW numeric ratings are 0 to 100, not arbitrary integers.
        "Brawling", "Puroresu", "Hardcore", "Technical", "Aerial",
        "Flashiness", "Psychology", "Experience", "Respect", "Reputation",
        "Charisma", "Microphone", "Acting", "StarQuality", "SexAppeal",
        "Menace", "Basics", "Selling", "Consistency", "Safety", "Stamina",
        "Athleticism", "Power", "Toughness", "Resilience",
        "PlayByPlay", "Colour", "Refereeing", "Scouting", "Potential"
    };

    internal static bool IsProtectedName(string name)
        => ReferenceNames.Contains(name)
            || name.EndsWith("UID", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("_ID", StringComparison.OrdinalIgnoreCase);

    internal static bool IsSupportedScalarType(Type type)
        => Type.GetTypeCode(type) is
            TypeCode.Boolean or TypeCode.Byte or TypeCode.SByte or
            TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32 or
            TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64 or
            TypeCode.Single or TypeCode.Double or TypeCode.Decimal or
            TypeCode.DateTime or TypeCode.String;

    internal static bool CanEdit(string tableName, DataColumn column,
        IReadOnlyCollection<string> recordKeys)
        => tableName.StartsWith("tbl", StringComparison.OrdinalIgnoreCase)
            && recordKeys.Count > 0
            && !recordKeys.Contains(column.ColumnName, StringComparer.OrdinalIgnoreCase)
            && !IsProtectedName(column.ColumnName)
            && IsSupportedScalarType(column.DataType)
            && !column.ReadOnly
            && !column.AutoIncrement
            && string.IsNullOrEmpty(column.Expression);

    internal static bool IsValidValue(string tableName, DataColumn column, object value)
    {
        if (value == null || value is DBNull)
            return column.AllowDBNull &&
                !(tableName.Equals("tblWorkerSkill", StringComparison.OrdinalIgnoreCase) &&
                  PercentageFields.Contains(column.ColumnName));

        if (!IsSupportedScalarType(column.DataType) || !column.DataType.IsInstanceOfType(value))
            return false;

        if (value is string text && column.MaxLength >= 0 &&
            text.Length > column.MaxLength)
            return false;

        if (tableName.Equals("tblWorkerSkill", StringComparison.OrdinalIgnoreCase) &&
            PercentageFields.Contains(column.ColumnName))
        {
            try
            {
                var rating = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                return rating >= 0 && rating <= 100;
            }
            catch (Exception ex) when (ex is InvalidCastException or
                FormatException or OverflowException)
            {
                return false;
            }
        }

        return true;
    }
}
