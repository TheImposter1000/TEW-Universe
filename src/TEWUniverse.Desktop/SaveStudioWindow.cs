using System.ComponentModel;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using TEWUniverse.Studio;

namespace TEWUniverse.Desktop;

/// <summary>
/// Embedded all-table editor inside the main TEW Universe Studio window.
/// Core workers, titles and company editing still use the existing modules.
/// </summary>
internal sealed class SaveTablesView : UserControl, IDisposable
{
    private readonly SaveGameSession _session;
    private readonly List<SaveTableEntry> _tables;
    private readonly ListBox _tableList = new();
    private readonly TextBox _tableSearch = new();
    private readonly ComboBox _categoryFilter = new();
    private readonly DataGrid _preview = new();
    private readonly TextBlock _tableDetails = new();
    private readonly TextBlock _status = new();
    private readonly Button _saveButton = new();
    private readonly Button _discardButton = new();
    private readonly Button _reloadButton = new();
    private readonly Button _nextPageButton = new();
    private readonly Button _previousPageButton = new();
    private readonly ComboBox _pageSizePicker = new();
    private readonly ComboBox _recordFieldPicker = new();
    private readonly TextBox _recordSearch = new();
    private readonly Button _applyRecordFilter = new();
    private readonly Button _clearRecordFilter = new();
    private readonly TextBlock _recordFilterSummary = new();
    private readonly TextBlock _pageLabel = new();
    private const int DefaultPageSize = 250;
    // 0 means load all records from the selected table.
    private int _pageSize = DefaultPageSize;
    private int _pageIndex;
    private int _totalRows;
    private DataTable? _loadedTable;
    internal bool HasPendingChanges => PendingChanges > 0;
    internal event Action? DirtyStateChanged;

    private sealed record SaveTableEntry(string Name, string Category)
    {
        private static readonly Dictionary<string, string> FriendlyNames =
            new(StringComparer.OrdinalIgnoreCase) {
                ["tblWorker"] = "Workers",
                ["tblWorkerSkill"] = "Wrestler skills",
                ["tblWorkerOver"] = "Worker popularity",
                ["tblFed"] = "Companies",
                ["tblContract"] = "Contracts",
                ["tblBelt"] = "Championships",
                ["tblBeltHistory"] = "Championship history",
                ["tblMatchLog"] = "Match results",
                ["tblGameInfo"] = "Game information",
                ["tblPlayerInfo"] = "Player information",
                ["tblStoryline"] = "Storylines"
            };

        public string DisplayName => FriendlyNames.TryGetValue(Name, out var title)
            ? title + "   ·   " + Name : Name;

        public override string ToString() => DisplayName;
    }

    internal SaveTablesView(TEWUniverse.Studio.DatabaseSession activeSave)
    {
        _session = SaveGameSession.Attach(activeSave);
        _tables = _session.TableNames
            .Select(name => new SaveTableEntry(name, SaveTableBrowserRules.Category(name)))
            .OrderBy(item => SaveTableBrowserRules.CategoryOrder(item.Category))
            .ThenBy(item => SaveTableBrowserRules.WorkerOrder(item.Name))
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList();

        Background = Brush("#091322");
        Foreground = Brushes.White;
        Content = BuildContent();
        RefreshTables();
    }

    private static SolidColorBrush Brush(string hex)
        => new((Color)ColorConverter.ConvertFromString(hex)!);

    private Style AppStyle(string name) => (Style)FindResource(name);

    private static Style GridHeaderStyle()
    {
        var style = new Style(typeof(DataGridColumnHeader));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brush("#1C3855")));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Brush("#EEF5FF")));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Brush("#375575")));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(11, 7, 11, 7)));
        style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 12d));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
        return style;
    }

    private static Style GridRowHeaderStyle()
    {
        var style = new Style(typeof(DataGridRowHeader));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brush("#172E48")));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Brush("#C8D9ED")));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Brush("#375575")));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 0, 8, 0)));
        return style;
    }

    private static Style GridCellStyle()
    {
        var style = new Style(typeof(DataGridCell));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Brush("#EFF6FF")));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Brush("#2C4865")));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 0)));
        var selected = new Trigger {
            Property = DataGridCell.IsSelectedProperty,
            Value = true
        };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, Brush("#1264B9")));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        style.Triggers.Add(selected);
        return style;
    }

    private static Style GridDisplayStyle()
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(10, 0, 10, 0)));
        style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        style.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.NoWrap));
        style.Setters.Add(new Setter(TextBlock.ForegroundProperty, Brush("#EFF6FF")));
        return style;
    }

    private static Style GridEditingStyle()
    {
        var style = new Style(typeof(TextBox));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brush("#0B263F")));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Brush("#50A0FA")));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(9, 4, 9, 4)));
        style.Setters.Add(new Setter(TextBox.CaretBrushProperty, Brushes.White));
        return style;
    }

    private static DataGridLength ColumnWidth(string name)
    {
        if (name.Equals("Name", StringComparison.OrdinalIgnoreCase)) return new(245);
        if (name.Equals("Picture", StringComparison.OrdinalIgnoreCase)) return new(200);
        if (name.Equals("Profile", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Description", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Biography", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Bio", StringComparison.OrdinalIgnoreCase))
            return new(400);
        if (name.Equals("UID", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("UID", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Fed", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Active", StringComparison.OrdinalIgnoreCase))
            return new(105);
        return new(172);
    }

    private UIElement BuildContent()
    {
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel { Margin = new Thickness(23, 17, 23, 15) };
        header.Children.Add(new TextBlock {
            Text = "Save Tables", FontSize = 26, FontWeight = FontWeights.Bold
        });
        header.Children.Add(new TextBlock {
            Text = "Workers and related records appear first. Use the Workers navigation for detailed individual profiles.",
            FontSize = 12, Foreground = Brush("#F5C96A"), Margin = new Thickness(0, 4, 0, 5)
        });
        header.Children.Add(new TextBlock {
            Text = System.IO.Path.GetFileName(_session.OriginalPath) +
                "  |  " + _tables.Count + " actual TEW data tables",
            FontSize = 12, Foreground = Brush("#A6BFDA")
        });
        Grid.SetRow(header, 0);
        layout.Children.Add(header);

        var workspace = new Grid { Margin = new Thickness(17, 0, 17, 12) };
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new DockPanel {
            Margin = new Thickness(0, 0, 12, 0), Background = Brush("#102137")
        };
        var picker = new StackPanel { Margin = new Thickness(14, 13, 14, 12) };
        picker.Children.Add(new TextBlock {
            Text = "SAVE DATA TABLES", FontSize = 13, FontWeight = FontWeights.Bold,
            Foreground = Brush("#AFCEF1"), Margin = new Thickness(0, 0, 0, 9)
        });
        picker.Children.Add(new TextBlock {
            Text = "Find a table",
            Foreground = Brush("#B1C6DF"),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 6)
        });
        _tableSearch.Style = AppStyle("SearchBox");
        _tableSearch.Height = 38;
        _tableSearch.Padding = new Thickness(9, 6, 9, 6);
        _tableSearch.Background = Brush("#0B192B");
        _tableSearch.Foreground = Brushes.White;
        _tableSearch.BorderBrush = Brush("#365878");
        _tableSearch.ToolTip = "Search by exact or partial table name";
        _tableSearch.TextChanged += (_, _) => RefreshTables();
        picker.Children.Add(_tableSearch);
        picker.Children.Add(new TextBlock {
            Text = "Category",
            Foreground = Brush("#B1C6DF"),
            FontSize = 12,
            Margin = new Thickness(0, 12, 0, 6)
        });
        _categoryFilter.Style = AppStyle("DarkComboBox");
        _categoryFilter.Background = Brush("#142B45");
        _categoryFilter.Foreground = Brushes.White;
        _categoryFilter.BorderBrush = Brush("#3C638B");
        _categoryFilter.Margin = new Thickness(0, 0, 0, 0);
        foreach (var category in new[] {
            "Workers", "All categories", "Current universe", "History and results",
            "Game and booking state", "Other save tables"
        })
            _categoryFilter.Items.Add(category);
        // Start on real worker records rather than tblBelt or MSys internals.
        _categoryFilter.SelectedIndex = _tables.Any(t => t.Category == "Workers") ? 0 : 1;
        _categoryFilter.SelectionChanged += (_, _) => RefreshTables();
        picker.Children.Add(_categoryFilter);
        DockPanel.SetDock(picker, Dock.Top);
        left.Children.Add(picker);

        _tableList.Background = Brushes.Transparent;
        _tableList.BorderThickness = new Thickness(0);
        _tableList.Foreground = Brushes.White;
        _tableList.SelectionChanged += (_, _) => LoadSelected();
        left.Children.Add(_tableList);
        Grid.SetColumn(left, 0);
        workspace.Children.Add(left);

        var right = new Grid { Background = Brush("#102137") };
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var details = new StackPanel { Margin = new Thickness(16, 13, 16, 11) };
        details.Children.Add(new TextBlock {
            Text = "Table records", FontSize = 19, FontWeight = FontWeights.SemiBold
        });
        _tableDetails.Text = "Select a save table to inspect its records.";
        _tableDetails.Margin = new Thickness(0, 6, 0, 0);
        _tableDetails.Foreground = Brush("#A6BFDA");
        _tableDetails.TextWrapping = TextWrapping.Wrap;
        details.Children.Add(_tableDetails);

        var controls = new StackPanel {
            Orientation = Orientation.Horizontal, Margin = new Thickness(0, 11, 0, 0)
        };
        _saveButton.Content = "Save Changes";
        _saveButton.Style = AppStyle("BlueButton");
        _saveButton.Padding = new Thickness(14, 9, 14, 9);
        _saveButton.Background = Brush("#176FDF");
        _saveButton.Foreground = Brushes.White;
        _saveButton.IsEnabled = false;
        _saveButton.Click += SaveChanges_Click;
        controls.Children.Add(_saveButton);

        _discardButton.Content = "Discard Changes";
        _discardButton.Style = AppStyle("ActionButton");
        _discardButton.Padding = new Thickness(14, 9, 14, 9);
        _discardButton.Margin = new Thickness(9, 0, 0, 0);
        _discardButton.IsEnabled = false;
        _discardButton.Click += (_, _) => LoadSelected(force: true);
        controls.Children.Add(_discardButton);

        _reloadButton.Content = "Refresh Table";
        _reloadButton.Style = AppStyle("ActionButton");
        _reloadButton.Padding = new Thickness(14, 9, 14, 9);
        _reloadButton.Margin = new Thickness(9, 0, 0, 0);
        _reloadButton.Click += (_, _) => LoadSelected(force: true);
        controls.Children.Add(_reloadButton);
        details.Children.Add(controls);

        var filter = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        filter.Children.Add(new TextBlock {
            Text = "Find save records by field or search all fields",
            FontSize = 12, Foreground = Brush("#B6CEE5"), Margin = new Thickness(0, 0, 0, 7)
        });
        var filters = new StackPanel { Orientation = Orientation.Horizontal };

        _recordFieldPicker.Style = AppStyle("DarkComboBox");
        _recordFieldPicker.Width = 185;
        _recordFieldPicker.Height = 34;
        _recordFieldPicker.Items.Add(SaveTableRecordFilter.AllColumns);
        _recordFieldPicker.SelectedIndex = 0;
        filters.Children.Add(_recordFieldPicker);

        _recordSearch.Style = AppStyle("SearchBox");
        _recordSearch.Width = 260;
        _recordSearch.Height = 34;
        _recordSearch.Margin = new Thickness(9, 0, 0, 0);
        _recordSearch.ToolTip = "Search across the complete save table, not only the current page";
        _recordSearch.KeyDown += (_, args) => {
            if (args.Key == System.Windows.Input.Key.Enter) ApplyRecordFilter();
        };
        filters.Children.Add(_recordSearch);

        _applyRecordFilter.Content = "Apply Filter";
        _applyRecordFilter.Style = AppStyle("BlueButton");
        _applyRecordFilter.Margin = new Thickness(9, 0, 0, 0);
        _applyRecordFilter.Padding = new Thickness(10, 6, 10, 6);
        _applyRecordFilter.Click += (_, _) => ApplyRecordFilter();
        filters.Children.Add(_applyRecordFilter);

        _clearRecordFilter.Content = "Clear";
        _clearRecordFilter.Style = AppStyle("ActionButton");
        _clearRecordFilter.Margin = new Thickness(8, 0, 0, 0);
        _clearRecordFilter.Padding = new Thickness(10, 6, 10, 6);
        _clearRecordFilter.Click += (_, _) => ClearRecordFilter();
        filters.Children.Add(_clearRecordFilter);
        filter.Children.Add(filters);
        _recordFilterSummary.Foreground = Brush("#A8C4DE");
        _recordFilterSummary.FontSize = 11;
        _recordFilterSummary.Margin = new Thickness(0, 5, 0, 0);
        _recordFilterSummary.Text = "Select a table to search its records.";
        filter.Children.Add(_recordFilterSummary);
        details.Children.Add(filter);

        var pager = new StackPanel {
            Orientation = Orientation.Horizontal, Margin = new Thickness(0, 11, 0, 0)
        };
        pager.Children.Add(new TextBlock {
            Text = "Show records", VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush("#BCD3E8"), Margin = new Thickness(0, 0, 9, 0)
        });
        _pageSizePicker.Style = AppStyle("DarkComboBox");
        _pageSizePicker.Width = 120;
        _pageSizePicker.Height = 33;
        foreach (var option in new[] { "100", "250", "500", "1000", "All Records" })
            _pageSizePicker.Items.Add(option);
        _pageSizePicker.SelectedItem = "250";
        _pageSizePicker.SelectionChanged += (_, _) => ChangePageSize();
        pager.Children.Add(_pageSizePicker);

        _previousPageButton.Margin = new Thickness(12, 0, 0, 0);
        _previousPageButton.Content = "Previous";
        _previousPageButton.Style = AppStyle("ActionButton");
        _previousPageButton.Padding = new Thickness(10, 6, 10, 6);
        _previousPageButton.Click += (_, _) => ChangePage(-1);
        pager.Children.Add(_previousPageButton);

        _pageLabel.Text = "No records";
        _pageLabel.Foreground = Brush("#C9DCEF");
        _pageLabel.VerticalAlignment = VerticalAlignment.Center;
        _pageLabel.Margin = new Thickness(13, 0, 13, 0);
        pager.Children.Add(_pageLabel);

        _nextPageButton.Content = "Next";
        _nextPageButton.Style = AppStyle("ActionButton");
        _nextPageButton.Padding = new Thickness(10, 6, 10, 6);
        _nextPageButton.Click += (_, _) => ChangePage(1);
        pager.Children.Add(_nextPageButton);
        details.Children.Add(pager);

        Grid.SetRow(details, 0);
        right.Children.Add(details);

        _preview.AutoGenerateColumns = true;
        _preview.IsReadOnly = false;
        _preview.FontFamily = new FontFamily("Segoe UI");
        _preview.FontSize = 13;
        _preview.RowHeight = 34;
        _preview.ColumnHeaderHeight = 38;
        _preview.RowHeaderWidth = 38;
        _preview.ColumnHeaderStyle = GridHeaderStyle();
        _preview.RowHeaderStyle = GridRowHeaderStyle();
        _preview.CellStyle = GridCellStyle();
        _preview.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _preview.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _preview.CanUserAddRows = false;
        _preview.CanUserDeleteRows = false;
        _preview.EnableRowVirtualization = true;
        _preview.EnableColumnVirtualization = true;
        _preview.HeadersVisibility = DataGridHeadersVisibility.All;
        _preview.Background = Brush("#102137");
        _preview.Foreground = Brushes.White;
        _preview.RowBackground = Brush("#13283F");
        _preview.AlternatingRowBackground = Brush("#1B334C");
        _preview.BorderThickness = new Thickness(0);
        _preview.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
        _preview.HorizontalGridLinesBrush = Brush("#30465C");
        _preview.AutoGeneratingColumn += (_, e) =>
        {
            var writable = _sessionCanEdit(e.PropertyName);
            var binding = new Binding("[" + e.PropertyName + "]") {
                Mode = writable ? BindingMode.TwoWay : BindingMode.OneWay,
                UpdateSourceTrigger = UpdateSourceTrigger.LostFocus
            };
            var choices = _loadedTable == null
                ? Array.Empty<TewCodeChoice>()
                : TewCodeLabels.Choices(_loadedTable.TableName, e.PropertyName).ToArray();

            if (choices.Length > 0)
            {
                // Use the exact same verified numeric code catalog as the
                // detailed TEW Universe editor. Never write display labels
                // into the underlying numeric MDB field.
                var displayed = choices.ToList();
                foreach (DataRow row in _loadedTable!.Rows)
                {
                    if (!TewCodeLabels.TryGetCode(Convert.ToString(
                        row[e.PropertyName], System.Globalization.CultureInfo.InvariantCulture),
                        out int stored) || displayed.Any(c => c.Code == stored))
                        continue;
                    displayed.Add(new TewCodeChoice(stored, "Unmapped code " + stored));
                }
                var comboStyle = AppStyle("DarkComboBox");
                e.Column = new DataGridComboBoxColumn {
                    Header = writable ? e.PropertyName + "  ✎" : e.PropertyName,
                    ItemsSource = displayed,
                    SelectedValuePath = nameof(TewCodeChoice.Code),
                    DisplayMemberPath = nameof(TewCodeChoice.Label),
                    SelectedValueBinding = binding,
                    ElementStyle = comboStyle,
                    EditingElementStyle = comboStyle,
                    Width = ColumnWidth(e.PropertyName),
                    MinWidth = 105,
                    MaxWidth = 450,
                    IsReadOnly = !writable
                };
            }
            else
                e.Column = new DataGridTextColumn {
                    Header = writable ? e.PropertyName + "  ✎" : e.PropertyName,
                    Binding = binding,
                    ElementStyle = GridDisplayStyle(),
                    EditingElementStyle = GridEditingStyle(),
                    Width = ColumnWidth(e.PropertyName),
                    MinWidth = 85,
                    MaxWidth = 450,
                    IsReadOnly = !writable
                };
        };
        _preview.CellEditEnding += (_, _) =>
            Dispatcher.BeginInvoke(new Action(UpdateDirtyState));
        _preview.CurrentCellChanged += (_, _) => UpdateDirtyState();
        Grid.SetRow(_preview, 1);
        right.Children.Add(_preview);
        Grid.SetColumn(right, 1);
        workspace.Children.Add(right);

        Grid.SetRow(workspace, 1);
        layout.Children.Add(workspace);

        var footer = new Border {
            Background = Brush("#102137"), Padding = new Thickness(20, 9, 20, 9)
        };
        _status.Text = "No data loaded.";
        _status.FontSize = 12;
        _status.Foreground = Brush("#A6BFDA");
        footer.Child = _status;
        Grid.SetRow(footer, 2);
        layout.Children.Add(footer);
        return layout;
    }

    private bool _sessionCanEdit(string columnName)
        => _loadedTable?.Columns.Contains(columnName) == true
            && SaveGameSession.CanEdit(_loadedTable, _loadedTable.Columns[columnName]!);

    private int PendingChanges =>
        _loadedTable == null ? 0 : _session.CountPendingEdits(_loadedTable);

    private void UpdateDirtyState()
    {
        var count = PendingChanges;
        _saveButton.IsEnabled = count > 0;
        _discardButton.IsEnabled = count > 0;
        // Do not silently lose staged edits by browsing to another table.
        _tableList.IsEnabled = count == 0;
        _tableSearch.IsEnabled = count == 0;
        _categoryFilter.IsEnabled = count == 0;
        _pageSizePicker.IsEnabled = count == 0;
        _recordFieldPicker.IsEnabled = count == 0;
        _recordSearch.IsEnabled = count == 0;
        _applyRecordFilter.IsEnabled = count == 0;
        _clearRecordFilter.IsEnabled = count == 0;
        _previousPageButton.IsEnabled = count == 0 &&
            _pageSize != 0 && _pageIndex > 0;
        _nextPageButton.IsEnabled = count == 0 && _pageSize != 0 &&
            (_pageIndex + 1) * _pageSize < _totalRows;
        if (count > 0)
            _status.Text = count + " pending field changes. Save or discard before switching tables.";
        DirtyStateChanged?.Invoke();
    }

    private void RefreshTables()
    {
        if (PendingChanges > 0) return;
        var query = _tableSearch.Text.Trim();
        var category = _categoryFilter.SelectedItem as string ?? "All categories";
        var previous = (_tableList.SelectedItem as SaveTableEntry)?.Name;

        var filtered = _tables.Where(table =>
            (query.Length == 0 ||
                table.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                table.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)) &&
            (category == "All categories" || table.Category == category)).ToList();

        _tableList.ItemsSource = filtered;
        _tableList.SelectedItem = filtered.FirstOrDefault(item =>
            string.Equals(item.Name, previous, StringComparison.OrdinalIgnoreCase))
            ?? filtered.FirstOrDefault(item =>
                item.Name.Equals("tblWorker", StringComparison.OrdinalIgnoreCase))
            ?? filtered.FirstOrDefault();
        var workerTables = _tables.Count(t => t.Category == "Workers");
        _status.Text = filtered.Count + " of " + _tables.Count +
            " TEW tables  |  " + workerTables + " worker-related tables  |  Direct edits require Save Changes.";
    }

    private void LoadSelected() => LoadSelected(force: false);

    private void ChangePageSize()
    {
        if (PendingChanges > 0) return;
        var choice = _pageSizePicker.SelectedItem as string ?? "250";
        var selected = choice == "All Records" ? 0 :
            int.TryParse(choice, out var size) ? size : DefaultPageSize;
        if (_pageSize == selected) return;
        _pageSize = selected;
        _pageIndex = 0;
        if (_pageSize != 0) _recordSearch.Text = "";
        LoadSelected(force: false);
    }

    private void ApplyRecordFilter()
    {
        if (_loadedTable == null || PendingChanges > 0) return;
        if (_recordSearch.Text.Trim().Length > 0 && _pageSize != 0)
        {
            // Always search the whole save table. Never silently search
            // only the currently visible 250 records.
            _pageSizePicker.SelectedItem = "All Records";
            return; // ChangePageSize performs the complete reload and filter.
        }
        ApplyFilterToView();
    }

    private void ClearRecordFilter()
    {
        if (_loadedTable == null || PendingChanges > 0) return;
        _recordSearch.Text = "";
        _recordFieldPicker.SelectedIndex = 0;
        _loadedTable.DefaultView.RowFilter = "";
        _recordFilterSummary.Text = "Showing " + _loadedTable.DefaultView.Count.ToString("N0") +
            " records. Search all fields or select a specific field.";
    }

    private void RebuildRecordFieldChoices(DataTable data)
    {
        _recordFieldPicker.Items.Clear();
        _recordFieldPicker.Items.Add(SaveTableRecordFilter.AllColumns);
        foreach (DataColumn column in data.Columns)
            if (SaveEditRules.IsSupportedScalarType(column.DataType))
                _recordFieldPicker.Items.Add(column.ColumnName);
        _recordFieldPicker.SelectedIndex = 0;
    }

    private void ApplyFilterToView()
    {
        if (_loadedTable == null) return;
        try
        {
            var column = _recordFieldPicker.SelectedItem as string ??
                SaveTableRecordFilter.AllColumns;
            _loadedTable.DefaultView.RowFilter = SaveTableRecordFilter.Build(
                _loadedTable, column, _recordSearch.Text);
            _recordFilterSummary.Text = "Matching " +
                _loadedTable.DefaultView.Count.ToString("N0") + " of " +
                _totalRows.ToString("N0") + " records in " +
                _loadedTable.TableName + ".";
        }
        catch (Exception ex) when (ex is EvaluateException or SyntaxErrorException
            or ArgumentException or InvalidOperationException)
        {
            _loadedTable.DefaultView.RowFilter = "";
            _recordFilterSummary.Text = "Filter cannot be applied: " + ex.Message;
        }
    }

    private void ChangePage(int delta)
    {
        if (PendingChanges > 0 || _loadedTable == null) return;
        var pages = _pageSize == 0 ? 1 :
            Math.Max(1, (_totalRows + _pageSize - 1) / _pageSize);
        var next = Math.Clamp(_pageIndex + delta, 0, pages - 1);
        if (next == _pageIndex) return;
        _pageIndex = next;
        LoadSelected(force: false);
    }

    private void LoadSelected(bool force)
    {
        if (_tableList.SelectedItem is not SaveTableEntry item)
        {
            if (PendingChanges == 0)
            {
                _preview.ItemsSource = null;
                _tableDetails.Text = "No matching table selected.";
            }
            return;
        }
        if (PendingChanges > 0 && !force)
            return;
        if (PendingChanges > 0 &&
            MessageBox.Show(Window.GetWindow(this), "Discard all pending edits to this table?",
                "Discard changes", MessageBoxButton.YesNo, MessageBoxImage.Warning)
            != MessageBoxResult.Yes)
            return;

        try
        {
            var changingTable = _loadedTable == null ||
                !_loadedTable.TableName.Equals(item.Name, StringComparison.OrdinalIgnoreCase);
            if (changingTable)
            {
                _pageIndex = 0;
                _recordSearch.Text = "";
            }

            _totalRows = _session.CountRows(item.Name);
            var maxPage = _pageSize == 0 ? 0 :
                Math.Max(0, (_totalRows - 1) / _pageSize);
            _pageIndex = Math.Min(_pageIndex, maxPage);

            var data = _session.Preview(item.Name, _pageIndex, _pageSize);
            if (_loadedTable != null) _loadedTable.RowChanged -= LoadedTable_RowChanged;
            _loadedTable = data;
            _loadedTable.RowChanged += LoadedTable_RowChanged;
            _preview.ItemsSource = data.DefaultView;
            if (changingTable)
                RebuildRecordFieldChoices(data);
            ApplyFilterToView();
            var editable = data.Columns.Cast<DataColumn>()
                .Count(column => SaveGameSession.CanEdit(data, column));
            var first = _totalRows == 0 ? 0 :
                _pageSize == 0 ? 1 : _pageIndex * _pageSize + 1;
            var last = _pageSize == 0 ? _totalRows :
                Math.Min((_pageIndex + 1) * _pageSize, _totalRows);
            _pageLabel.Text = _pageSize == 0
                ? "All " + _totalRows.ToString("N0") + " records"
                : first.ToString("N0") + " to " +
                    last.ToString("N0") + " of " + _totalRows.ToString("N0");

            _tableDetails.Text = item.Name + "  |  " + _session.TableType(item.Name) +
                "  |  " + item.Category + "  |  " +
                data.Columns.Count + " columns  |  " +
                (editable > 0
                    ? editable + " editable fields (marked with a pen)"
                    : "Read-only: no unique record key or editable scalar fields");
            _status.Text = editable > 0
                ? (_pageSize == 0 ? "All records loaded. " : "") +
                    "Change a pen-marked field and choose Save Changes to update the save."
                : "This table is view-only because its field types or row identity are not verified.";
            UpdateDirtyState();
        }
        catch (Exception ex)
        {
            _preview.ItemsSource = null;
            _loadedTable = null;
            _totalRows = 0;
            _pageLabel.Text = "No records";
            _tableDetails.Text = item.Name + " could not be loaded.";
            _status.Text = ex.Message;
        }
    }

    private void LoadedTable_RowChanged(object? sender, DataRowChangeEventArgs e)
        => Dispatcher.BeginInvoke(new Action(UpdateDirtyState));

    private void SaveChanges_Click(object sender, RoutedEventArgs e) => SavePendingChanges();

    internal void SavePendingChanges()
    {
        if (_loadedTable == null) return;
        _preview.CommitEdit(DataGridEditingUnit.Cell, true);
        _preview.CommitEdit(DataGridEditingUnit.Row, true);
        var changes = PendingChanges;
        if (changes == 0) return;

        if (MessageBox.Show(Window.GetWindow(this),
                "Save " + changes + " changed fields directly into:\n\n" +
                _session.OriginalPath +
                "\n\nTEW IX must be closed. These edits will change the original game save. " +
                "Changing a valid Access field can still affect game rules or linked records.",
                "Confirm changes to active save", MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            var committed = _session.SaveChanges(_loadedTable);
            UpdateDirtyState();
            _status.Text = "Saved " + committed +
                " fields directly to the TEW IX save. No working copy was created.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message, "Save failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Called before leaving the embedded table workspace, loading another MDB
    // or exiting Studio. Pending edits are never silently abandoned.
    internal bool MayLeave()
    {
        _preview.CommitEdit(DataGridEditingUnit.Cell, true);
        _preview.CommitEdit(DataGridEditingUnit.Row, true);
        if (PendingChanges == 0) return true;
        var discard = MessageBox.Show(Window.GetWindow(this),
            "Discard unsaved save table changes?",
            "Unsaved changes", MessageBoxButton.YesNo, MessageBoxImage.Warning)
            == MessageBoxResult.Yes;
        if (discard && _loadedTable != null)
        {
            _loadedTable.RejectChanges();
            UpdateDirtyState();
        }
        return discard;
    }

    public void Dispose()
    {
        DirtyStateChanged = null;
        _session.Dispose();
    }
}
