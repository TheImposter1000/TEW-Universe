using System.Data;
using System.Data.OleDb;
using System.Globalization;

namespace TEWUniverse.Studio;

internal sealed partial class DatabaseSession
{
    /// <summary>Discover existing values for an actual worker type code column.</summary>
    public List<int> ReadObservedWorkerTypeCodes(string field)
    {
        if (!new[] { "WorkerType", "Worker_Type", "Role", "Type" }
                .Contains(field, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unknown worker type field.");
        var schema = ValidateWorkerExtraSchema("tblWorker", "UID", field);
        Type type = schema.Columns[field]!.DataType;
        if (type != typeof(byte) && type != typeof(short) &&
            type != typeof(int) && type != typeof(long))
            return new List<int>();
        var codes = new HashSet<int>();
        using var cmd = new OleDbCommand(
            $"SELECT DISTINCT [{field}] FROM [tblWorker] WHERE [{field}] IS NOT NULL",
            _connection);
        using var reader = cmd.ExecuteReader();
        while (reader?.Read() == true)
        {
            try { codes.Add(Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture)); }
            catch (OverflowException) { /* Preserve out-of-range values in existing editor. */ }
        }
        return codes.OrderBy(x => x).ToList();
    }
}
