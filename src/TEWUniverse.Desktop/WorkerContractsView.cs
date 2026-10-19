using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using TEWUniverse.Studio;

namespace TEWUniverse.Desktop;

/// <summary>
/// Active tblContract records. Existing contracts can be inspected and their
/// supported scalar properties edited; historical employment remains tblCV.
/// New contract insertion is intentionally disabled until every mandatory
/// TEW IX contract field and its allowed values can be verified.
/// </summary>
public partial class MainWindow
{
    private DataGrid _contractsGrid = null!;
    private StackPanel _contractDetails = null!;
    private TextBlock _contractSummary = null!;
    private Button _contractSave = null!;
    private bool _settingContractForm;
    private WorkerContractEntry? _selectedContract;
    private List<WorkerContractEntry> _contractRows = new();
    private readonly Dictionary<string, TextBox> _contractInputs =
        new(StringComparer.OrdinalIgnoreCase);

    private void InitializeWorkerContractsUI()
    {
        _contractsGrid = ExtraGrid();
        TextColumn(_contractsGrid, "Contract record", "Label",
            new DataGridLength(1.1, DataGridLengthUnitType.Star));
        TextColumn(_contractsGrid, "Company", "Company",
            new DataGridLength(1, DataGridLengthUnitType.Star));
        _contractsGrid.SelectionChanged += ContractSelectionChanged;

        _contractDetails = new StackPanel { Margin = new Thickness(1, 0, 8, 6) };
        _contractDetails.Children.Add(ExtraLabel("Contract details", true));
        _contractDetails.Children.Add(ExtraLabel(
            "Select an existing contract on the left. Supported existing " +
            "fields are editable; keys and unverified company links stay protected."));
        _contractSave = ExtraButton("Save Selected Contract", true, SaveContract_Click);
        _contractSave.IsEnabled = false;
        _contractDetails.Children.Add(_contractSave);
        AppendCreateContractButton();

        BuildExtraSection(ContractsHost, "Worker Contracts", _contractsGrid,
            _contractDetails, out _contractSummary);
    }

    private void ResetWorkerContractsUI()
    {
        if (_contractsGrid == null) return;
        _settingContractForm = true;
        try
        {
            _contractRows.Clear();
            _contractInputs.Clear();
            _selectedContract = null;
            _contractsGrid.ItemsSource = null;
            _contractDetails.Children.Clear();
            _contractDetails.Children.Add(ExtraLabel("Contract details", true));
            _contractDetails.Children.Add(ExtraLabel(
                "Select a worker and an existing contract to inspect its fields."));
            _contractSave = ExtraButton("Save Selected Contract", true, SaveContract_Click);
            _contractSave.IsEnabled = false;
            _contractDetails.Children.Add(_contractSave);
            AppendCreateContractButton();
            _contractSummary.Text = "Select a worker to view existing contracts.";
        }
        finally { _settingContractForm = false; }
    }

    private void RefreshWorkerContracts(object? selectKey = null)
    {
        if (_session == null || _worker == null) return;
        try
        {
            var rows = _session.ReadWorkerContracts(_worker.Id);
            _settingContractForm = true;
            try
            {
                _contractRows = rows;
                _selectedContract = null;
                _contractInputs.Clear();
                _contractsGrid.ItemsSource = rows;
                _contractsGrid.SelectedItem = selectKey == null ? null :
                    rows.FirstOrDefault(x => Equals(x.Key, selectKey));
                _contractSummary.Text = rows.Count == 0
                    ? $"No tblContract records linked to {_worker.Name}. " +
                      "Historical company periods are under Employment History."
                    : $"{rows.Count} active contract record(s) for {_worker.Name}. " +
                      "Use Save Selected Contract for fields in the selected record.";
            }
            finally { _settingContractForm = false; }
            ShowSelectedContract(_contractsGrid.SelectedItem as WorkerContractEntry);
        }
        catch (Exception ex)
        {
            ResetWorkerContractsUI();
            _contractSummary.Text = "Cannot read this worker's contracts: " + ex.Message;
        }
    }

    private void ContractSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settingContractForm) return;
        var next = _contractsGrid.SelectedItem as WorkerContractEntry;
        if (_selectedContract != null && !ReferenceEquals(next, _selectedContract) &&
            HasUnappliedContractEdits() &&
            MessageBox.Show(this, "Discard unsaved changes to the selected contract?",
                "Unapplied Contract Edits", MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            _settingContractForm = true;
            try { _contractsGrid.SelectedItem = _selectedContract; }
            finally { _settingContractForm = false; }
            return;
        }
        ShowSelectedContract(next);
    }

    private void ShowSelectedContract(WorkerContractEntry? selected)
    {
        _settingContractForm = true;
        try
        {
            _selectedContract = selected;
            _contractInputs.Clear();
            _contractDetails.Children.Clear();
            _contractDetails.Children.Add(ExtraLabel(
                selected == null ? "Contract details" : selected.Label, true));
            if (selected == null)
            {
                _contractDetails.Children.Add(ExtraLabel(
                    "Select a contract on the left to inspect its original MDB fields."));
            }
            else
            {
                _contractDetails.Children.Add(ExtraLabel(
                    selected.Company + "  •  tblContract (current contract)"));
                _contractDetails.Children.Add(new TextBlock {
                    Text = "Dates: MM/DD/YYYY. Numeric enumerations are raw TEW IX codes. " +
                        "UID, record identity and foreign-key links cannot be edited here.",
                    Foreground = ExtraColor("#A8C4E1"), FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 15)
                });
                foreach (var field in selected.Fields)
                {
                    _contractDetails.Children.Add(ExtraLabel(
                        field.Name + (field.CanEdit ? "" : "  (read only)")));
                    var input = ExtraTextBox();
                    input.Text = field.Value;
                    input.IsReadOnly = !field.CanEdit;
                    input.Opacity = field.CanEdit ? 1.0 : 0.72;
                    if (field.Type == typeof(DateTime))
                        input.ToolTip = "MM/DD/YYYY";
                    if (field.Type == typeof(bool))
                        input.ToolTip = "True or False";
                    if (field.CanEdit) _contractInputs.Add(field.Name, input);
                    _contractDetails.Children.Add(input);
                }
            }
            _contractSave = ExtraButton("Save Selected Contract", true, SaveContract_Click);
            _contractSave.IsEnabled = selected != null &&
                selected.Fields.Any(f => f.CanEdit);
            _contractDetails.Children.Add(_contractSave);
            AppendCreateContractButton();
            _contractDetails.Children.Add(new TextBlock {
                Text = "New contracts copy the complete terms of an existing contract " +
                    "from the same company. Review every value before creation; " +
                    "unverified fields are never invented. A manual backup is required.",
                Foreground = ExtraColor("#93B4D5"), FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 10)
            });
        }
        finally { _settingContractForm = false; }
    }

    private void AppendCreateContractButton()
    {
        var button = ExtraButton("Create Contract From Template", true,
            CreateWorkerContract_Click);
        button.IsEnabled = _session != null && _worker != null;
        _contractDetails.Children.Add(button);
    }

    private void CreateWorkerContract_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null || _worker == null) return;
        if (HasEdits() || HasUnappliedContractEdits())
        {
            MessageBox.Show(this, "Save or discard unsaved changes before creating a contract.");
            return;
        }
        if (string.IsNullOrWhiteSpace(_lastManualBackup) ||
            !File.Exists(_lastManualBackup) ||
            !AppSettings.SamePath(_lastBackupDatabase, _session.PathOnDisk))
        {
            MessageBox.Show(this,
                "Choose CREATE BACKUP before adding a contract. The scenario MDB " +
                "must be recoverable if TEW IX rejects any copied contract terms.",
                "Create a backup first", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            var dialog = new WorkerContractCreationDialog(_session, _worker,
                _session.ListExistingRecords("Companies")) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.ChosenTemplate == null) return;
            var template = dialog.ChosenTemplate;
            var companyName = _session.ListExistingRecords("Companies")
                .FirstOrDefault(c => c.Id == template.CompanyId)?.Name ??
                $"Company #{template.CompanyId}";
            if (MessageBox.Show(this,
                $"CREATE NEW CONTRACT for {_worker.Name} with {companyName}?\n\n" +
                $"Using template: {template.Label}\n" +
                $"Edited terms: {dialog.Terms.Count}\n\n" +
                "All other template terms, including any unverified numeric codes, " +
                "will be copied exactly. The original template contract stays unchanged. " +
                "This inserts ONE new tblContract row; it does not insert a CV record. " +
                "Please verify the result using TEW IX Data Check.",
                "Confirm New Contract", MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            int newId = _session.CreateWorkerContract(
                _worker.Id, template, dialog.Terms);
            _lastSaved = DateTime.Now;
            RefreshWorkerContracts(newId);
            SetStatus($"Created contract UID {newId} for {_worker.Name} with {companyName}.");
            RefreshDirty();
        }
        catch (Exception ex) { Error(ex); }
    }

    private Dictionary<string, string> ChangedContractFields()
    {
        var changed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (_selectedContract == null) return changed;
        foreach (var field in _selectedContract.Fields.Where(f => f.CanEdit))
            if (_contractInputs.TryGetValue(field.Name, out var input) &&
                !string.Equals(input.Text, field.Value, StringComparison.Ordinal))
                changed[field.Name] = input.Text;
        return changed;
    }

    private bool HasUnappliedContractEdits() =>
        !_settingContractForm && _selectedContract != null &&
        ChangedContractFields().Count > 0;

    private void SaveContract_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null || _worker == null || _selectedContract == null) return;
        try
        {
            var fields = ChangedContractFields();
            if (fields.Count == 0)
            {
                MessageBox.Show(this, "No contract fields have changed.");
                return;
            }
            if (!ConfirmExtraWrite(
                $"Update {fields.Count} field(s) in {_selectedContract.Label} " +
                $"for {_worker.Name} ({_selectedContract.Company})? " +
                "This modifies an existing active contract, not employment history.",
                "Save Worker Contract")) return;

            var identity = _selectedContract.Key;
            int saved = _session.SaveWorkerContract(
                _worker.Id, _selectedContract, fields);
            _lastSaved = DateTime.Now;
            RefreshWorkerContracts(identity);
            SetStatus($"Saved {saved} contract field(s) for {_worker.Name}.");
            RefreshDirty();
        }
        catch (Exception ex) { Error(ex); }
    }
}
