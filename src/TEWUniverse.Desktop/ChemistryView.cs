using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using TEWUniverse.Studio;

namespace TEWUniverse.Desktop;

/// <summary>
/// Preset chemistry editor backed by real tblChemistryForce records.
/// Raw RelType / Quality codes are not given invented TEW IX labels.
/// </summary>
public partial class MainWindow
{
    private sealed record ChemistryChoice(int Code)
    {
        public override string ToString() => "Code " + Code.ToString(CultureInfo.InvariantCulture);
    }

    private DataGrid _chemGrid = null!;
    private TextBox _chemSearch = null!;
    private TextBlock _chemSummary = null!;
    private TextBlock _chemHeading = null!;
    private TextBlock _chemDetail = null!;
    private ComboBox _chemFirst = null!;
    private ComboBox _chemSecond = null!;
    private ComboBox _chemType = null!;
    private ComboBox _chemQuality = null!;
    private Button _chemSave = null!;
    private Button _chemDelete = null!;
    private ChemistryEntry? _selectedChemistry;
    private List<ChemistryEntry> _chemistryRecords = new();
    private bool _loadingChemistry;

    private static SolidColorBrush ChemistryBrush(string color) =>
        new((Color)ColorConverter.ConvertFromString(color)!);

    private static TextBlock ChemistryLabel(string label, bool headline = false) => new() {
        Text = label, FontSize = headline ? 21 : 13,
        FontWeight = headline ? FontWeights.Bold : FontWeights.SemiBold,
        Foreground = ChemistryBrush(headline ? "#EAF5FF" : "#BCD6EE"),
        Margin = new Thickness(0, headline ? 0 : 10, 0, 8)
    };

    private Button ChemistryButton(string label, RoutedEventHandler handler, bool main = false)
    {
        var button = new Button {
            Content = label,
            Style = (Style)FindResource(main ? "BlueButton" : "ActionButton"),
            Padding = new Thickness(13, 9, 13, 9),
            Margin = new Thickness(0, 0, 9, 8)
        };
        button.Click += handler;
        return button;
    }

    private void InitializeChemistryUI()
    {
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(450) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ChemistryWorkspace.Children.Add(layout);

        var left = new Border {
            Style = (Style)FindResource("CardBorder"),
            Padding = new Thickness(15), Margin = new Thickness(0, 0, 10, 0)
        };
        Grid.SetColumn(left, 0);
        layout.Children.Add(left);
        var browse = new DockPanel();
        left.Child = browse;
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(header, Dock.Top);
        browse.Children.Add(header);
        header.Children.Add(ChemistryLabel("Preset Chemistry", true));
        header.Children.Add(new TextBlock {
            Text = "Records from tblChemistryForce. Search by either worker, UID, type, or quality.",
            Foreground = ChemistryBrush("#9EBBDD"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });
        _chemSearch = new TextBox {
            Style = (Style)FindResource("SearchBox"),
            ToolTip = "Search existing chemistry"
        };
        _chemSearch.TextChanged += (_, _) => FilterChemistry();
        header.Children.Add(_chemSearch);
        _chemSummary = new TextBlock {
            Foreground = ChemistryBrush("#ABC9E6"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0)
        };
        header.Children.Add(_chemSummary);

        var actions = new WrapPanel { Margin = new Thickness(0, 11, 0, 0) };
        DockPanel.SetDock(actions, Dock.Bottom);
        browse.Children.Add(actions);
        actions.Children.Add(ChemistryButton("+ New Chemistry", (_, _) => {
            if (!ConfirmDiscardChemistryEdits()) return;
            _loadingChemistry = true;
            try { _chemGrid.SelectedItem = null; }
            finally { _loadingChemistry = false; }
            ShowChemistry(null);
            RefreshDirty();
        }, true));
        actions.Children.Add(ChemistryButton("Refresh", (_, _) => {
            if (ConfirmDiscardChemistryEdits()) ReloadChemistry();
        }));

        _chemGrid = ExtraGrid();
        _chemGrid.SelectionMode = DataGridSelectionMode.Single;
        _chemGrid.SelectionUnit = DataGridSelectionUnit.FullRow;
        TextColumn(_chemGrid, "Worker pairing", nameof(ChemistryEntry.PairLabel),
            new DataGridLength(1.6, DataGridLengthUnitType.Star));
        TextColumn(_chemGrid, "Type", nameof(ChemistryEntry.TypeLabel),
            new DataGridLength(0.7, DataGridLengthUnitType.Star));
        TextColumn(_chemGrid, "Quality", nameof(ChemistryEntry.QualityLabel),
            new DataGridLength(0.7, DataGridLengthUnitType.Star));
        _chemGrid.SelectionChanged += (_, _) => {
            if (_loadingChemistry) return;
            if (!ConfirmDiscardChemistryEdits())
            {
                _loadingChemistry = true;
                try { _chemGrid.SelectedItem = _selectedChemistry; }
                finally { _loadingChemistry = false; }
                return;
            }
            ShowChemistry(_chemGrid.SelectedItem as ChemistryEntry);
        };
        browse.Children.Add(_chemGrid);

        var right = new Border {
            Style = (Style)FindResource("CardBorder"), Padding = new Thickness(19)
        };
        Grid.SetColumn(right, 1);
        layout.Children.Add(right);
        var scroll = new ScrollViewer {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        right.Child = scroll;
        var editor = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        scroll.Content = editor;
        _chemHeading = ChemistryLabel("Create Preset Chemistry", true);
        editor.Children.Add(_chemHeading);
        _chemDetail = new TextBlock {
            TextWrapping = TextWrapping.Wrap,
            Foreground = ChemistryBrush("#A5C2DF"),
            Margin = new Thickness(0, 0, 0, 14)
        };
        editor.Children.Add(_chemDetail);

        editor.Children.Add(ChemistryLabel("First worker"));
        _chemFirst = WorkerPicker();
        editor.Children.Add(_chemFirst);
        editor.Children.Add(ChemistryLabel("Second worker"));
        _chemSecond = WorkerPicker();
        editor.Children.Add(_chemSecond);

        editor.Children.Add(ChemistryLabel("Chemistry type (RelType)"));
        _chemType = RelationshipCodePicker();
        editor.Children.Add(_chemType);
        editor.Children.Add(ChemistryLabel("Chemistry quality (Quality)"));
        _chemQuality = RelationshipCodePicker();
        editor.Children.Add(_chemQuality);

        editor.Children.Add(new TextBlock {
            Text = "Type and quality codes are taken from chemistry presets already " +
                   "stored in this database. Their TEW IX meanings have not been verified, " +
                   "so the editor shows the real code rather than guessing a label. " +
                   "If the table has no presets, adding new ones remains unavailable " +
                   "until the code mappings are confirmed.",
            Foreground = ChemistryBrush("#E3C38A"),
            TextWrapping = TextWrapping.Wrap, FontSize = 12,
            Margin = new Thickness(0, 12, 0, 18)
        });
        var buttons = new WrapPanel();
        editor.Children.Add(buttons);
        _chemSave = ChemistryButton("Create Chemistry", SaveChemistry_Click, true);
        _chemDelete = ChemistryButton("Delete Selected", DeleteChemistry_Click);
        buttons.Children.Add(_chemSave);
        buttons.Children.Add(_chemDelete);
        foreach (var field in new[] { _chemFirst, _chemSecond, _chemType, _chemQuality })
            field.SelectionChanged += (_, _) => {
                if (!_loadingChemistry && _activeModule == "Chemistry")
                    RefreshDirty();
            };
        editor.Children.Add(new TextBlock {
            Text = "Preset chemistry overrides naturally generated chemistry. Writes require " +
                   "confirmation, with a backup offered but never required.",
            Foreground = ChemistryBrush("#A5C2DF"), TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 16, 0, 0)
        });
        ShowChemistry(null);
    }

    private void SelectChemistryModule()
    {
        if (_activeModule == "Chemistry") return;
        if (!MayLeave()) return;
        _activeModule = "Chemistry";
        WorkerBrowserPanel.Visibility = Visibility.Collapsed;
        WorkerDetailPanel.Visibility = Visibility.Collapsed;
        ExistingWorkspace.Visibility = Visibility.Collapsed;
        RelationshipsWorkspace.Visibility = Visibility.Collapsed;
        AwardsWorkspace.Visibility = Visibility.Collapsed;
        ToolsWorkspace.Visibility = Visibility.Collapsed;
        ChemistryWorkspace.Visibility = Visibility.Visible;
        WorkersNav.Style = (Style)FindResource("NavigationButton");
        RelationshipsNav.Style = (Style)FindResource("NavigationButton");
        AwardsNav.Style = (Style)FindResource("NavigationButton");
        ToolsNav.Style = (Style)FindResource("NavigationButton");
        ChemistryNav.Style = (Style)FindResource("BlueButton");
        foreach (var button in new[] { CompaniesNav, TitlesNav, TagTeamsNav, EventsNav, StablesNav })
            button.Style = (Style)FindResource("NavigationButton");
        ReloadChemistry();
        RefreshDirty();
    }

    private void ReloadChemistry(int? uid = null)
    {
        if (_chemGrid == null) return;
        if (_session == null)
        {
            _chemistryRecords.Clear();
            _chemSummary.Text = "Select an MDB database to browse chemistry.";
            FilterChemistry();
            return;
        }
        try
        {
            _chemistryRecords = _session.ReadChemistry();
            _chemFirst.ItemsSource = _allWorkers;
            _chemSecond.ItemsSource = _allWorkers;
            FilterChemistry(uid);
            SetStatus($"Loaded {_chemistryRecords.Count:N0} chemistry presets.");
        }
        catch (Exception ex)
        {
            _chemistryRecords.Clear();
            _chemSummary.Text = "Could not load tblChemistryForce: " + ex.Message;
            _chemGrid.ItemsSource = null;
            ShowChemistry(null);
        }
    }

    private void FilterChemistry(int? uid = null)
    {
        if (_chemGrid == null) return;
        string search = _chemSearch?.Text.Trim() ?? "";
        var rows = _chemistryRecords.Where(x =>
            search.Length == 0 || x.SearchText.Contains(search,
                StringComparison.OrdinalIgnoreCase)).ToList();
        int? selected = uid ?? _selectedChemistry?.Uid;
        _loadingChemistry = true;
        try
        {
            _chemGrid.ItemsSource = rows;
            _chemGrid.SelectedItem = selected.HasValue ?
                rows.FirstOrDefault(x => x.Uid == selected) : null;
        }
        finally { _loadingChemistry = false; }
        _chemSummary.Text = $"{rows.Count:N0} of {_chemistryRecords.Count:N0} preset(s)";
        ShowChemistry(_chemGrid.SelectedItem as ChemistryEntry);
    }

    private void FillChemistryChoices(ComboBox picker,
        IEnumerable<int> codes, int? selection)
    {
        var choices = codes.Distinct().OrderBy(x => x)
            .Select(x => new ChemistryChoice(x)).ToList();
        if (selection.HasValue && choices.All(x => x.Code != selection.Value))
            choices.Add(new ChemistryChoice(selection.Value));
        picker.ItemsSource = choices;
        picker.SelectedItem = selection.HasValue
            ? choices.FirstOrDefault(x => x.Code == selection.Value) : null;
    }

    private void ShowChemistry(ChemistryEntry? record)
    {
        if (_chemFirst == null) return;
        _loadingChemistry = true;
        try
        {
            _selectedChemistry = record;
            _chemHeading.Text = record == null ? "Create Preset Chemistry" : "Edit Preset Chemistry";
            _chemDetail.Text = record == null
                ? "Select two existing workers and verified type / quality codes."
                : $"Chemistry UID {record.Uid} • {record.RecordName}. " +
                  "Worker identities are protected when editing.";
            _chemFirst.SelectedItem = record == null ? null :
                _allWorkers.FirstOrDefault(x => x.Id == record.Worker1);
            _chemSecond.SelectedItem = record == null ? null :
                _allWorkers.FirstOrDefault(x => x.Id == record.Worker2);
            _chemFirst.IsEnabled = record == null;
            _chemSecond.IsEnabled = record == null;
            FillChemistryChoices(_chemType, _chemistryRecords.Select(x => x.RelType),
                record?.RelType);
            FillChemistryChoices(_chemQuality, _chemistryRecords.Select(x => x.Quality),
                record?.Quality);
            _chemSave.Content = record == null ? "Create Chemistry" : "Save Chemistry";
            _chemDelete.IsEnabled = record != null;
        }
        finally { _loadingChemistry = false; }
        UpdateSectionCsvButtons();
    }

    private bool HasPendingChemistryEdits()
    {
        if (_activeModule != "Chemistry" || _loadingChemistry || _chemFirst == null)
            return false;
        var type = (_chemType.SelectedItem as ChemistryChoice)?.Code;
        var quality = (_chemQuality.SelectedItem as ChemistryChoice)?.Code;
        if (_selectedChemistry is { } selected)
            return type != selected.RelType || quality != selected.Quality;
        return _chemFirst.SelectedItem != null || _chemSecond.SelectedItem != null ||
            type.HasValue || quality.HasValue;
    }

    private bool ConfirmDiscardChemistryEdits() =>
        !HasPendingChemistryEdits() ||
        MessageBox.Show(this, "Discard unsaved chemistry changes?",
            "Unsaved Chemistry", MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;

    private void SaveChemistry_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null) return;
        try
        {
            if (_chemType.SelectedItem is not ChemistryChoice type ||
                _chemQuality.SelectedItem is not ChemistryChoice quality)
                throw new InvalidOperationException(
                    "Choose verified chemistry type and quality codes.");
            if (_selectedChemistry is { } original)
            {
                if (!HasPendingChemistryEdits())
                {
                    MessageBox.Show(this, "No chemistry changes to save.");
                    return;
                }
                if (MessageBox.Show(this,
                    $"Update chemistry for {original.PairLabel}?\n\n" +
                    $"RelType code: {type.Code}\nQuality code: {quality.Code}\n\n" +
                    "The worker pair and record identity will not change.",
                    "Confirm Chemistry Update", MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                if (!OfferOptionalBackupBeforeWrite("updating preset chemistry")) return;
                _session.UpdateChemistry(original, type.Code, quality.Code);
                _lastSaved = DateTime.Now;
                ReloadChemistry(original.Uid);
                SetStatus("Saved chemistry UID " + original.Uid);
            }
            else
            {
                if (_chemFirst.SelectedItem is not WorkerItem first ||
                    _chemSecond.SelectedItem is not WorkerItem second)
                    throw new InvalidOperationException("Choose two existing workers.");
                ChemistryRules.ValidatePair(first.Id, second.Id,
                    _allWorkers.Select(x => x.Id).ToHashSet());
                ChemistryRules.ValidateObserved(type.Code, quality.Code,
                    _chemistryRecords.Select(x => x.RelType).ToArray(),
                    _chemistryRecords.Select(x => x.Quality).ToArray());
                if (MessageBox.Show(this,
                    $"Create preset chemistry for {first.Name} / {second.Name}?\n\n" +
                    $"RelType code: {type.Code}\nQuality code: {quality.Code}\n\n" +
                    "This adds one tblChemistryForce record to the active MDB.",
                    "Confirm New Chemistry", MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                if (!OfferOptionalBackupBeforeWrite("creating preset chemistry")) return;
                int uid = _session.CreateChemistry(first.Id, second.Id, type.Code, quality.Code);
                _lastSaved = DateTime.Now;
                ReloadChemistry(uid);
                SetStatus("Created chemistry UID " + uid);
            }
            RefreshDirty();
        }
        catch (Exception ex) { Error(ex); }
    }

    private void DeleteChemistry_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null || _selectedChemistry is not { } selected) return;
        if (MessageBox.Show(this,
            $"Delete the chemistry preset for {selected.PairLabel}?\n\n" +
            $"RelType code {selected.RelType}, Quality code {selected.Quality}.\n" +
            "The workers themselves will not be deleted.",
            "Confirm Chemistry Deletion", MessageBoxButton.YesNo,
            MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        if (!OfferOptionalBackupBeforeWrite("deleting preset chemistry")) return;
        try
        {
            _session.DeleteChemistry(selected);
            _lastSaved = DateTime.Now;
            ReloadChemistry();
            RefreshDirty();
            SetStatus("Deleted chemistry UID " + selected.Uid);
        }
        catch (Exception ex) { Error(ex); }
    }
}
