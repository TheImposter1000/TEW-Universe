using System.Data;
using System.Data.OleDb;
using System.Globalization;
using System.Text;

namespace TEWUniverse.Studio;

internal sealed record CareerChampionship(string Company, string Title,
    IReadOnlyList<string> WonDates)
{
    internal int Reigns => WonDates.Count;
}

internal sealed record CareerAward(string Scope, string Category, string Year,
    string Details);

internal sealed record WorkerCareerRecord(
    IReadOnlyList<CareerChampionship> Championships,
    IReadOnlyList<CareerAward> Awards,
    IReadOnlyList<string> Notes);

/// <summary>
/// Read-only career details from actual TEW IX historical titles and awards.
/// A worker only receives awards when the table's explicit recipient column
/// points to that worker. An unrelated match/show reference never counts.
/// </summary>
internal sealed partial class DatabaseSession
{
    public WorkerCareerRecord ReadWorkerCareerDetails(int workerId)
    {
        CheckLinkedWorker(workerId);
        var titles = ListExistingRecords("Titles").ToDictionary(x => x.Id, x => x.Name);
        var companies = ListExistingRecords("Companies").ToDictionary(x => x.Id, x => x.Name);
        var workers = ListWorkers().ToDictionary(x => x.Id, x => x.Name);
        var beltCompanies = new Dictionary<int, string>();
        var reigns = new Dictionary<int, List<string>>();
        var awards = new List<CareerAward>();
        var notes = new List<string>();

        try
        {
            using var cmd = new OleDbCommand("SELECT [UID], [Fed] FROM [tblBelt]", _connection);
            using var reader = cmd.ExecuteReader();
            while (reader != null && reader.Read())
            {
                int id = Convert.ToInt32(reader["UID"], CultureInfo.InvariantCulture);
                int fed = Convert.ToInt32(reader["Fed"], CultureInfo.InvariantCulture);
                beltCompanies[id] = companies.TryGetValue(fed, out var company)
                    ? company : "Other / Unassigned Promotions";
            }
        }
        catch (OleDbException ex)
        {
            notes.Add("Title company information unavailable: " + ex.Message);
        }

        try
        {
            using var cmd = new OleDbCommand("SELECT * FROM [tblBeltPreHistory]", _connection);
            var data = new DataTable();
            using (var adapter = new OleDbDataAdapter(cmd)) adapter.Fill(data);
            foreach (DataRow row in data.Rows)
            {
                bool isHolder = new[] { "Holder1", "Holder2", "Holder3" }.Any(name =>
                    data.Columns.Contains(name) &&
                    int.TryParse(Display(row[name]), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out int id) && id == workerId);
                if (!isHolder) continue;
                int beltId = data.Columns.Contains("BeltUID") &&
                    int.TryParse(Display(row["BeltUID"]), out int found) ? found : -1;
                string captured = "";
                foreach (string field in new[] { "Captured", "BeltCaptured" })
                {
                    if (!data.Columns.Contains(field) || row[field] is DBNull) continue;
                    captured = Display(row[field]);
                    break;
                }
                if (!reigns.TryGetValue(beltId, out var dates))
                    reigns[beltId] = dates = new List<string>();
                dates.Add(captured);
            }
        }
        catch (OleDbException ex)
        {
            notes.Add("Championship history unavailable: " + ex.Message);
        }

        foreach (bool companySpecific in new[] { false, true })
        {
            string table = companySpecific ? AwardsRules.CompanyTable : AwardsRules.GlobalTable;
            try
            {
                using var cmd = new OleDbCommand("SELECT * FROM [" + table + "]", _connection);
                var data = new DataTable();
                using (var adapter = new OleDbDataAdapter(cmd)) adapter.Fill(data);
                foreach (DataRow row in data.Rows)
                {
                    string year = data.Columns.Contains("Year")
                        ? Display(row["Year"]) : "";
                    string scope = "Global Awards";
                    if (companySpecific)
                    {
                        string? col = data.Columns.Contains("FedUID")
                            ? "FedUID" : data.Columns.Contains("Fed") ? "Fed" : null;
                        if (col != null && int.TryParse(Display(row[col]), out int fed))
                            scope = companies.TryGetValue(fed, out var name)
                                ? name : "Unlinked Promotion " + fed;
                        else scope = "Unassigned Promotion";
                    }
                    foreach (DataColumn col in data.Columns)
                    {
                        if (!AwardsRules.IsWorkerField(table, col.ColumnName, col.DataType))
                            continue;
                        if (!int.TryParse(Display(row[col]), NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out int id) || id != workerId)
                            continue;
                        string details = "";
                        if (col.ColumnName is "MaleTag1" or "MaleTag2" or
                            "FemaleTag1" or "FemaleTag2")
                        {
                            string partner = col.ColumnName.EndsWith("1",
                                StringComparison.Ordinal) ?
                                col.ColumnName[..^1] + "2" : col.ColumnName[..^1] + "1";
                            if (data.Columns.Contains(partner) &&
                                int.TryParse(Display(row[partner]), out int partnerId) &&
                                partnerId != workerId &&
                                workers.TryGetValue(partnerId, out var partnerName))
                                details = "with " + partnerName;
                        }
                        awards.Add(new CareerAward(scope,
                            AwardsRules.CareerName(col.ColumnName), year, details));
                    }
                }
            }
            catch (OleDbException ex)
            {
                notes.Add((companySpecific ? "Company" : "Global") +
                    " awards unavailable: " + ex.Message);
            }
        }

        var championships = reigns.Select(pair => new CareerChampionship(
            beltCompanies.TryGetValue(pair.Key, out var company)
                ? company : "Other / Unassigned Promotions",
            titles.TryGetValue(pair.Key, out var title)
                ? title : "Title UID " + pair.Key,
            pair.Value))
            .OrderBy(x => x.Company, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase).ToArray();

        // A tag award is stored in two recipient columns. Suppress only
        // duplicate references for this worker, not legitimate other years.
        var distinctAwards = awards.Distinct()
            .OrderBy(x => x.Scope, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(x => int.TryParse(x.Year, out int year) ? year : 0)
            .ToArray();

        return new WorkerCareerRecord(championships, distinctAwards, notes);
    }

    // Kept for callers expecting plain text, but the UI uses structured
    // records for the company-oriented Titles & Accomplishments layout.
    public string ReadWorkerCareerSummary(int workerId)
    {
        var record = ReadWorkerCareerDetails(workerId);
        var text = new StringBuilder("CHAMPIONSHIPS");
        foreach (var company in record.Championships.GroupBy(x => x.Company))
        {
            text.AppendLine().AppendLine().AppendLine(company.Key);
            foreach (var title in company)
                text.AppendLine(title.Reigns + "x " + title.Title);
        }
        text.AppendLine().AppendLine("AWARDS & OTHER ACCOMPLISHMENTS");
        foreach (var award in record.Awards)
            text.AppendLine(award.Category + " (" + award.Year + ") · " +
                award.Scope + (award.Details.Length == 0 ? "" : " · " + award.Details));
        foreach (var note in record.Notes)
            text.AppendLine("Note: " + note);
        return text.ToString();
    }
}
