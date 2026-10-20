using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using TEWUniverse.Studio;

namespace TEWUniverse.Desktop;

/// <summary>
/// Existing-history augmentation and employment-check preview. Separate from
/// destructive full-history creation: no dates/defenses are recomputed here.
/// </summary>
public partial class MainWindow
{
    private readonly Dictionary<int, List<EmploymentWindow>> _titleEmploymentCache = new();
    private List<TitleSlotPatch> _advancedSlotPatches = new();
    private bool _advancedSlotMode;
    private int _advancedEmploymentWarnings;

    private TitleCompetitorFormat CurrentTitleFormat()
    {
        if (_session == null || _selectedExisting == null)
            return TitleCompetitorFormat.Singles;
        return _settings.GetTitleCompetitorFormat(_session.PathOnDisk, _selectedExisting.Id) switch
        {
            "TagTeam" => TitleCompetitorFormat.TagTeam,
            "Trios" => TitleCompetitorFormat.Trios,
            _ => TitleCompetitorFormat.Singles
        };
    }

    private void ConfigureEmploymentCompanyPicker()
    {
        if (_session == null || _selectedExisting == null) return;
        var options = new List<ExistingRecordItem> {
            new(0, "Automatic: title OriginalFed")
        };
        options.AddRange(_session.ListExistingRecords("Companies"));
        AdvancedEmploymentCompany.DisplayMemberPath = nameof(ExistingRecordItem.Name);
        AdvancedEmploymentCompany.ItemsSource = options;
        AdvancedEmploymentCompany.SelectedIndex = 0;
    }

    private int? EmploymentCompanyUid()
    {
        if (_session == null || _selectedExisting == null) return null;
        if (AdvancedEmploymentCompany.SelectedItem is ExistingRecordItem option &&
            option.Id > 0)
            return option.Id;
        return _session.ReadTitleOriginalCompany(_selectedExisting.Id);
    }

    private string EmploymentFlag(
        IReadOnlyList<int> workers, DateTime captured, DateTime lost, bool annual)
    {
        if (_session == null) return "FLAG: Employment history cannot be read";
        if (workers.Count == 0) return "Name-only holder (not linked to a worker)";
        int? company = EmploymentCompanyUid();
        if (!company.HasValue)
            return "FLAG: No company selected or linked in title OriginalFed";

        var notes = new List<string>();
        foreach (int uid in workers)
        {
            if (!_titleEmploymentCache.TryGetValue(uid, out var windows))
            {
                try
                {
                    windows = _session.ReadWorkerEmployment(uid)
                        .Select(entry => new EmploymentWindow(
                            entry.FedUid, entry.FromDate, entry.UntilDate)).ToList();
                    _titleEmploymentCache[uid] = windows;
                }
                catch (Exception ex)
                {
                    return "FLAG: Cannot verify tblCV for worker " + uid +
                        " (" + ex.Message + ")";
                }
            }
            var status = TitleEmploymentValidator.Check(company,
                captured, annual ? captured : lost, windows);
            if (status != EmploymentVerification.Covered)
            {
                string name = _allWorkers.FirstOrDefault(x => x.Id == uid)?.Name
                    ?? $"Worker UID {uid}";
                notes.Add(name + ": " + TitleEmploymentValidator.Label(status));
            }
        }
        return notes.Count == 0 ? "Covered by employment history" :
            string.Join("; ", notes);
    }

    private void ApplyEmploymentPreview(DataTable preview,
        IReadOnlyList<GeneratedTitleReign> proposal, bool annual)
    {
        preview.Columns.Add("Employment check", typeof(string));
        _advancedEmploymentWarnings = 0;
        for (int i = 0; i < proposal.Count; i++)
        {
            var holder = proposal[i];
            string status = EmploymentFlag(
                new[] { holder.Holder1, holder.Holder2, holder.Holder3 }.Where(x => x > 0).ToArray(),
                holder.Captured, holder.Lost, annual);
            preview.Rows[i]["Employment check"] = status;
            if (status.Contains("FLAG:", StringComparison.Ordinal))
                _advancedEmploymentWarnings++;
        }
    }

    private List<ExistingTitleSlot> ExistingSlots(TitleLineageResult result)
    {
        if (!string.Equals(result.LinkField, "BeltUID", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(result.OrderField, "BeltOrder", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Selective replacement requires verified BeltUID and BeltOrder references.");
        string NameField(string a, string b) =>
            result.Rows.Columns.Contains(a) ? a :
            result.Rows.Columns.Contains(b) ? b :
            throw new InvalidOperationException("Missing history holder name column: " + a);
        string n1 = NameField("Holder_Name1", "HolderName1");
        string n2 = NameField("Holder_Name2", "HolderName2");
        string n3 = NameField("Holder_Name3", "HolderName3");
        string[] mandatory = { "UID", "BeltOrder", "BeltCaptured",
            "BeltLost", "Defences", "Holder1", "Holder2", "Holder3" };
        if (mandatory.Any(f => !result.Rows.Columns.Contains(f)))
            throw new InvalidOperationException(
                "Cannot safely identify all existing title-history fields.");
        static int Number(DataRow row, string name) =>
            row[name] is DBNull ? -1 :
            Convert.ToInt32(row[name], CultureInfo.InvariantCulture);
        static string Name(DataRow row, string name) =>
            row[name] is DBNull ? "" :
            Convert.ToString(row[name], CultureInfo.InvariantCulture) ?? "";
        return result.Rows.Rows.Cast<DataRow>().Select(row =>
        {
            if (row["BeltCaptured"] is not DateTime captured ||
                row["BeltLost"] is not DateTime lost)
                throw new InvalidOperationException(
                    "An existing title-history date is missing or unreadable.");
            return new ExistingTitleSlot(
                Number(row, "UID"), Number(row, "BeltOrder"),
                captured, lost, Convert.ToInt16(row["Defences"], CultureInfo.InvariantCulture),
                Number(row, "Holder1"), Name(row, n1),
                Number(row, "Holder2"), Name(row, n2),
                Number(row, "Holder3"), Name(row, n3));
        }).OrderBy(x => x.Order).ToList();
    }

    private List<TitleSlotRequest> SlotRequests()
    {
        var format = CurrentTitleFormat();
        var requests = new List<TitleSlotRequest>();
        foreach (var participant in _advancedParticipants)
        {
            if (!_allWorkers.Any(w => w.Id == participant.WorkerId) ||
                !int.TryParse(participant.FirstYear, out int from) ||
                !int.TryParse(participant.LastYear, out int through) ||
                !int.TryParse(participant.RequiredReigns, out int total))
                throw new InvalidOperationException(
                    $"Enter valid linked worker, active years and reign count for {participant.DisplayName}.");

            int[] members = new[] {
                participant.WorkerId,
                format >= TitleCompetitorFormat.TagTeam &&
                    int.TryParse(participant.Partner2Uid, out int second) ? second : -1,
                format == TitleCompetitorFormat.Trios &&
                    int.TryParse(participant.Partner3Uid, out int third) ? third : -1
            }.Take((int)format).ToArray();
            var names = members.Select((id, index) => index == 0
                ? participant.DisplayName.Trim()
                : _allWorkers.FirstOrDefault(w => w.Id == id)?.Name ?? "").ToArray();
            if (members.Any(id => !_allWorkers.Any(w => w.Id == id)) ||
                members.Distinct().Count() != members.Length)
                throw new InvalidOperationException(
                    $"Select {(int)format} different existing workers for {participant.DisplayName}. " +
                    "For tag teams/trios enter valid Partner 2 / Partner 3 worker UIDs.");
            requests.Add(new TitleSlotRequest(
                participant.DisplayName.Trim(), members, names,
                from, through, total, participant.WinYears, participant.ReignLengths));
        }
        return requests;
    }

    private void GenerateSelectivePreview(TitleLineageResult history)
    {
        if (_session == null || _selectedExisting == null) return;
        var titleFormat = CurrentTitleFormat();
        var slots = ExistingSlots(history);
        var requested = SlotRequests();
        _titleEmploymentCache.Clear();
        var matches = ExistingTitleSlotPlanner.Plan(slots, requested, titleFormat,
            (request, slot) =>
                request.WorkerIds.Count(uid => EmploymentFlag(
                    new[] { uid }, slot.Captured, slot.Lost, IsAnnualTournament())
                    == "Covered by employment history"));

        ResetAdvancedDraft();
        _advancedSlotPatches = matches.ToList();
        _advancedSlotMode = true;
        _advancedDraftTitleId = _selectedExisting.Id;
        _advancedExistingSlots = slots.Count;
        _advancedDraftIsAnnualTournament = IsAnnualTournament();
        var indexed = matches.ToDictionary(p => p.Uid);
        var preview = new DataTable("Existing title lineage (selective updates)");
        preview.Columns.Add("Reign #", typeof(int));
        preview.Columns.Add("Year", typeof(int));
        preview.Columns.Add("Champions", typeof(string));
        preview.Columns.Add("Worker UIDs", typeof(string));
        preview.Columns.Add("Action", typeof(string));
        preview.Columns.Add("Captured", typeof(DateTime));
        preview.Columns.Add("Lost", typeof(DateTime));
        preview.Columns.Add("Defences", typeof(short));
        preview.Columns.Add("Employment check", typeof(string));
        _advancedEmploymentWarnings = 0;

        foreach (var slot in slots)
        {
            var patch = indexed.TryGetValue(slot.Uid, out var selected) ? selected : null;
            int[] holders = patch == null
                ? slot.LinkedWorkers
                : new[] { patch.Holder1, patch.Holder2, patch.Holder3 }
                    .Where(id => id > 0).ToArray();
            string[] names = patch == null
                ? new[] { slot.Name1, slot.Name2, slot.Name3 }
                    .Where(name => !string.IsNullOrWhiteSpace(name)).ToArray()
                : new[] { patch.Name1, patch.Name2, patch.Name3 }
                    .Where(name => !string.IsNullOrWhiteSpace(name)).ToArray();
            string check = EmploymentFlag(holders, slot.Captured, slot.Lost,
                _advancedDraftIsAnnualTournament);
            if (check.Contains("FLAG:", StringComparison.Ordinal))
                _advancedEmploymentWarnings++;
            preview.Rows.Add(slot.Order, slot.Captured.Year,
                string.Join(" / ", names), string.Join(", ", holders),
                patch == null ? "Preserved" : "REPLACE NAME-ONLY HOLDER",
                slot.Captured, slot.Lost, slot.Defences, check);
        }

        AdvancedPreviewGrid.ItemsSource = preview.DefaultView;
        AdvancedCandidatesGrid.Items.Refresh();
        AdvancedApplyButton.IsEnabled = true;
        AdvancedExportButton.IsEnabled = true;
        int originalLinked = slots.Count(s => !s.IsNameOnly);
        int remainingNpc = slots.Count(s => s.IsNameOnly) - matches.Count;
        int? companyId = EmploymentCompanyUid();
        string referenceCompany = companyId.HasValue
            ? (_session.ListExistingRecords("Companies").FirstOrDefault(x => x.Id == companyId.Value)?.Name
                ?? $"Company UID {companyId.Value}")
            : "unknown (employment cannot be verified)";
        AdvancedGenerationStatus.Text =
            $"SELECTIVE {titleFormat} PREVIEW • {matches.Count} name-only reign(s) to link. " +
            $"{originalLinked} already-linked reign(s), {remainingNpc} other name-only reign(s), " +
            $"and all {slots.Count} original dates / defenses / history UIDs are preserved. " +
            $"Employment reference: {referenceCompany}. " +
            (_advancedEmploymentWarnings > 0
                ? $"WARNING: {_advancedEmploymentWarnings} displayed reign(s) lack verified employment coverage. "
                : "All displayed linked workers have matching historical employment. ") +
            "Only the highlighted replacements will be written; backup recommended but optional.";
        SetStatus($"Prepared {matches.Count} targeted championship-history substitutions.");
    }
}
