using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using TEWUniverse.Studio;

namespace TEWUniverse.Desktop;

/// <summary>
/// Standalone Relationships workspace: browses actual tblRelation rows and
/// creates/edits/deletes them with explicit user confirmation and manual backup.
/// </summary>
public partial class MainWindow
{
    private DataGrid _relationGrid = null!;
    private TextBox _relationSearch = null!;
    private ComboBox _relationFilter = null!;
    private ComboBox _relationWorker1 = null!;
    private ComboBox _relationWorker2 = null!;
    private ComboBox _relationPersonal = null!;
    private ComboBox _relationRomantic = null!;
    private ComboBox _relationFamily = null!;
    private CheckBox _relationMentor = null!;
    private Button _relationSave = null!;
    private Button _relationDelete = null!;
    private TextBlock _relationSummary = null!;
    private TextBlock _relationFormHeading = null!;
    private TextBlock _relationRecordDetails = null!;
    private bool _loadingRelationshipForm;
    private RelationshipEntry? _selectedRelationship;
    private List<RelationshipEntry> _relationshipRows = new();

    private static SolidColorBrush RelationBrush(string value) =>
        new((Color)ColorConverter.ConvertFromString(value)!);

    private static TextBlock RelationLabel(string text, bool heading = false) => new() {
        Text = text,
        FontWeight = heading ? FontWeights.Bold : FontWeights.SemiBold,
        FontSize = heading ? 19 : 12,
        Foreground = RelationBrush(heading ? "#EAF5FF" : "#B9D4ED"),
        Margin = new Thickness(0, heading ? 0 : 10, 0, 8)
    };

    private Button RelationButton(string text, RoutedEventHandler handler, bool primary = false)
    {
        var button = new Button {
            Content = text,
            Style = (Style)FindResource(primary ? "BlueButton" : "ActionButton"),
            Margin = new Thickness(0, 0, 8, 7),
            Padding = new Thickness(13, 9, 13, 9)
        };
        button.Click += handler;
        return button;
    }

    private void InitializeRelationshipsUI()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(450) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        RelationshipsWorkspace.Children.Add(grid);

        var left = new Border {
            Style = (Style)FindResource("CardBorder"),
            Margin = new Thickness(0, 0, 10, 0),
            Padding = new Thickness(14)
        };
        Grid.SetColumn(left, 0);
        grid.Children.Add(left);
        var leftPanel = new DockPanel();
        left.Child = leftPanel;
        var listingHeader = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(listingHeader, Dock.Top);
        leftPanel.Children.Add(listingHeader);
        listingHeader.Children.Add(RelationLabel("Worker Relationships", true));
        listingHeader.Children.Add(new TextBlock {
            Text = "Actual tblRelation records. Search both workers or their UIDs.",
            TextWrapping = TextWrapping.Wrap, Foreground = RelationBrush("#9FB7D6"),
            FontSize = 12, Margin = new Thickness(0, 0, 0, 12)
        });
        _relationSearch = new TextBox {
            Style = (Style)FindResource("SearchBox"),
            ToolTip = "Search both wrestler names, relationship names or stored worker IDs"
        };
        _relationSearch.TextChanged += (_, _) => FilterRelationshipList();
        listingHeader.Children.Add(_relationSearch);
        _relationFilter = new ComboBox {
            Style = (Style)FindResource("DarkComboBox"),
            ItemsSource = new[] { "All relationships", "Personal", "Romantic",
                "Family", "Mentor / Protégé" },
            SelectedIndex = 0, Margin = new Thickness(0, 9, 0, 0),
            Height = 35
        };
        _relationFilter.SelectionChanged += (_, _) => FilterRelationshipList();
        listingHeader.Children.Add(_relationFilter);
        _relationSummary = new TextBlock {
            Foreground = RelationBrush("#A8C7E4"), FontSize = 12,
            Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap
        };
        listingHeader.Children.Add(_relationSummary);
        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(actions, Dock.Bottom);
        leftPanel.Children.Add(actions);
        actions.Children.Add(RelationButton("+ New Relationship", (_, _) => {
            if (!ConfirmDiscardRelationshipForm()) return;
            _loadingRelationshipForm = true;
            try { _relationGrid.SelectedItem = null; }
            finally { _loadingRelationshipForm = false; }
            ShowRelationshipForm(null);
            RefreshDirty();
        }, true));
        actions.Children.Add(RelationButton("Refresh", (_, _) => {
            if (ConfirmDiscardRelationshipForm()) ReloadRelationships();
        }));
        _relationGrid = new DataGrid {
            AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false,
            CanUserDeleteRows = false, SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            Background = RelationBrush("#102238"),
            Foreground = RelationBrush("#E2EDF8"),
            RowBackground = RelationBrush("#142B45"),
            AlternatingRowBackground = RelationBrush("#193550"),
            BorderBrush = RelationBrush("#3B5673"),
            BorderThickness = new Thickness(1),
            RowHeight = 30, ColumnHeaderHeight = 35
        };
        _relationGrid.Columns.Add(new DataGridTextColumn {
            Header = "Workers", Binding = new Binding(nameof(RelationshipEntry.PairLabel)),
            Width = new DataGridLength(1.1, DataGridLengthUnitType.Star)
        });
        _relationGrid.Columns.Add(new DataGridTextColumn {
            Header = "Relationships", Binding = new Binding(nameof(RelationshipEntry.CategorySummary)),
            Width = new DataGridLength(1.2, DataGridLengthUnitType.Star)
        });
        _relationGrid.SelectionChanged += RelationshipSelectionChanged;
        leftPanel.Children.Add(_relationGrid);

        var right = new Border {
            Style = (Style)FindResource("CardBorder"), Padding = new Thickness(18)
        };
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        var scroll = new ScrollViewer {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        right.Child = scroll;
        var form = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        scroll.Content = form;
        _relationFormHeading = RelationLabel("Create Relationship", true);
        form.Children.Add(_relationFormHeading);
        _relationRecordDetails = new TextBlock {
            TextWrapping = TextWrapping.Wrap,
            Foreground = RelationBrush("#A8C7E4"),
            Margin = new Thickness(0, 0, 0, 13)
        };
        form.Children.Add(_relationRecordDetails);

        form.Children.Add(RelationLabel("Worker 1"));
        _relationWorker1 = WorkerPicker();
        form.Children.Add(_relationWorker1);
        form.Children.Add(RelationLabel("Worker 2"));
        _relationWorker2 = WorkerPicker();
        form.Children.Add(_relationWorker2);
        form.Children.Add(new TextBlock {
            Text = "Worker order is preserved on existing records. " +
                   "Both selections must refer to different real workers.",
            Foreground = RelationBrush("#A8C7E4"), TextWrapping = TextWrapping.Wrap,
            FontSize = 11, Margin = new Thickness(0, 0, 0, 10)
        });

        form.Children.Add(RelationLabel("Personal relationship (P_Rel)"));
        _relationPersonal = RelationshipCodePicker();
        form.Children.Add(_relationPersonal);
        form.Children.Add(RelationLabel("Romantic relationship (R_Rel)"));
        _relationRomantic = RelationshipCodePicker();
        form.Children.Add(_relationRomantic);
        form.Children.Add(RelationLabel("Family relationship (F_Rel)"));
        _relationFamily = RelationshipCodePicker();
        form.Children.Add(_relationFamily);
        _relationMentor = new CheckBox {
            Content = "Mentor relationship (Yes / No)", IsThreeState = false,
            Foreground = RelationBrush("#EAF5FF"),
            Margin = new Thickness(0, 16, 0, 13)
        };
        form.Children.Add(_relationMentor);
        form.Children.Add(new TextBlock {
            Text = "A pair can have several relationship elements at once. " +
                   "The dropdown labels are descriptive; the MDB keeps its original numeric codes. " +
                   "Unknown existing codes are shown without interpreting them.",
            Foreground = RelationBrush("#9FB7D6"), TextWrapping = TextWrapping.Wrap,
            FontSize = 12, Margin = new Thickness(0, 0, 0, 13)
        });
        var buttons = new WrapPanel();
        form.Children.Add(buttons);
        _relationSave = RelationButton("Create Relationship", SaveRelationship_Click, true);
        buttons.Children.Add(_relationSave);
        _relationDelete = RelationButton("Delete Selected", DeleteRelationship_Click);
        buttons.Children.Add(_relationDelete);
        foreach (var selector in new[] { _relationWorker1, _relationWorker2,
                     _relationPersonal, _relationRomantic, _relationFamily })
            selector.SelectionChanged += (_, _) => {
                if (!_loadingRelationshipForm && _activeModule == "Relationships")
                    RefreshDirty();
            };
        _relationMentor.Checked += (_, _) => {
            if (!_loadingRelationshipForm && _activeModule == "Relationships")
                RefreshDirty();
        };
        _relationMentor.Unchecked += (_, _) => {
            if (!_loadingRelationshipForm && _activeModule == "Relationships")
                RefreshDirty();
        };
        form.Children.Add(new TextBlock {
            Text = "Before writing, choose to create a backup, continue without one, " +
                   "or cancel. Final confirmation is still required. Verify changes using TEW IX Data Check.",
            Foreground = RelationBrush("#E5BF78"), TextWrapping = TextWrapping.Wrap,
            FontSize = 12, Margin = new Thickness(0, 15, 0, 0)
        });
        ShowRelationshipForm(null);
    }

    private ComboBox WorkerPicker() => new() {
        Style = (Style)FindResource("DarkComboBox"),
        IsEditable = true, IsTextSearchEnabled = true, MaxDropDownHeight = 260,
        Height = 35, Margin = new Thickness(0, 0, 0, 6),
        ToolTip = "Type a worker name to find a worker; the list includes stored UIDs."
    };

    private ComboBox RelationshipCodePicker() => new() {
        Style = (Style)FindResource("DarkComboBox"),
        IsEditable = false, Height = 35, MaxDropDownHeight = 260,
        Margin = new Thickness(0, 0, 0, 4)
    };

    private void SelectRelationshipsModule()
    {
        if (_activeModule == "Relationships") return;
        if (!MayLeave()) return;
        _activeModule = "Relationships";
        WorkerBrowserPanel.Visibility = Visibility.Collapsed;
        WorkerDetailPanel.Visibility = Visibility.Collapsed;
        ExistingWorkspace.Visibility = Visibility.Collapsed;
        RelationshipsWorkspace.Visibility = Visibility.Visible;
        AwardsWorkspace.Visibility = Visibility.Collapsed;
        AwardsNav.Style = (Style)FindResource("NavigationButton");
        WorkersNav.Style = (Style)FindResource("NavigationButton");
        RelationshipsNav.Style = (Style)FindResource("BlueButton");
        foreach (var button in new[] { CompaniesNav, TitlesNav, TagTeamsNav, EventsNav, StablesNav })
            button.Style = (Style)FindResource("NavigationButton");
        _relationWorker1.ItemsSource = _allWorkers;
        _relationWorker2.ItemsSource = _allWorkers;
        ReloadRelationships();
        RefreshDirty();
    }

    private void ReloadRelationships(int? selectUid = null)
    {
        if (_session == null)
        {
            _relationshipRows.Clear();
            _relationGrid.ItemsSource = null;
            _relationSummary.Text = "Choose a TEW IX scenario MDB to browse relationships.";
            ShowRelationshipForm(null);
            return;
        }
        try
        {
            _relationshipRows = _session.ReadRelationships();
            FilterRelationshipList(selectUid);
            SetStatus($"Loaded {_relationshipRows.Count:N0} worker relationship records.");
        }
        catch (Exception ex)
        {
            _relationshipRows.Clear();
            _relationGrid.ItemsSource = null;
            _relationSummary.Text = "Relationship table cannot be read: " + ex.Message;
            ShowRelationshipForm(null);
        }
    }

    private void FilterRelationshipList(int? selectUid = null)
    {
        if (_relationGrid == null) return;
        string search = _relationSearch?.Text.Trim() ?? "";
        string kind = _relationFilter?.SelectedItem as string ?? "All relationships";
        bool CategoryMatch(RelationshipEntry row) => kind switch {
            "Personal" => row.Personal != 0,
            "Romantic" => row.Romantic != 0,
            "Family" => row.Family != 0,
            "Mentor / Protégé" => row.Mentor,
            _ => true
        };
        var visible = _relationshipRows.Where(r => CategoryMatch(r) &&
            (search.Length == 0 ||
             r.Worker1Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             r.Worker2Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             r.Worker1.ToString(CultureInfo.InvariantCulture).Contains(search, StringComparison.OrdinalIgnoreCase) ||
             r.Worker2.ToString(CultureInfo.InvariantCulture).Contains(search, StringComparison.OrdinalIgnoreCase) ||
             r.RecordName.Contains(search, StringComparison.OrdinalIgnoreCase))).ToList();
        // Filtering must not discard edits in progress. The selected row may
        // disappear from the current list, but the form remains untouched.
        bool keepEditing = !selectUid.HasValue && HasPendingRelationshipEdits();
        int? keepUid = selectUid ?? _selectedRelationship?.Uid;
        _loadingRelationshipForm = true;
        try
        {
            _relationGrid.ItemsSource = visible;
            _relationGrid.SelectedItem = keepUid is int uid
                ? visible.FirstOrDefault(r => r.Uid == uid)
                : null;
        }
        finally { _loadingRelationshipForm = false; }
        _relationSummary.Text = $"{visible.Count:N0} of {_relationshipRows.Count:N0} records";
        if (!keepEditing) ShowRelationshipForm(_relationGrid.SelectedItem as RelationshipEntry);
    }

    private void RelationshipSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingRelationshipForm) return;
        var selected = _relationGrid.SelectedItem as RelationshipEntry;
        if (!ConfirmDiscardRelationshipForm())
        {
            _loadingRelationshipForm = true;
            _relationGrid.SelectedItem = _selectedRelationship;
            _loadingRelationshipForm = false;
            return;
        }
        ShowRelationshipForm(selected);
    }

    private static TewCodeChoice? SelectedCode(ComboBox picker) =>
        picker.SelectedItem as TewCodeChoice;

    private void FillRelationshipCodes(ComboBox picker, string field, int code)
    {
        var codes = TewCodeLabels.Choices("tblRelation", field).ToList();
        var selected = codes.FirstOrDefault(x => x.Code == code);
        if (selected == null)
        {
            selected = new TewCodeChoice(code, "Code " + code + " (unmapped, preserved)");
            codes.Add(selected);
        }
        picker.ItemsSource = codes;
        picker.SelectedItem = selected;
    }

    private void ShowRelationshipForm(RelationshipEntry? entry)
    {
        if (_relationWorker1 == null) return;
        _loadingRelationshipForm = true;
        try
        {
            _selectedRelationship = entry;
            _relationFormHeading.Text = entry == null ? "Create Relationship" : "Edit Relationship";
            _relationRecordDetails.Text = entry == null
                ? "Select two wrestlers, set one or more relationship elements, then create."
                : $"Relation UID {entry.Uid}  •  Stored name: {entry.RecordName}" +
                  "  •  Worker identity fields cannot be changed here.";
            _relationWorker1.SelectedItem = entry == null ? null :
                _allWorkers.FirstOrDefault(x => x.Id == entry.Worker1);
            _relationWorker2.SelectedItem = entry == null ? null :
                _allWorkers.FirstOrDefault(x => x.Id == entry.Worker2);
            _relationWorker1.IsEnabled = entry == null;
            _relationWorker2.IsEnabled = entry == null;
            FillRelationshipCodes(_relationPersonal, "P_Rel", entry?.Personal ?? 0);
            FillRelationshipCodes(_relationRomantic, "R_Rel", entry?.Romantic ?? 0);
            FillRelationshipCodes(_relationFamily, "F_Rel", entry?.Family ?? 0);
            _relationMentor.IsChecked = entry?.Mentor ?? false;
            _relationSave.Content = entry == null ? "Create Relationship" : "Save Relationship";
            _relationDelete.IsEnabled = entry != null;
        }
        finally { _loadingRelationshipForm = false; }
    }

    private bool HasPendingRelationshipEdits()
    {
        if (_activeModule != "Relationships" || _loadingRelationshipForm ||
            _relationPersonal == null) return false;
        int personal = SelectedCode(_relationPersonal)?.Code ?? -1;
        int romantic = SelectedCode(_relationRomantic)?.Code ?? -1;
        int family = SelectedCode(_relationFamily)?.Code ?? -1;
        bool mentor = _relationMentor.IsChecked == true;
        if (_selectedRelationship is { } original)
            return personal != original.Personal || romantic != original.Romantic ||
                family != original.Family || mentor != original.Mentor;
        return _relationWorker1.SelectedItem != null || _relationWorker2.SelectedItem != null ||
            personal != 0 || romantic != 0 || family != 0 || mentor;
    }

    private bool ConfirmDiscardRelationshipForm() =>
        !HasPendingRelationshipEdits() ||
        MessageBox.Show(this,
            "Discard unsaved relationship changes?", "Unsaved Relationship",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    private bool ReadyForRelationshipWrite()
    {
        if (_session == null)
        {
            MessageBox.Show(this, "Select a TEW IX scenario MDB first.");
            return false;
        }
        return OfferOptionalBackupBeforeWrite("editing worker relationships");
    }

    private void SaveRelationship_Click(object sender, RoutedEventArgs e)
    {
        if (!ReadyForRelationshipWrite()) return;
        try
        {
            var p = SelectedCode(_relationPersonal) ??
                throw new InvalidOperationException("Choose a personal relationship code.");
            var r = SelectedCode(_relationRomantic) ??
                throw new InvalidOperationException("Choose a romantic relationship code.");
            var f = SelectedCode(_relationFamily) ??
                throw new InvalidOperationException("Choose a family relationship code.");
            bool mentor = _relationMentor.IsChecked == true;
            string detail = $"Personal: {p.Label}\nRomantic: {r.Label}\n" +
                $"Family: {f.Label}\nMentor: {(mentor ? "Yes" : "No")}";
            if (_selectedRelationship is { } old)
            {
                if (!HasPendingRelationshipEdits())
                {
                    MessageBox.Show(this, "No relationship values have changed.");
                    return;
                }
                if (MessageBox.Show(this,
                    $"Save relationship {old.Worker1Name} / {old.Worker2Name}?\n\n{detail}\n\n" +
                    "Existing worker IDs and record identity are preserved. " +
                    "This writes to the active MDB.",
                    "Confirm Relationship Update", MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                _session!.UpdateRelationship(old, p.Code, r.Code, f.Code, mentor);
                _lastSaved = DateTime.Now;
                ReloadRelationships(old.Uid);
                SetStatus($"Updated relationship UID {old.Uid}.");
            }
            else
            {
                if (_relationWorker1.SelectedItem is not WorkerItem first ||
                    _relationWorker2.SelectedItem is not WorkerItem second)
                    throw new InvalidOperationException("Choose two existing workers.");
                RelationshipRules.ValidatePair(first.Id, second.Id,
                    _allWorkers.Select(w => w.Id).ToHashSet());
                RelationshipRules.ValidateCodes(p.Code, r.Code, f.Code, mentor);
                if (MessageBox.Show(this,
                    $"Create relationship {first.Name} / {second.Name}?\n\n{detail}\n\n" +
                    "This adds one tblRelation record to the active MDB. " +
                    "Existing workers will not be modified.",
                    "Confirm New Relationship", MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                int uid = _session!.CreateRelationship(first.Id, second.Id,
                    p.Code, r.Code, f.Code, mentor);
                _lastSaved = DateTime.Now;
                ReloadRelationships(uid);
                SetStatus($"Created relationship UID {uid}.");
            }
            RefreshDirty();
        }
        catch (Exception ex) { Error(ex); }
    }

    private void DeleteRelationship_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedRelationship is not { } record || !ReadyForRelationshipWrite()) return;
        if (MessageBox.Show(this,
            $"Permanently delete relationship UID {record.Uid}?\n\n" +
            $"{record.Worker1Name} / {record.Worker2Name}\n\n" +
            "All four relationship elements on this record will be deleted. " +
            "Neither worker record will be deleted. " +
            "The operation cannot be undone without a database backup.",
            "Confirm Relationship Deletion", MessageBoxButton.YesNo,
            MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            _session!.DeleteRelationship(record);
            _lastSaved = DateTime.Now;
            ReloadRelationships();
            SetStatus($"Deleted relationship UID {record.Uid}.");
            RefreshDirty();
        }
        catch (Exception ex) { Error(ex); }
    }
}
