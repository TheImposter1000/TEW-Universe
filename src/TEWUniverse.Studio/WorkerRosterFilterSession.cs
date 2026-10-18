using System.Data;
using System.Data.OleDb;
using System.Globalization;

namespace TEWUniverse.Studio;

internal sealed record WorkerFilterIndex(
    IReadOnlyList<WorkerFilterRecord> Workers,
    IReadOnlyList<ExistingRecordItem> Companies,
    bool HasGender, bool HasBirthday, bool HasNationality,
    bool HasRegion, bool HasWorkerRoles, bool HasContractCompanyLinks);

internal sealed partial class DatabaseSession
{
    private static readonly (string Column, string Label)[] KnownWorkerRoles =
    {
        ("Wrestler", "Wrestler"), ("OccasionalWrestler", "Occasional Wrestler"),
        ("Manager", "Manager"), ("Personality", "Personality"),
        ("Referee", "Referee"), ("RoadAgent", "Road Agent"),
        ("Announcer", "Announcer"), ("Colour", "Colour Commentator"),
        ("Color", "Colour Commentator"),
        ("ColourCommentator", "Colour Commentator"),
        ("ColorCommentator", "Colour Commentator"),
        ("Authority", "Authority Figure"),
        ("AuthorityFigure", "Authority Figure"),
        ("Interviewer", "Interviewer")
    };

    /// <summary>
    /// Batch-read the same scenario data seen by the Workers editor.
    /// Company uses current tblContract links, NOT historical tblCV stints.
    /// Unknown gender/region/nationality codes are shown as stored codes,
    /// rather than mapped to an unverified TEW IX enum.
    /// </summary>
    public WorkerFilterIndex ReadWorkerFilterIndex(IReadOnlyList<WorkerItem> workers)
    {
        var schema = new DataTable("WorkerFilterSchema");
        using (var command = new OleDbCommand(
            "SELECT * FROM [tblWorker] WHERE 1=0", _connection))
        using (var adapter = new OleDbDataAdapter(command))
            adapter.Fill(schema);

        bool hasGender = schema.Columns.Contains("Gender");
        bool hasBirthday = schema.Columns.Contains("Birthday");
        bool hasNationality = schema.Columns.Contains("Nationality");
        bool hasRegion = schema.Columns.Contains("Based_In");
        var roles = KnownWorkerRoles
            .Where(role => schema.Columns.Contains(role.Column) &&
                (schema.Columns[role.Column]!.DataType == typeof(bool) ||
                 WorkerFieldCatalog.IsNumeric(schema.Columns[role.Column]!.DataType)))
            .ToArray();
        string? typeColumn = new[] { "WorkerType", "Worker_Type", "Role" }
            .FirstOrDefault(col => schema.Columns.Contains(col));

        var fields = new List<string> { "UID" };
        foreach (var column in new[] { "Gender", "Birthday", "Nationality", "Based_In" })
            if (schema.Columns.Contains(column)) fields.Add(column);
        fields.AddRange(roles.Select(r => r.Column));
        if (typeColumn != null && !fields.Contains(typeColumn))
            fields.Add(typeColumn);

        var values = new Dictionary<int, (DateTime? Birthday,
            string Gender, string Nationality, string Region, string[] Roles)>();
        using (var command = new OleDbCommand(
            "SELECT " + string.Join(", ", fields.Select(c => $"[{c}]")) +
            " FROM [tblWorker]", _connection))
        using (var reader = command.ExecuteReader())
        {
            while (reader?.Read() == true)
            {
                int uid = Convert.ToInt32(reader["UID"], CultureInfo.InvariantCulture);
                string Code(string field)
                {
                    if (!fields.Contains(field) || reader[field] is DBNull) return "";
                    object item = reader[field];
                    string code = Convert.ToString(item, CultureInfo.InvariantCulture)?.Trim() ?? "";
                    return item is string ? code : code.Length == 0 ? "" : "Code " + code;
                }

                var roleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (col, name) in roles)
                {
                    if (reader[col] is DBNull) continue;
                    var raw = reader[col];
                    bool enabled = raw is bool b ? b :
                        WorkerFieldCatalog.IsNumeric(raw.GetType()) &&
                        Convert.ToDecimal(raw, CultureInfo.InvariantCulture) != 0;
                    if (enabled) roleNames.Add(name);
                }
                if (typeColumn != null)
                {
                    string value = Code(typeColumn);
                    if (value.Length > 0) roleNames.Add(value);
                }
                DateTime? birthday = hasBirthday && reader["Birthday"] is DateTime date
                    ? date : null;
                values[uid] = (birthday, Code("Gender"), Code("Nationality"),
                    Code("Based_In"), roleNames.OrderBy(r => r).ToArray());
            }
        }

        var companyLinks = new Dictionary<int, HashSet<int>>();
        bool companySupported = false;
        var contractSchema = new DataTable("ContractFilterSchema");
        try
        {
            using var command = new OleDbCommand(
                "SELECT * FROM [tblContract] WHERE 1=0", _connection);
            using var adapter = new OleDbDataAdapter(command);
            adapter.Fill(contractSchema);
            companySupported = contractSchema.Columns.Contains("WorkerUID") &&
                contractSchema.Columns.Contains("FedUID");
            if (companySupported)
            {
                using var contracts = new OleDbCommand(
                    "SELECT [WorkerUID], [FedUID] FROM [tblContract]", _connection);
                using var reader = contracts.ExecuteReader();
                while (reader?.Read() == true)
                {
                    if (reader.IsDBNull(0) || reader.IsDBNull(1)) continue;
                    int uid = Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture);
                    int company = Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture);
                    if (company <= 0) continue;
                    if (!companyLinks.TryGetValue(uid, out var set))
                        companyLinks.Add(uid, set = new HashSet<int>());
                    set.Add(company);
                }
            }
        }
        catch (OleDbException)
        {
            // An absent or unreadable tblContract cannot prove unemployment.
            // The company filter is disabled rather than guessing.
            companySupported = false;
            companyLinks.Clear();
        }

        var companyNames = companySupported
            ? ListExistingRecords("Companies") : new List<ExistingRecordItem>();
        var data = workers.Select(worker =>
        {
            values.TryGetValue(worker.Id, out var raw);
            companyLinks.TryGetValue(worker.Id, out var companies);
            return new WorkerFilterRecord(worker.Id, worker.Name, worker.IsActive,
                raw.Birthday, raw.Gender ?? "", raw.Nationality ?? "",
                raw.Region ?? "", raw.Roles ?? Array.Empty<string>(),
                (IReadOnlyCollection<int>?)companies ?? Array.Empty<int>());
        }).ToArray();

        return new WorkerFilterIndex(data, companyNames, hasGender, hasBirthday,
            hasNationality, hasRegion, roles.Length > 0 || typeColumn != null,
            companySupported);
    }
}
