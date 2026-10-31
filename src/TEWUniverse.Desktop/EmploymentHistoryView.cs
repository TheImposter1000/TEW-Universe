using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TEWUniverse.Studio;

namespace TEWUniverse.Desktop;

/// <summary>
/// Standalone tblCV workspace with a complete worker-wide view. Reuses the
/// existing verified employment editor and transactional single-row service.
/// </summary>
public partial class MainWindow
{
    private ComboBox _employmentWorkerPicker = null!;
    private TextBox _employmentSearch = null!;
    private string _employmentAppliedSearch = "";
    private bool _resettingEmploymentSearch;
    private List<EmploymentDisplay> _allEmploymentRows = new();

    private WorkerItem? EmploymentWorker =>
        _activeModule == "Employment History"
            ? _employmentWorkerPicker?.SelectedItem as WorkerItem
            : _worker;

    private void InitializeEmploymentHistoryModuleUI()
    {
        // The original tblCV editor is now hosted in its own main workspace.
        var root = EmploymentHost;
        var left = root.Children.OfType<DockPanel>().First();
        var header = left.Children.OfType<StackPanel>().First();
        header.Children.Insert(0, ExtraLabel("All worker employment periods", true));
        _employmentSearch = new TextBox {
            Style = (Style)FindResource("SearchBox"),
            MinHeight = 35,
            ToolTip = "Find a worker, company, initials, date or RecordName",
            Margin = new Thickness(0, 0, 0, 10)
        };
        _employmentSearch.TextChanged += (_, _) => {
            if (_resettingEmploymentSearch) return;
            if (_activeModule == "Employment History" && HasUnappliedWorkerExtraForm())
            {
                _resettingEmploymentSearch = true;
                try { _employmentSearch.Text = _employmentAppliedSearch; }
                finally { _resettingEmploymentSearch = false; }
                return;
            }
            FilterEmploymentHistory();
        };
        header.Children.Insert(1, _employmentSearch);

        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(actions, Dock.Bottom);
        left.Children.Insert(1, actions);
        void AddAction(string name, RoutedEventHandler action)
        {
            var button = ExtraButton(name, false, action);
            button.Margin = new Thickness(0, 0, 9, 8);
            actions.Children.Add(button);
        }
        AddAction("+ New Period", (_, _) => {
            if (HasUnappliedWorkerExtraForm() &&
                MessageBox.Show(this, "Discard unsaved changes and start a new period?",
                    "New Employment Period", MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            _settingWorkerExtraForm = true;
            try { _employmentGrid.SelectedItem = null; }
            finally { _settingWorkerExtraForm = false; }
            EmploymentSelectionChanged(_employmentGrid, null!);
            _employmentWorkerPicker.SelectedItem = null;
            _employmentWorkerPicker.IsEnabled = true;
            RefreshDirty();
        });
        AddAction("Delete Selected", RemoveEmployment_Click);
        AddAction("Export All CSV", ExportEmploymentCsv_Click);
        AddAction("Import CSV", ImportEmploymentCsv_Click);
        AddAction("Refresh", (_, _) => {
            if (!HasUnappliedWorkerExtraForm() ||
                MessageBox.Show(this, "Discard unsaved employment changes?",
                    "Refresh Employment", MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) == MessageBoxResult.Yes)
                ReloadEmploymentHistory();
        });

        TextColumn(_employmentGrid, "Worker", nameof(EmploymentDisplay.WorkerName),
            new DataGridLength(1.15, DataGridLengthUnitType.Star));
        // Put the worker before company/name for searching across all workers.
        int last = _employmentGrid.Columns.Count - 1;
        _employmentGrid.Columns[last].DisplayIndex = 0;

        var right = root.Children.OfType<ScrollViewer>().First();
        var editor = (StackPanel)right.Content;
        var workerGroup = new StackPanel();
        workerGroup.Children.Add(ExtraLabel("Worker (required for new periods)"));
        _employmentWorkerPicker = ExtraPicker(editable: true);
        _employmentWorkerPicker.DisplayMemberPath = nameof(WorkerItem.Name);
        TextSearch.SetTextPath(_employmentWorkerPicker, nameof(WorkerItem.Name));
        workerGroup.Children.Add(_employmentWorkerPicker);
        workerGroup.Children.Add(new TextBlock {
            Text = "Choose any existing worker for a new period. Selecting a row above fills its worker automatically.",
            Foreground = ExtraColor("#93B4D5"), FontSize = 11,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14)
        });
        editor.Children.Insert(1, workerGroup);
    }

    private void SelectEmploymentHistoryModule()
    {
        if (_activeModule == "Employment History") return;
        if (!MayLeave()) return;
        _activeModule = "Employment History";
        WorkerBrowserPanel.Visibility = Visibility.Collapsed;
        WorkerDetailPanel.Visibility = Visibility.Collapsed;
        ExistingWorkspace.Visibility = Visibility.Collapsed;
        RelationshipsWorkspace.Visibility = Visibility.Collapsed;
        ChemistryWorkspace.Visibility = Visibility.Collapsed;
        AwardsWorkspace.Visibility = Visibility.Collapsed;
        ToolsWorkspace.Visibility = Visibility.Collapsed;
        EmploymentWorkspace.Visibility = Visibility.Visible;
        foreach (var button in new[] { WorkersNav, CompaniesNav, TitlesNav,
            TagTeamsNav, EventsNav, StablesNav, RelationshipsNav,
            ChemistryNav, AwardsNav, ToolsNav })
            button.Style = (Style)FindResource("NavigationButton");
        EmploymentNav.Style = (Style)FindResource("BlueButton");
        _employmentWorkerPicker.ItemsSource = _allWorkers;
        _employmentSearch.Text = "";
        ReloadEmploymentHistory();
        RefreshDirty();
    }

    private void ReloadEmploymentHistory(int? selectedWorker = null, string? selectedName = null)
    {
        if (_employmentGrid == null || _employmentSearch == null) return;
        if (_session == null)
        {
            _allEmploymentRows.Clear();
            _employmentGrid.ItemsSource = null;
            _employmentSummary.Text = "Select an MDB scenario to see employment history.";
            return;
        }
        try
        {
            var records = _session.ReadAllWorkerEmployment();
            _employmentCompanies = _session.ReadEmploymentCompanies();
            var companyNames = _employmentCompanies.ToDictionary(c => c.Id, c => c.Name);
            var workerNames = _allWorkers.ToDictionary(w => w.Id, w => w.Name);
            _allEmploymentRows = records.Select(e => new EmploymentDisplay(e,
                companyNames.TryGetValue(e.FedUid, out var company)
                    ? company : e.Initials + " (UID " + e.FedUid + ")",
                workerNames.TryGetValue(e.WorkerUid, out var worker)
                    ? worker : "Unknown worker " + e.WorkerUid)).ToList();
            _settingWorkerExtraForm = true;
            try
            {
                _employmentCompany.ItemsSource = _employmentCompanies;
                _employmentWorkerPicker.ItemsSource = _allWorkers;
            }
            finally { _settingWorkerExtraForm = false; }
            FilterEmploymentHistory(selectedWorker, selectedName);
            SetStatus($"Loaded {_allEmploymentRows.Count:N0} employment-history periods.");
        }
        catch (Exception ex)
        {
            _allEmploymentRows.Clear();
            _employmentGrid.ItemsSource = null;
            _employmentSummary.Text = "Employment history could not load: " + ex.Message;
        }
    }

    private void FilterEmploymentHistory(int? workerUid = null, string? name = null)
    {
        if (_employmentGrid == null || _employmentSearch == null) return;
        string q = _employmentSearch.Text.Trim();
        var rows = _allEmploymentRows.Where(e => q.Length == 0 ||
            e.WorkerName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            e.Company.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            e.Entry.Initials.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            e.Entry.RecordName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            e.Entry.WorkerUid.ToString(CultureInfo.InvariantCulture).Contains(q) ||
            e.FromText.Contains(q) || e.UntilText.Contains(q)).ToList();
        if (!workerUid.HasValue && _employmentGrid.SelectedItem is EmploymentDisplay selected)
        {
            workerUid = selected.Entry.WorkerUid;
            name = selected.Entry.RecordName;
        }
        _settingWorkerExtraForm = true;
        try
        {
            _employmentGrid.ItemsSource = rows;
            _employmentGrid.SelectedItem = workerUid.HasValue
                ? rows.FirstOrDefault(x => x.Entry.WorkerUid == workerUid &&
                    string.Equals(x.Entry.RecordName, name, StringComparison.OrdinalIgnoreCase))
                : null;
            _employmentSummary.Text = $"{rows.Count:N0} of {_allEmploymentRows.Count:N0} historical periods. " +
                "Record Names are unique within each worker, not across the entire scenario.";
        }
        finally { _settingWorkerExtraForm = false; }
        EmploymentSelectionChanged(_employmentGrid, null!);
        _employmentAppliedSearch = _employmentSearch.Text;
    }

    private void ExportEmploymentCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null) return;
        var dialog = new SaveFileDialog {
            FileName = "TEW-Employment-History.csv",
            Filter = "CSV files (*.csv)|*.csv", AddExtension = true,
            Title = "Export all tblCV employment periods"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            // Re-read from the MDB, never export unsaved form edits.
            var data = _session.ReadAllWorkerEmployment();
            File.WriteAllText(dialog.FileName, EmploymentCsvCodec.ExportSimple(data),
                new System.Text.UTF8Encoding(true));
            SetStatus($"Exported {data.Count:N0} employment records to CSV.");
        }
        catch (Exception ex) { Error(ex); }
    }

    private void ImportEmploymentCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null) return;
        if (HasUnappliedWorkerExtraForm())
        {
            MessageBox.Show(this,
                "Save or discard the employment form changes before importing CSV.",
                "Unsaved Employment", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var picker = new OpenFileDialog {
            Filter = "CSV files (*.csv)|*.csv", CheckFileExists = true,
            Title = "Import edited Employment History CSV"
        };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(picker.FileName).Length > 20_000_000)
                throw new InvalidOperationException("Employment CSV exceeds 20 MB.");
            string csv = File.ReadAllText(picker.FileName);
            var original = _session.ReadAllWorkerEmployment();
            var workers = _allWorkers.Select(x => x.Id).ToHashSet();
            var companies = _session.ReadEmploymentCompanies().Select(x => x.Id).ToHashSet();
            EmploymentCsvPreview preview;
            if (EmploymentCsvCodec.IsLegacyFormat(csv))
            {
                var legacyImportChoice = MessageBox.Show(this,
                    "This is an older Employment History CSV with Action, " +
                    "OriginalRecordName and SourceFingerprint columns.\n\n" +
                    "YES: Treat the visible CSV rows as the entire history, like " +
                    "the Championship importer. Edited rows update records; added " +
                    "rows create records; missing rows and Action=Delete rows remove " +
                    "records. Original-name and fingerprint columns are ignored.\n\n" +
                    "NO: Apply only the older Add, Update and Delete actions. " +
                    "Each update must still have a distinct original identity.\n\n" +
                    "CANCEL: Do not import.",
                    "Old Employment CSV: Choose Import Method",
                    MessageBoxButton.YesNoCancel, MessageBoxImage.Question,
                    MessageBoxResult.Cancel);
                if (legacyImportChoice == MessageBoxResult.Cancel) return;
                preview = legacyImportChoice == MessageBoxResult.Yes
                    ? EmploymentCsvCodec.PreviewLegacyReplacement(
                        csv, original, workers, companies)
                    : EmploymentCsvCodec.Preview(csv, original, workers, companies);
            }
            else
                preview = EmploymentCsvCodec.Preview(
                    csv, original, workers, companies);
            if (preview.Changes.Count == 0)
            {
                MessageBox.Show(this, "This CSV has no new or changed employment periods.",
                    "Employment CSV");
                return;
            }
            int additions = preview.Changes.Count(x => x.IsAdd);
            int deletions = preview.Changes.Count(x => x.IsDelete);
            int edits = preview.Changes.Count - additions - deletions;
            string mode = preview.RebuildFromSnapshot
                ? "DUPLICATE-NAME RECOVERY: the MDB contains ambiguous old RecordNames. " +
                  "All existing employment records will be replaced by the CSV snapshot " +
                  "within one transaction. The listed Add and DELETE counts are physical " +
                  "rebuild operations, not new hires and departures. Create a backup first."
                : preview.IsReplacement
                    ? "Full-table synchronization: deleted spreadsheet rows will be deleted " +
                      "from the MDB, and new spreadsheet rows will be inserted."
                    : "Advanced Action CSV: only rows explicitly marked Delete are removed.";
            if (MessageBox.Show(this,
                $"Reviewed {preview.ExportedRows:N0} CSV records.\n\n" +
                $"Add: {additions:N0}\nUpdate: {edits:N0}\nDELETE: {deletions:N0}\n\n" +
                mode + "\n\n" +
                "All changes use one database transaction. No new workers or companies " +
                "are created. If any operation fails, every CSV change rolls back.",
                "Confirm Employment CSV Import", MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            if (deletions > 0 && MessageBox.Show(this,
                $"Confirm permanent deletion of {deletions:N0} employment history " +
                "record(s) from the selected TEW database.\n\n" +
                "This occurs because rows were removed from the full-table CSV " +
                "or explicitly marked Delete in an advanced CSV.\n\n" +
                "Only proceed if all of these deletions are intentional.",
                "FINAL CONFIRMATION: Delete Employment History",
                MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes) return;
            if (!OfferOptionalBackupBeforeWrite("importing the employment CSV")) return;
            int count = _session.ApplyEmploymentCsv(preview);
            _lastSaved = DateTime.Now;
            ReloadEmploymentHistory();
            RefreshDirty();
            SetStatus($"Imported {count:N0} employment CSV changes.");
            MessageBox.Show(this,
                $"Applied {count:N0} changes: {additions:N0} added, " +
                $"{edits:N0} updated, {deletions:N0} deleted.",
                "Employment CSV Import Complete");
        }
        catch (Exception ex) { Error(ex); }
    }
}
