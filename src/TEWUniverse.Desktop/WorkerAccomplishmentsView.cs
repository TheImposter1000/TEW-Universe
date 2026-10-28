using System.Windows;
using System.Windows.Controls;
using TEWUniverse.Studio;

namespace TEWUniverse.Desktop;

/// <summary>
/// Manually curated worker achievements. These are NOT TEW IX award or
/// championship records and never modify the opened MDB.
/// </summary>
public partial class MainWindow
{
    private readonly WorkerAccomplishmentStore _accomplishmentStore = new();
    private DataGrid _accomplishmentGrid = null!;
    private TextBlock _accomplishmentSummary = null!;
    private TextBox _accomplishmentTitle = null!;
    private TextBox _accomplishmentYear = null!;
    private TextBox _accomplishmentNotes = null!;
    private ComboBox _accomplishmentCategory = null!;
    private Button _accomplishmentAdd = null!;
    private Button _accomplishmentSave = null!;
    private Button _accomplishmentRemove = null!;
    private WorkerAccomplishment? _selectedAccomplishment;
    private bool _settingAccomplishmentForm;

    private void InitializeAccomplishmentsUI()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AccomplishmentsHost.Children.Add(grid);
        var left = new Border {
            Style = (Style)FindResource("CardBorder"),
            Padding = new Thickness(14), Margin = new Thickness(0, 0, 10, 0)
        };
        Grid.SetColumn(left, 0);
        grid.Children.Add(left);
        var listing = new DockPanel();
        left.Child = listing;
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(header, Dock.Top);
        listing.Children.Add(header);
        header.Children.Add(ExtraLabel("Accomplishments", true));
        header.Children.Add(new TextBlock {
            Text = "Custom career achievements saved only by Studio. " +
                "Championships, awards, tournaments and milestones are separate " +
                "from the TEW IX database.",
            Foreground = ExtraColor("#B9D2E8"), TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 10)
        });
        _accomplishmentSummary = ExtraLabel("Select a worker.");
        _accomplishmentSummary.TextWrapping = TextWrapping.Wrap;
        header.Children.Add(_accomplishmentSummary);
        _accomplishmentGrid = ExtraGrid();
        TextColumn(_accomplishmentGrid, "Achievement", "Title",
            new DataGridLength(1, DataGridLengthUnitType.Star));
        TextColumn(_accomplishmentGrid, "Type / Year", "Summary",
            new DataGridLength(0.8, DataGridLengthUnitType.Star));
        _accomplishmentGrid.SelectionChanged += (_, _) => {
            if (_settingAccomplishmentForm) return;
            var selected = _accomplishmentGrid.SelectedItem as WorkerAccomplishment;
            if (HasPendingAccomplishmentEdits() &&
                !ReferenceEquals(selected, _selectedAccomplishment) &&
                !ConfirmDiscardAccomplishmentEdits())
            {
                _settingAccomplishmentForm = true;
                try { _accomplishmentGrid.SelectedItem = _selectedAccomplishment; }
                finally { _settingAccomplishmentForm = false; }
                return;
            }
            ShowAccomplishment(selected);
        };
        listing.Children.Add(_accomplishmentGrid);

        var right = new Border {
            Style = (Style)FindResource("CardBorder"), Padding = new Thickness(18)
        };
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility =
            ScrollBarVisibility.Auto };
        right.Child = scroll;
        var form = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
        scroll.Content = form;
        form.Children.Add(ExtraLabel("Worker Career Record", true));
        form.Children.Add(new TextBlock {
            Text = "These entries exist only in a local Studio companion JSON file " +
                "for this scenario and worker UID. They do not update championship " +
                "lineage, TEW awards, worker biographies or TEW IX gameplay.",
            TextWrapping = TextWrapping.Wrap, Foreground = ExtraColor("#A9C6E3"),
            Margin = new Thickness(0, 6, 0, 15)
        });
        form.Children.Add(ExtraLabel("Category"));
        _accomplishmentCategory = ExtraPicker();
        _accomplishmentCategory.ItemsSource = WorkerAccomplishmentStore.Categories;
        form.Children.Add(_accomplishmentCategory);
        form.Children.Add(ExtraLabel("Achievement name"));
        _accomplishmentTitle = ExtraTextBox();
        _accomplishmentTitle.ToolTip = "Example: World Tag Team Championship, Wrestler of the Year";
        form.Children.Add(_accomplishmentTitle);
        form.Children.Add(ExtraLabel("Year (optional, 1800–2100)"));
        _accomplishmentYear = ExtraTextBox();
        form.Children.Add(_accomplishmentYear);
        form.Children.Add(ExtraLabel("Notes, reign count, partners, context (optional)"));
        _accomplishmentNotes = ExtraTextBox();
        _accomplishmentNotes.AcceptsReturn = true;
        _accomplishmentNotes.TextWrapping = TextWrapping.Wrap;
        _accomplishmentNotes.VerticalContentAlignment = VerticalAlignment.Top;
        _accomplishmentNotes.MinHeight = 110;
        form.Children.Add(_accomplishmentNotes);

        _accomplishmentAdd = ExtraButton("+ Add Accomplishment", true,
            AddAccomplishment_Click);
        _accomplishmentSave = ExtraButton("Save Selected Accomplishment", false,
            SaveAccomplishment_Click);
        _accomplishmentRemove = ExtraButton("Remove Selected", false,
            RemoveAccomplishment_Click);
        var newButton = ExtraButton("New / Clear Form", false, (_, _) => {
            if (!ConfirmDiscardAccomplishmentEdits()) return;
            _settingAccomplishmentForm = true;
            try { _accomplishmentGrid.SelectedItem = null; }
            finally { _settingAccomplishmentForm = false; }
            ShowAccomplishment(null);
        });
        form.Children.Add(newButton);
        form.Children.Add(_accomplishmentAdd);
        form.Children.Add(_accomplishmentSave);
        form.Children.Add(_accomplishmentRemove);

        _accomplishmentCategory.SelectionChanged += (_, _) => AccomplishmentInputChanged();
        _accomplishmentTitle.TextChanged += (_, _) => AccomplishmentInputChanged();
        _accomplishmentYear.TextChanged += (_, _) => AccomplishmentInputChanged();
        _accomplishmentNotes.TextChanged += (_, _) => AccomplishmentInputChanged();
        ShowAccomplishment(null);
    }

    private string CurrentAccomplishmentCategory() =>
        _accomplishmentCategory?.SelectedItem as string ?? "";

    private bool HasPendingAccomplishmentEdits()
    {
        if (_settingAccomplishmentForm || _worker == null ||
            _accomplishmentTitle == null) return false;
        string name = _accomplishmentTitle.Text.Trim();
        string year = _accomplishmentYear.Text.Trim();
        string notes = _accomplishmentNotes.Text.Trim();
        if (_selectedAccomplishment == null)
            return name.Length > 0 || year.Length > 0 || notes.Length > 0;
        return name != _selectedAccomplishment.Title ||
            year != _selectedAccomplishment.Year ||
            notes != _selectedAccomplishment.Notes ||
            CurrentAccomplishmentCategory() != _selectedAccomplishment.Category;
    }

    private bool ConfirmDiscardAccomplishmentEdits() =>
        !HasPendingAccomplishmentEdits() ||
        MessageBox.Show(this, "Discard unsaved Studio-only accomplishment changes?",
            "Unsaved Accomplishment", MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;

    private void AccomplishmentInputChanged()
    {
        if (_settingAccomplishmentForm) return;
        UpdateAccomplishmentButtons();
        RefreshDirty();
    }

    private void UpdateAccomplishmentButtons()
    {
        if (_accomplishmentAdd == null) return;
        bool hasWorker = _worker != null && _session != null;
        bool pending = HasPendingAccomplishmentEdits();
        _accomplishmentAdd.IsEnabled = hasWorker &&
            _selectedAccomplishment == null && _accomplishmentTitle.Text.Trim().Length > 0;
        _accomplishmentSave.IsEnabled = hasWorker &&
            _selectedAccomplishment != null && pending;
        _accomplishmentRemove.IsEnabled = hasWorker &&
            _selectedAccomplishment != null;
    }

    private void ShowAccomplishment(WorkerAccomplishment? entry)
    {
        if (_accomplishmentTitle == null) return;
        _settingAccomplishmentForm = true;
        try
        {
            _selectedAccomplishment = entry;
            _accomplishmentCategory.SelectedItem =
                entry?.Category ?? WorkerAccomplishmentStore.Categories[0];
            _accomplishmentTitle.Text = entry?.Title ?? "";
            _accomplishmentYear.Text = entry?.Year ?? "";
            _accomplishmentNotes.Text = entry?.Notes ?? "";
        }
        finally { _settingAccomplishmentForm = false; }
        UpdateAccomplishmentButtons();
        RefreshDirty();
    }

    private void RefreshAccomplishments(Guid? selectedId = null)
    {
        if (_accomplishmentGrid == null) return;
        try
        {
            var entries = _session == null || _worker == null
                ? Array.Empty<WorkerAccomplishment>()
                : _accomplishmentStore.List(_session.PathOnDisk, _worker.Id).ToArray();
            _settingAccomplishmentForm = true;
            try
            {
                _accomplishmentGrid.ItemsSource = entries;
                _accomplishmentGrid.SelectedItem = selectedId.HasValue
                    ? entries.FirstOrDefault(x => x.Id == selectedId.Value) : null;
                _accomplishmentSummary.Text = _worker == null
                    ? "Select a worker." : $"{entries.Length} custom entries for {_worker.Name}.";
            }
            finally { _settingAccomplishmentForm = false; }
            ShowAccomplishment(_accomplishmentGrid.SelectedItem as WorkerAccomplishment);
        }
        catch (Exception ex) { Error(ex); }
    }

    private void AddAccomplishment_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null || _worker == null || _selectedAccomplishment != null) return;
        try
        {
            var item = _accomplishmentStore.Add(_session.PathOnDisk, _worker.Id,
                CurrentAccomplishmentCategory(), _accomplishmentTitle.Text,
                _accomplishmentYear.Text, _accomplishmentNotes.Text);
            RefreshAccomplishments(item.Id);
            _lastSaved = DateTime.Now;
            SetStatus("Added Studio-only accomplishment. TEW IX MDB unchanged.");
            RefreshDirty();
        }
        catch (Exception ex) { Error(ex); }
    }

    private void SaveAccomplishment_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null || _worker == null || _selectedAccomplishment == null) return;
        try
        {
            var item = _accomplishmentStore.Update(_session.PathOnDisk, _worker.Id,
                _selectedAccomplishment.Id, CurrentAccomplishmentCategory(),
                _accomplishmentTitle.Text, _accomplishmentYear.Text,
                _accomplishmentNotes.Text);
            RefreshAccomplishments(item.Id);
            _lastSaved = DateTime.Now;
            SetStatus("Saved Studio-only accomplishment. TEW IX MDB unchanged.");
            RefreshDirty();
        }
        catch (Exception ex) { Error(ex); }
    }

    private void RemoveAccomplishment_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null || _worker == null || _selectedAccomplishment == null) return;
        if (MessageBox.Show(this,
            $"Remove '{_selectedAccomplishment.Title}' from Studio's custom accomplishments? " +
            "The TEW IX database will not be touched.",
            "Remove Studio Accomplishment", MessageBoxButton.YesNo,
            MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            _accomplishmentStore.Remove(_session.PathOnDisk, _worker.Id,
                _selectedAccomplishment.Id);
            RefreshAccomplishments();
            _lastSaved = DateTime.Now;
            SetStatus("Removed Studio-only accomplishment. TEW IX MDB unchanged.");
            RefreshDirty();
        }
        catch (Exception ex) { Error(ex); }
    }
}
