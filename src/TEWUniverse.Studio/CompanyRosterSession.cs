using System.Data;
using System.Data.OleDb;
using System.Globalization;

namespace TEWUniverse.Studio;

internal sealed record CompanyRosterMember(
    int WorkerUid, string WorkerName, bool? Active, int ContractCount);

internal sealed partial class DatabaseSession
{
    // Current roster is tblContract, NOT tblCV historical employment. Read-only
    // display; changing contracts belongs to the worker Contracts editor.
    public IReadOnlyList<CompanyRosterMember> ReadCompanyRoster(int companyUid)
    {
        if (companyUid <= 0 || !ReadSection("tblFed", companyUid).Exists)
            throw new InvalidOperationException("Select an existing company.");

        var schema = new DataTable();
        using (var cmd = new OleDbCommand("SELECT * FROM [tblContract] WHERE 1=0", _connection))
        using (var adapter = new OleDbDataAdapter(cmd))
            adapter.Fill(schema);
        if (!schema.Columns.Contains("WorkerUID") || !schema.Columns.Contains("FedUID") ||
            schema.Columns["WorkerUID"]!.DataType != typeof(int) ||
            schema.Columns["FedUID"]!.DataType != typeof(int))
            throw new InvalidOperationException(
                "The contract table lacks verified WorkerUID and FedUID links.");

        var counts = new Dictionary<int, int>();
        using (var cmd = new OleDbCommand(
            "SELECT [WorkerUID] FROM [tblContract] WHERE [FedUID]=?", _connection))
        {
            cmd.Parameters.Add("company", OleDbType.Integer).Value = companyUid;
            using var reader = cmd.ExecuteReader();
            while (reader?.Read() == true)
            {
                if (reader.IsDBNull(0)) continue;
                int worker = Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture);
                if (worker <= 0) continue;
                counts[worker] = counts.GetValueOrDefault(worker) + 1;
            }
        }

        var workers = ListWorkers().ToDictionary(w => w.Id);
        return counts.Select(pair =>
        {
            if (workers.TryGetValue(pair.Key, out var worker))
                return new CompanyRosterMember(worker.Id, worker.Name,
                    worker.IsActive, pair.Value);
            return new CompanyRosterMember(pair.Key,
                "Missing worker record (UID " + pair.Key + ")", false, pair.Value);
        }).OrderBy(x => x.WorkerName, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
