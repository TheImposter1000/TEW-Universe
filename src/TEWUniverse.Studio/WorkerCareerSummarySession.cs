using System.Data;
using System.Data.OleDb;
using System.Globalization;
using System.Text;

namespace TEWUniverse.Studio;

/// <summary>
/// A READ-ONLY, on-demand career record generated from actual TEW IX rows.
/// No Studio-only accomplishments JSON and no text inferred from biographies.
/// </summary>
internal sealed partial class DatabaseSession
{
    public string ReadWorkerCareerSummary(int workerId)
    {
        CheckLinkedWorker(workerId);
        var titles = ListExistingRecords("Titles").ToDictionary(x => x.Id, x => x.Name);
        var companies = ListExistingRecords("Companies").ToDictionary(x => x.Id, x => x.Name);
        var championships = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var awards = new List<string>();
        var notes = new List<string>();

        // The game may also record active/current titleholders elsewhere. Only
        // confirmed pregame reigns are counted here; never invent an active reign.
        try
        {
            using var cmd = new OleDbCommand("SELECT * FROM [tblBeltPreHistory]", _connection);
            var data = new DataTable();
            using (var adapter = new OleDbDataAdapter(cmd)) adapter.Fill(data);
            foreach (DataRow row in data.Rows)
            {
                bool holder = new[] { "Holder1", "Holder2", "Holder3" }.Any(name =>
                    data.Columns.Contains(name) &&
                    int.TryParse(Display(row[name]), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out int id) && id == workerId);
                if (!holder) continue;
                int beltId = data.Columns.Contains("BeltUID") &&
                    int.TryParse(Display(row["BeltUID"]), out int found) ? found : -1;
                string title = titles.TryGetValue(beltId, out string? actual)
                    ? actual : "Title UID " + beltId;
                string captured = "";
                foreach (string dateField in new[] { "BeltCaptured", "Captured" })
                    if (data.Columns.Contains(dateField) && row[dateField] is not DBNull)
                    {
                        captured = Display(row[dateField]);
                        break;
                    }
                if (!championships.TryGetValue(title, out var histories))
                    championships[title] = histories = new List<string>();
                histories.Add(captured.Length > 0 ? captured : "Date not recorded");
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
                    string year = data.Columns.Contains("Year") ?
                        Display(row["Year"]) : "Year not recorded";
                    string company = "";
                    if (companySpecific)
                    {
                        string? companyCol = data.Columns.Contains("FedUID")
                            ? "FedUID" : data.Columns.Contains("Fed") ? "Fed" : null;
                        if (companyCol != null && int.TryParse(
                            Display(row[companyCol]), out int cid))
                            company = companies.TryGetValue(cid, out string? cname)
                                ? cname + " — " : "Company UID " + cid + " — ";
                    }
                    foreach (DataColumn column in data.Columns)
                    {
                        if (!AwardsRules.IsWorkerField(table, column.ColumnName,
                                column.DataType)) continue;
                        if (!int.TryParse(Display(row[column]), NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out int id) || id != workerId)
                            continue;
                        string category = column.ColumnName;
                        if (companySpecific && data.Columns.Contains("Award") &&
                            row["Award"] is not DBNull)
                            category += " (award code " + Display(row["Award"]) + ")";
                        awards.Add(year + " — " + company + category +
                            (companySpecific ? " (Company)" : " (World)"));
                    }
                }
            }
            catch (OleDbException ex)
            {
                notes.Add((companySpecific ? "Company" : "World") +
                    " awards unavailable: " + ex.Message);
            }
        }

        var result = new StringBuilder();
        result.AppendLine("CHAMPIONSHIPS — VERIFIED PRE-GAME TITLE HISTORY");
        result.AppendLine();
        if (championships.Count == 0)
            result.AppendLine("No linked championship reigns found in tblBeltPreHistory.");
        else foreach (var title in championships.OrderBy(x => x.Key))
        {
            result.AppendLine(title.Key + " — " + title.Value.Count +
                (title.Value.Count == 1 ? " reign" : " reigns"));
            foreach (string date in title.Value)
                result.AppendLine("    • Won: " + date);
            result.AppendLine();
        }
        result.AppendLine();
        result.AppendLine("AWARDS — RECORDED YEAR-END WINNERS");
        result.AppendLine();
        if (awards.Count == 0)
            result.AppendLine("No linked global/company award wins found in recognized winner fields.");
        else foreach (string award in awards.OrderBy(x => x))
            result.AppendLine("• " + award);
        result.AppendLine();
        if (notes.Count > 0)
        {
            result.AppendLine("UNAVAILABLE SOURCES");
            foreach (string note in notes) result.AppendLine("• " + note);
            result.AppendLine();
        }
        result.AppendLine("Read-only. Generated from linked MDB records each time this tab opens.");
        result.AppendLine("Title counts cover confirmed pre-game history, not unrecorded " +
            "biographical accomplishments or in-game save progress.");
        return result.ToString();
    }
}
