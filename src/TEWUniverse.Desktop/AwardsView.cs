using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TEWUniverse.Studio;

namespace TEWUniverse.Desktop;

/// <summary>
/// Company and world year-end awards are independent actual TEW IX tables.
/// Never infer that a raw numeric award column is a worker ID unless verified.
/// </summary>
public partial class MainWindow
{
    private ComboBox _awardScope = null!;
    private TextBox _awardSearch = null!;
    private DataGrid _awardGrid = null!;
    private TextBlock _awardSummary = null!;
    private StackPanel _awardForm = null!;
    private Button _awardSave = null!;
    private AwardEntry? _selectedAward;
    private List<AwardEntry> _awardEntries = new();
    private readonly Dictionary<string, Control> _awardInputs =
        new(StringComparer.OrdinalIgnoreCase);
    private TextBox _awardNewYear = null!;
    private Button _awardCreate = null!;
    private bool _loadingAwards;

    private void InitializeAwardsUI()
    {
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(355) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AwardsWorkspace.Children.Add(root);

        var listBorder = new Border {
            Style = (Style)FindResource("CardBorder"),
            Padding = new Thickness(15), Margin = new Thickness(0, 0, 9, 0)
        };
        Grid.SetColumn(listBorder, 0);
        root.Children.Add(listBorder);
        var listPanel = new DockPanel();
        listBorder.Child = listPanel;

        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(heading, Dock.Top);
        listPanel.Children.Add(heading);
        heading.Children.Add(ExtraLabel("Year-End Awards", true));
        heading.Children.Add(new TextBlock {
            Text = "Select recipients and companies by name. Edit existing awards " +
                "or create new award years by copying a real existing record.",
            TextWrapping = TextWrapping.Wrap, Foreground = ExtraColor("#B2CBE3"),
            Margin = new Thickness(0, 0, 0, 12)
        });
        _awardScope = ExtraPicker();
        _awardScope.ItemsSource = new[] { "World Year-End Awards", "Company Year-End Awards" };
        _awardScope.SelectedIndex = 0;
        _awardScope.SelectionChanged += (_, _) => {
            if (_loadingAwards) return;
            if (!ConfirmDiscardAwardEdits())
            {
                _loadingAwards = true;
                try { _awardScope.SelectedIndex = _awardCompanySpecific ? 1 : 0; }
                finally { _loadingAwards = false; }
                return;
            }
            _awardCompanySpecific = _awardScope.SelectedIndex == 1;
            ReloadAwards();
        };
        heading.Children.Add(_awardScope);
        _awardSearch = ExtraTextBox();
        _awardSearch.ToolTip = "Find awards by year, worker name, field value, or UID";
        _awardSearch.TextChanged += (_, _) => FilterAwards();
        heading.Children.Add(_awardSearch);
        _awardSummary = ExtraLabel("Select a database to browse awards.");
        _awardSummary.TextWrapping = TextWrapping.Wrap;
        heading.Children.Add(_awardSummary);

        var refresh = ExtraButton("Refresh Awards", false, (_, _) => {
            if (ConfirmDiscardAwardEdits()) ReloadAwards();
        });
        DockPanel.SetDock(refresh, Dock.Bottom);
        listPanel.Children.Add(refresh);
        _awardGrid = ExtraGrid();
        TextColumn(_awardGrid, "Award year / record", "Label",
            new DataGridLength(1, DataGridLengthUnitType.Star));
        TextColumn(_awardGrid, "UID", "Uid", new DataGridLength(75));
        _awardGrid.SelectionChanged += (_, _) => {
            if (_loadingAwards) return;
            var next = _awardGrid.SelectedItem as AwardEntry;
            if (!ReferenceEquals(_selectedAward, next) && !ConfirmDiscardAwardEdits())
            {
                _loadingAwards = true;
                try { _awardGrid.SelectedItem = _selectedAward; }
                finally { _loadingAwards = false; }
                return;
            }
            ShowAward(next);
        };
        listPanel.Children.Add(_awardGrid);

        var editor = new Border {
            Style = (Style)FindResource("CardBorder"),
            Padding = new Thickness(18)
        };
        Grid.SetColumn(editor, 1);
        root.Children.Add(editor);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility =
            ScrollBarVisibility.Auto };
        editor.Child = scroll;
        _awardForm = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
        scroll.Content = _awardForm;
        ShowAward(null);
    }

    private bool _awardCompanySpecific;

    private void SelectAwardsModule()
    {
        if (_activeModule == "Awards") return;
        if (!MayLeave()) return;
        _activeModule = "Awards";
        WorkerBrowserPanel.Visibility = Visibility.Collapsed;
        WorkerDetailPanel.Visibility = Visibility.Collapsed;
        ExistingWorkspace.Visibility = Visibility.Collapsed;
        EmploymentWorkspace.Visibility = Visibility.Collapsed;
        EmploymentNav.Style = (Style)FindResource("NavigationButton");
        RelationshipsWorkspace.Visibility = Visibility.Collapsed;
        ChemistryWorkspace.Visibility = Visibility.Collapsed;
        ChemistryNav.Style = (Style)FindResource("NavigationButton");
        AwardsWorkspace.Visibility = Visibility.Visible;
        WorkersNav.Style = (Style)FindResource("NavigationButton");
        ToolsWorkspace.Visibility = Visibility.Collapsed;
        ToolsNav.Style = (Style)FindResource("NavigationButton");
        RelationshipsNav.Style = (Style)FindResource("NavigationButton");
        AwardsNav.Style = (Style)FindResource("BlueButton");
        foreach (var button in new[] {
            CompaniesNav, TitlesNav, TagTeamsNav, EventsNav, StablesNav
        }) button.Style = (Style)FindResource("NavigationButton");
        ReloadAwards();
        RefreshDirty();
    }

    private void ReloadAwards(int? uid = null)
    {
        if (_awardGrid == null) return;
        if (_session == null)
        {
            _awardEntries.Clear();
            _awardSummary.Text = "Select an MDB database to browse awards.";
            _loadingAwards = true;
            try { _awardGrid.ItemsSource = null; }
            finally { _loadingAwards = false; }
            ShowAward(null);
            return;
        }
        try
        {
            int? selectedUid = uid ?? _selectedAward?.Uid;
            _awardEntries = _session.ReadAwards(_awardCompanySpecific);
            FilterAwards(selectedUid);
            SetStatus($"Loaded {_awardEntries.Count:N0} " +
                (_awardCompanySpecific ? "company" : "world") + " award records.");
        }
        catch (Exception ex)
        {
            _awardEntries.Clear();
            _awardSummary.Text = "Unable to read awards: " + ex.Message;
            _loadingAwards = true;
            try { _awardGrid.ItemsSource = null; }
            finally { _loadingAwards = false; }
            ShowAward(null);
        }
    }

    private void FilterAwards(int? selectUid = null)
    {
        if (_awardGrid == null) return;
        string search = _awardSearch?.Text.Trim() ?? "";
        var visible = _awardEntries.Where(x => search.Length == 0 ||
            x.SearchText.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
        bool keepForm = !selectUid.HasValue && HasPendingAwardEdits();
        int? savedId = selectUid ?? _selectedAward?.Uid;
        _loadingAwards = true;
        try
        {
            _awardGrid.ItemsSource = visible;
            _awardGrid.SelectedItem = savedId.HasValue
                ? visible.FirstOrDefault(x => x.Uid == savedId.Value) : null;
        }
        finally { _loadingAwards = false; }
        _awardSummary.Text = $"{visible.Count:N0} of {_awardEntries.Count:N0} records";
        if (!keepForm) ShowAward(_awardGrid.SelectedItem as AwardEntry);
    }

    private void ShowAward(AwardEntry? selected)
    {
        if (_awardForm == null) return;
        _loadingAwards = true;
        try
        {
            _selectedAward = selected;
            _awardInputs.Clear();
            _awardForm.Children.Clear();
            _awardForm.Children.Add(ExtraLabel(
                selected == null ? "Select an award year" : selected.Label, true));
            _awardForm.Children.Add(new TextBlock {
                Text = selected == null
                    ? "Choose a year on the left to edit its winners or create a new record."
                    : "Choose actual workers for each award category. The current year's " +
                      "winners and the new year's winners are saved separately.",
                Foreground = ExtraColor("#B2CBE3"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 13)
            });

            // Keep both actions above the long category list. The prior layout
            // buried creation after dozens of uneditable raw database fields.
            _awardSave = ExtraButton("Save Winners for Selected Year", true,
                SaveAward_Click);
            _awardForm.Children.Add(_awardSave);
            _awardForm.Children.Add(ExtraLabel("Create a New Award Year", true));
            _awardNewYear = ExtraTextBox();
            _awardNewYear.ToolTip = "Enter the new calendar year (1800–2100)";
            _awardNewYear.TextChanged += (_, _) => UpdateAwardSaveState();
            _awardForm.Children.Add(_awardNewYear);
            _awardCreate = ExtraButton("+ Create New Award Year", true,
                CreateAward_Click);
            _awardForm.Children.Add(_awardCreate);
            _awardForm.Children.Add(new TextBlock {
                Text = "Creates a separate record using this year as its template. " +
                    "Choose winners below before creating; unselected winner categories " +
                    "start unassigned, and the original record is not changed.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = ExtraColor("#A9C6E3"),
                Margin = new Thickness(0, 0, 0, 15)
            });

            if (selected != null)
            {
                _awardForm.Children.Add(ExtraLabel("Award Year, Promotion and Winners", true));
                foreach (var field in selected.Fields.Where(x => x.Editable))
                {
                    var label = ExtraLabel(AwardsRules.FriendlyName(field.Name));
                    label.ToolTip = "TEW IX column: " + field.Name;
                    _awardForm.Children.Add(label);
                    Control input;
                    string table = _awardCompanySpecific
                        ? AwardsRules.CompanyTable : AwardsRules.GlobalTable;
                    bool recipient = _session != null &&
                        AwardsRules.IsWorkerField(table, field.Name, field.Type);
                    bool company = _session != null &&
                        AwardsRules.IsCompanyField(table, field.Name, field.Type);
                    if (recipient || company)
                    {
                        // Editable ComboBox offers type-a-name search even for
                        // large universes with thousands of workers.
                        var picker = ExtraPicker(editable: true);
                        int.TryParse(field.StoredValue, out int current);
                        var options = (recipient
                            ? _allWorkers.Select(w => new ExistingRecordItem(w.Id, w.Name))
                            : _session!.ListExistingRecords("Companies"))
                            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
                        if (AwardsRules.CanBeUnassigned(table, field.Name, field.Type))
                            options.Insert(0, new ExistingRecordItem(0,
                                recipient ? "(No winner recorded)" : "(No company selected)"));
                        if (!options.Any(x => x.Id == current))
                            options.Insert(0, new ExistingRecordItem(current,
                                "Unresolved stored ID " + field.StoredValue));
                        picker.ItemsSource = options;
                        picker.DisplayMemberPath = nameof(ExistingRecordItem.Name);
                        TextSearch.SetTextPath(picker, nameof(ExistingRecordItem.Name));
                        picker.SelectedItem = options.FirstOrDefault(x => x.Id == current);
                        picker.SelectionChanged += (_, _) => UpdateAwardSaveState();
                        input = picker;
                    }
                    else
                    {
                        var text = ExtraTextBox();
                        text.Text = field.StoredValue;
                        text.TextChanged += (_, _) => UpdateAwardSaveState();
                        input = text;
                    }
                    _awardInputs[field.Name] = input;
                    _awardForm.Children.Add(input);
                }

                var details = new Expander {
                    Header = "Technical database details (read only)",
                    IsExpanded = false,
                    Foreground = ExtraColor("#B2CBE3"),
                    Margin = new Thickness(0, 14, 0, 10)
                };
                var readOnly = new StackPanel();
                foreach (var field in selected.Fields.Where(x => !x.Editable))
                {
                    readOnly.Children.Add(ExtraLabel(
                        AwardsRules.FriendlyName(field.Name) + ": " + field.DisplayValue));
                }
                details.Content = readOnly;
                _awardForm.Children.Add(details);
            }
        }
        finally { _loadingAwards = false; }
        UpdateAwardSaveState();
    }

    private static string AwardInputValue(Control control) => control switch
    {
        ComboBox picker => (picker.SelectedItem as ExistingRecordItem)?.Id
            .ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
        TextBox input => input.Text.Trim(),
        _ => ""
    };

    private Dictionary<string, string> ChangedAwardFields()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (_selectedAward == null) return values;
        foreach (var field in _selectedAward.Fields.Where(x => x.Editable))
            if (_awardInputs.TryGetValue(field.Name, out var input) &&
                AwardInputValue(input) != field.StoredValue)
                values[field.Name] = AwardInputValue(input);
        return values;
    }

    private bool HasPendingAwardEdits() =>
        !_loadingAwards && _activeModule == "Awards" &&
        (ChangedAwardFields().Count != 0 ||
         !string.IsNullOrWhiteSpace(_awardNewYear?.Text));

    private void UpdateAwardSaveState()
    {
        if (_loadingAwards || _awardSave == null) return;
        int changes = ChangedAwardFields().Count;
        _awardSave.IsEnabled = _session != null && changes > 0;
        _awardSave.Content = changes == 0 ? "Save Selected Award" :
            $"Save Selected Award ({changes} changed)";
        if (_awardCreate != null)
            _awardCreate.IsEnabled = _session != null && _selectedAward != null &&
                !string.IsNullOrWhiteSpace(_awardNewYear?.Text);
        RefreshDirty();
    }

    private bool ConfirmDiscardAwardEdits() =>
        !HasPendingAwardEdits() || MessageBox.Show(this,
            "Discard unsaved award changes?", "Unapplied Awards",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) ==
            MessageBoxResult.Yes;

    private void CreateAward_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null || _selectedAward == null) return;
        try
        {
            // A fresh dialog prevents the selected template's current winners
            // from accidentally becoming the new year's winners.
            var dialog = new AwardCreationDialog(
                _selectedAward, _awardCompanySpecific, _allWorkers,
                _session.ListExistingRecords("Companies"),
                _awardNewYear.Text.Trim()) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            string year = dialog.AwardYear;
            AwardsRules.ValidateYear(year);
            if (!ConfirmExtraWrite(
                $"CREATE a new {(_awardCompanySpecific ? "company" : "world")} award " +
                $"for {year} using record UID {_selectedAward.Uid} as the template? " +
                "Recipients have been chosen separately. The template stays unchanged.",
                "Create Year-End Award")) return;
            if (!OfferOptionalBackupBeforeWrite("creating this award")) return;
            int uid = _session.CreateAwardFromTemplate(
                _awardCompanySpecific, _selectedAward, year, dialog.Winners);
            _lastSaved = DateTime.Now;
            ReloadAwards(uid);
            SetStatus($"Created award UID {uid} for year {year}.");
            RefreshDirty();
        }
        catch (Exception ex) { Error(ex); }
    }

    private void SaveAward_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null || _selectedAward == null) return;
        var edits = ChangedAwardFields();
        if (edits.Count == 0) return;
        if (!ConfirmExtraWrite(
            $"Update {edits.Count} verified field(s) in award UID {_selectedAward.Uid}? " +
            "Unknown award fields and other records are preserved.",
            "Save Year-End Award")) return;
        try
        {
            int selectedUid = _selectedAward.Uid;
            int saved = _session.SaveAward(selectedUid, _awardCompanySpecific,
                _selectedAward, edits);
            _lastSaved = DateTime.Now;
            ReloadAwards(selectedUid);
            SetStatus($"Saved {saved} award field(s) in record UID {selectedUid}.");
            RefreshDirty();
        }
        catch (Exception ex) { Error(ex); }
    }
}
