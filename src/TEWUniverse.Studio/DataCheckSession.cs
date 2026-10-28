using System.Data;
using System.Data.OleDb;
using System.Globalization;

namespace TEWUniverse.Studio;

internal sealed record StudioDataIssue(
    string Severity, string Area, string Record, string Description, int WorkerUid = 0)
{
    public string SearchText => Severity + " " + Area + " " + Record + " " + Description;
}

internal sealed partial class DatabaseSession
{
    private DataTable ReadCheckTable(string table, List<StudioDataIssue> issues)
    {
        var result = new DataTable(table);
        try
        {
            using var command = new OleDbCommand(
                "SELECT * FROM [" + table + "]", _connection);
            using var adapter = new OleDbDataAdapter(command);
            adapter.Fill(result);
        }
        catch (Exception ex) when (ex is OleDbException or InvalidOperationException)
        {
            issues.Add(new StudioDataIssue("Unavailable", table, "Table",
                "This table could not be checked: " + ex.Message));
        }
        return result;
    }

    private static int ReadCheckId(DataRow row, string field)
    {
        if (!row.Table.Columns.Contains(field) || row[field] is DBNull)
            return 0;
        return int.TryParse(Convert.ToString(row[field], CultureInfo.InvariantCulture),
            NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
            ? result : 0;
    }

    private static string ReadCheckString(DataRow row, string field) =>
        row.Table.Columns.Contains(field) ? Display(row[field]) : "";

    /// <summary>
    /// Read-only consistency scan of the active MDB. Never changes, repairs,
    /// or rewrites any Access table. Textual biography disagreements are
    /// "Review", never "Error", because fictional careers may predate this DB.
    /// </summary>
    public List<StudioDataIssue> RunStudioDataCheck()
    {
        var issues = new List<StudioDataIssue>();
        var workers = ListWorkers();
        var workerNames = workers.ToDictionary(x => x.Id, x => x.Name);
        var workerUids = workerNames.Keys.ToHashSet();
        var titles = ListExistingRecords("Titles").ToDictionary(x => x.Id, x => x.Name);
        var titleUids = titles.Keys.ToHashSet();

        var teamTable = ReadCheckTable("tblTeam", issues);
        var teamRecords = new List<(int Uid, string Name, int Worker1, int Worker2)>();
        var memberships = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
        var pairIndex = new HashSet<string>(StringComparer.Ordinal);
        if (teamTable.Columns.Contains("Worker1") &&
            teamTable.Columns.Contains("Worker2"))
            foreach (DataRow row in teamTable.Rows)
            {
                int uid = ReadCheckId(row, "UID");
                int a = ReadCheckId(row, "Worker1");
                int b = ReadCheckId(row, "Worker2");
                int fed = ReadCheckId(row, "Fed");
                string name = ReadCheckString(row, "Name");
                string record = name.Length == 0 ? "UID " + uid : name + " (UID " + uid + ")";
                teamRecords.Add((uid, name, a, b));
                foreach (int person in new[] { a, b })
                    if (DataCheckRules.MissingLinkedId(person, workerUids))
                        issues.Add(new StudioDataIssue("Error", "Tag Teams", record,
                            "Worker UID " + person + " does not exist in tblWorker.", person));
                if (a > 0 && b > 0)
                {
                    if (a == b)
                        issues.Add(new StudioDataIssue("Error", "Tag Teams", record,
                            "Worker1 and Worker2 are the same wrestler.", a));
                    string key = fed + ":" + Math.Min(a, b) + ":" + Math.Max(a, b);
                    if (!pairIndex.Add(key))
                        issues.Add(new StudioDataIssue("Review", "Tag Teams", record,
                            "Same worker pair appears more than once for company UID " + fed +
                            ". This may be an intentional inactive or alternate team.", a));
                }
                if (name.Length > 0)
                {
                    if (!memberships.TryGetValue(name, out var members))
                        memberships[name] = members = new HashSet<int>();
                    if (a > 0) members.Add(a);
                    if (b > 0) members.Add(b);
                }
            }

        var reigns = ReadCheckTable("tblBeltPreHistory", issues);
        var byTitleWorker = new Dictionary<(int Belt, int Worker), int>();
        if (reigns.Columns.Contains("BeltUID"))
            foreach (DataRow row in reigns.Rows)
            {
                int belt = ReadCheckId(row, "BeltUID");
                int uid = ReadCheckId(row, "UID");
                if (DataCheckRules.MissingLinkedId(belt, titleUids))
                    issues.Add(new StudioDataIssue("Error", "Championships",
                        "Historical reign UID " + uid,
                        "BeltUID " + belt + " has no matching championship in tblBelt."));
                foreach (string column in new[] { "Holder1", "Holder2", "Holder3" })
                {
                    if (!reigns.Columns.Contains(column)) continue;
                    int person = ReadCheckId(row, column);
                    if (DataCheckRules.MissingLinkedId(person, workerUids))
                        issues.Add(new StudioDataIssue("Error", "Championships",
                            "Historical reign UID " + uid,
                            column + " points at a missing worker UID " + person +
                            ". An unlinked historical NPC should use -1 with a name.", person));
                    else if (person < -1)
                        issues.Add(new StudioDataIssue("Review", "Championships",
                            "Historical reign UID " + uid,
                            column + " has an unexpected negative identifier " + person +
                            ". Confirm it is not a temporary drafting value."));
                    if (person > 0 && titleUids.Contains(belt) && workerUids.Contains(person))
                    {
                        var key = (belt, person);
                        byTitleWorker.TryGetValue(key, out int count);
                        byTitleWorker[key] = count + 1;
                    }
                }
            }

        // End-of-year global award fields have a limited, positively identified
        // set of worker reference columns. Do not guess company-award field codes.
        var awards = ReadCheckTable(AwardsRules.GlobalTable, issues);
        foreach (DataRow row in awards.Rows)
        {
            int uid = ReadCheckId(row, "UID");
            foreach (DataColumn col in awards.Columns)
            {
                if (!AwardsRules.IsWorkerField(AwardsRules.GlobalTable,
                    col.ColumnName, col.DataType)) continue;
                int recipient = ReadCheckId(row, col.ColumnName);
                if (DataCheckRules.MissingLinkedId(recipient, workerUids))
                    issues.Add(new StudioDataIssue("Error", "Awards",
                        "World award UID " + uid,
                        col.ColumnName + " references missing worker UID " + recipient,
                        recipient));
            }
        }

        var bios = ReadCheckTable("tblWorkerBio", issues);
        foreach (DataRow row in bios.Rows)
        {
            int uid = ReadCheckId(row, "UID");
            string bio = ReadCheckString(row, "Profile");
            if (uid <= 0 || string.IsNullOrWhiteSpace(bio) ||
                !workerNames.TryGetValue(uid, out string? workerName)) continue;
            foreach (var title in titles)
            {
                int? claim = DataCheckRules.ClaimedReigns(bio, title.Value);
                if (claim is not int declared) continue;
                byTitleWorker.TryGetValue((title.Key, uid), out int recorded);
                if (declared != recorded)
                    issues.Add(new StudioDataIssue("Review", "Biography / Titles",
                        workerName + " (UID " + uid + ")",
                        "Biography appears to claim " + declared + " " +
                        title.Value + " reign(s), but pregame title lineage contains " +
                        recorded + ". This may be historical, out-of-scenario, " +
                        "or a text-parsing ambiguity; check manually.", uid));
            }
            foreach (var team in memberships)
            {
                if (!DataCheckRules.MentionsTeam(bio, team.Key) ||
                    team.Value.Contains(uid)) continue;
                issues.Add(new StudioDataIssue("Review", "Biography / Tag Teams",
                    workerName + " (UID " + uid + ")",
                    "Biography mentions team '" + team.Key +
                    "', but this wrestler is not currently linked to that " +
                    "tblTeam record. They may be a former member or opponent.", uid));
            }
        }
        return issues.OrderBy(x => x.Severity switch {
            "Error" => 0, "Review" => 1, _ => 2
        }).ThenBy(x => x.Area).ThenBy(x => x.Record).ToList();
    }
}
