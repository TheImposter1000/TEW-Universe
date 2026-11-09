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

        // The detailed editor only uses verified numeric codes. Apply the
        // same catalog to direct Save Tables edits instead of accepting an
        // arbitrary integer for a known TEW category.
        var choices = TewCodeLabels.Choices(tableName, column.ColumnName);
        if (choices.Count > 0)
        {
            if (!TewCodeLabels.TryGetCode(Convert.ToString(value,
                    CultureInfo.InvariantCulture), out int code))
                return false;
            if (!choices.Any(c => c.Code == code &&
                    !c.Label.Equals("Unassigned", StringComparison.OrdinalIgnoreCase)))
                return false;
        }

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


/// <summary>
/// Show TEW's actual user tables and group related worker data first.
/// ACE also exposes MSys/USys and system metadata; those are not TEW
/// records and should never crowd the Save Studio table browser.
/// </summary>
internal static class SaveTableBrowserRules
{
    internal static bool Include(string? name, string? type)
        => !string.IsNullOrWhiteSpace(name)
            && !name.StartsWith("MSys", StringComparison.OrdinalIgnoreCase)
            && !name.StartsWith("USys", StringComparison.OrdinalIgnoreCase)
            && (string.Equals(type, "TABLE", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("tbl", StringComparison.OrdinalIgnoreCase));

    internal static string Category(string table)
    {
        if (table.StartsWith("tblWorker", StringComparison.OrdinalIgnoreCase))
            return "Workers";
        if (table.StartsWith("tblFed", StringComparison.OrdinalIgnoreCase) ||
            table.StartsWith("tblBelt", StringComparison.OrdinalIgnoreCase) ||
            table.StartsWith("tblContract", StringComparison.OrdinalIgnoreCase) ||
            table.StartsWith("tblStoryline", StringComparison.OrdinalIgnoreCase) ||
            table.StartsWith("tblTeam", StringComparison.OrdinalIgnoreCase))
            return "Current universe";
        if (table.Contains("History", StringComparison.OrdinalIgnoreCase) ||
            table.EndsWith("Log", StringComparison.OrdinalIgnoreCase) ||
            table.Contains("LogCompetitor", StringComparison.OrdinalIgnoreCase))
            return "History and results";
        if (table.StartsWith("tblGame", StringComparison.OrdinalIgnoreCase) ||
            table.StartsWith("tblPlayer", StringComparison.OrdinalIgnoreCase) ||
            table.StartsWith("tblUser", StringComparison.OrdinalIgnoreCase) ||
            table.StartsWith("tblPending", StringComparison.OrdinalIgnoreCase) ||
            table.StartsWith("tblAI", StringComparison.OrdinalIgnoreCase) ||
            table.Contains("Booking", StringComparison.OrdinalIgnoreCase))
            return "Game and booking state";
        return "Other save tables";
    }

    internal static int CategoryOrder(string category) => category switch
    {
        "Workers" => 0,
        "Current universe" => 1,
        "Game and booking state" => 2,
        "History and results" => 3,
        _ => 4
    };

    internal static int WorkerOrder(string table) =>
        table.Equals("tblWorker", StringComparison.OrdinalIgnoreCase) ? 0 :
        table.Equals("tblWorkerSkill", StringComparison.OrdinalIgnoreCase) ? 1 :
        table.Equals("tblWorkerOver", StringComparison.OrdinalIgnoreCase) ? 2 :
        table.Equals("tblWorkerPhysical", StringComparison.OrdinalIgnoreCase) ? 3 :
        table.Equals("tblWorkerBio", StringComparison.OrdinalIgnoreCase) ? 4 : 5;
}


/// <summary>
/// Build a DataView search expression only from the selected DataTable's
/// discovered columns. Filtering is presentation-only and cannot alter MDB
/// records or bypass edit validation.
/// </summary>
internal static class SaveTableRecordFilter
{
    internal const string AllColumns = "All fields";

    internal static string Build(DataTable table, string? column, string? search)
    {
        var query = search?.Trim() ?? "";
        if (query.Length == 0) return "";

        IEnumerable<DataColumn> columns;
        if (string.IsNullOrWhiteSpace(column) || column == AllColumns)
            columns = table.Columns.Cast<DataColumn>();
        else
        {
            if (!table.Columns.Contains(column))
                throw new ArgumentException("The selected record field is not in this table.",
                    nameof(column));
            columns = new[] { table.Columns[column]! };
        }

        // Escape LIKE metacharacters in a single pass so the escape
        // sequences we insert cannot be escaped a second time.
        var escaped = new System.Text.StringBuilder();
        foreach (char ch in query)
        {
            escaped.Append(ch switch
            {
                '\u0027' => "''",
                '[' => "[[]",
                ']' => "[]]",
                '%' => "[%]",
                '*' => "[*]",
                _ => ch.ToString()
            });
        }
        var text = escaped.ToString();

        return string.Join(" OR ", columns.Where(col =>
                SaveEditRules.IsSupportedScalarType(col.DataType))
            .Select(col =>
            {
                var quoted = "[" + col.ColumnName
                    .Replace("\\", "\\\\", StringComparison.Ordinal)
                    .Replace("]", "\\]", StringComparison.Ordinal) + "]";
                var expression = col.DataType == typeof(string) ? quoted :
                    "Convert(" + quoted + ", 'System.String')";
                return expression + " LIKE '%" + text + "%'";
            }));
    }
}
