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
    private readonly Dictionary<string, TextBox> _awardInputs =
        new(StringComparer.OrdinalIgnoreCase);
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
            Text = "Browse actual TEW IX global and company award records. " +
                "Only verified existing fields can be edited.",
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
        RelationshipsWorkspace.Visibility = Visibility.Collapsed;
        AwardsWorkspace.Visibility = Visibility.Visible;
        WorkersNav.Style = (Style)FindResource("NavigationButton");
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
                selected == null ? "Award details" : selected.Label, true));
            _awardForm.Children.Add(new TextBlock {
                Text = "Actual MDB field names and values are displayed. Unknown " +
                    "fields are read-only. A verified numeric winner field must " +
                    "reference a real worker/company UID. No new award rows " +
                    "are generated without verifying the full record schema.",
                Foreground = ExtraColor("#B2CBE3"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 13)
            });
            if (selected != null)
                foreach (var field in selected.Fields)
                {
                    _awardForm.Children.Add(ExtraLabel(field.Name +
                        (field.Editable ? " (verified editable)" : " (read only)")));
                    var input = ExtraTextBox();
                    input.Text = field.StoredValue;
                    input.ToolTip = field.DisplayValue;
                    input.IsReadOnly = !field.Editable;
                    input.Opacity = field.Editable ? 1 : 0.65;
                    if (field.Editable)
                    {
                        _awardInputs[field.Name] = input;
                        input.TextChanged += (_, _) => UpdateAwardSaveState();
                    }
                    _awardForm.Children.Add(input);
                    if (field.DisplayValue != field.StoredValue)
                    {
                        var detail = ExtraLabel(field.DisplayValue);
                        detail.Margin = new Thickness(0, -8, 0, 10);
                        _awardForm.Children.Add(detail);
                    }
                }
            _awardSave = ExtraButton("Save Selected Award", true,
                SaveAward_Click);
            _awardForm.Children.Add(_awardSave);
        }
        finally { _loadingAwards = false; }
        UpdateAwardSaveState();
    }

    private Dictionary<string, string> ChangedAwardFields()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (_selectedAward == null) return values;
        foreach (var field in _selectedAward.Fields.Where(x => x.Editable))
            if (_awardInputs.TryGetValue(field.Name, out var input) &&
                input.Text.Trim() != field.StoredValue)
                values[field.Name] = input.Text.Trim();
        return values;
    }

    private bool HasPendingAwardEdits() =>
        !_loadingAwards && _activeModule == "Awards" &&
        ChangedAwardFields().Count != 0;

    private void UpdateAwardSaveState()
    {
        if (_loadingAwards || _awardSave == null) return;
        int changes = ChangedAwardFields().Count;
        _awardSave.IsEnabled = _session != null && changes > 0;
        _awardSave.Content = changes == 0 ? "Save Selected Award" :
            $"Save Selected Award ({changes} changed)";
        RefreshDirty();
    }

    private bool ConfirmDiscardAwardEdits() =>
        !HasPendingAwardEdits() || MessageBox.Show(this,
            "Discard unsaved award changes?", "Unapplied Awards",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) ==
            MessageBoxResult.Yes;

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
