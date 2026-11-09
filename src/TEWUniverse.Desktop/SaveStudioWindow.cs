using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TEWUniverse.Desktop;

/// <summary>
/// Dedicated Save Studio workspace. No edit, save, SQL, or import controls exist.
/// </summary>
internal sealed class SaveStudioWindow : Window
{
    private readonly SaveGameSession _session;
    private readonly List<SaveTableEntry> _tables;
    private readonly ListBox _tableList = new();
    private readonly TextBox _tableSearch = new();
    private readonly ComboBox _categoryFilter = new();
    private readonly DataGrid _preview = new();
    private readonly TextBlock _tableDetails = new();
    private readonly TextBlock _status = new();

    private sealed record SaveTableEntry(string Name, string Category)
    {
        public override string ToString() => Name;
    }

    internal SaveStudioWindow(string sourcePath, string password)
    {
        _session = SaveGameSession.OpenReadOnly(sourcePath, password);
        _tables = _session.TableNames
            .Select(name => new SaveTableEntry(name, CategoryFor(name)))
            .OrderBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList();

        Title = "TEW Universe Studio | Save Studio (Read Only)";
        Width = 1360;
        Height = 850;
        MinWidth = 900;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("#091322");
        Foreground = Brushes.White;
        Content = BuildContent();
        Closed += (_, _) => _session.Dispose();
        RefreshTables();
    }

    private static SolidColorBrush Brush(string hex)
        => new((Color)ColorConverter.ConvertFromString(hex)!);

    private static string CategoryFor(string name)
    {
        if (name.Contains("History", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("Log", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("LogCompetitor", StringComparison.OrdinalIgnoreCase))
            return "History and results";
        if (name.StartsWith("tblGame", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("tblPlayer", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("tblUser", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("tblPending", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("tblAI", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Booking", StringComparison.OrdinalIgnoreCase))
            return "Game and booking state";
        if (name.StartsWith("tblWorker", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("tblFed", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("tblBelt", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("tblContract", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("tblStoryline", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("tblTeam", StringComparison.OrdinalIgnoreCase))
            return "Current universe";
        return "Other save tables";
    }

    private UIElement BuildContent()
    {
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel { Margin = new Thickness(23, 17, 23, 15) };
        header.Children.Add(new TextBlock {
            Text = "Save Studio", FontSize = 26, FontWeight = FontWeights.Bold
        });
        header.Children.Add(new TextBlock {
            Text = "READ ONLY  |  Disposable snapshot  |  Original save never edited",
            FontSize = 12, Foreground = Brush("#65D5B6"), Margin = new Thickness(0, 4, 0, 5)
        });
        header.Children.Add(new TextBlock {
            Text = System.IO.Path.GetFileName(_session.OriginalPath) +
                "  |  " + _tables.Count + " discovered tables",
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
            Text = "SAVE TABLES", FontSize = 13, FontWeight = FontWeights.Bold,
            Foreground = Brush("#AFCEF1"), Margin = new Thickness(0, 0, 0, 9)
        });
        _tableSearch.Height = 36;
        _tableSearch.Padding = new Thickness(9, 6, 9, 6);
        _tableSearch.Background = Brush("#0B192B");
        _tableSearch.Foreground = Brushes.White;
        _tableSearch.BorderBrush = Brush("#365878");
        _tableSearch.ToolTip = "Search by exact or partial table name";
        _tableSearch.TextChanged += (_, _) => RefreshTables();
        picker.Children.Add(_tableSearch);
        _categoryFilter.Margin = new Thickness(0, 10, 0, 0);
        foreach (var category in new[] {
            "All categories", "Current universe", "History and results",
            "Game and booking state", "Other save tables"
        })
            _categoryFilter.Items.Add(category);
        _categoryFilter.SelectedIndex = 0;
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
            Text = "Table preview", FontSize = 19, FontWeight = FontWeights.SemiBold
        });
        _tableDetails.Text = "Select a save table to inspect its records.";
        _tableDetails.Margin = new Thickness(0, 6, 0, 0);
        _tableDetails.Foreground = Brush("#A6BFDA");
        _tableDetails.TextWrapping = TextWrapping.Wrap;
        details.Children.Add(_tableDetails);
        Grid.SetRow(details, 0);
        right.Children.Add(details);

        _preview.AutoGenerateColumns = true;
        _preview.IsReadOnly = true;
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
        _preview.AutoGeneratingColumn += (_, e) => {
            if (e.Column is DataGridTextColumn column)
                column.Binding = new System.Windows.Data.Binding("[" + e.PropertyName + "]") {
                    Mode = System.Windows.Data.BindingMode.OneWay
                };
        };
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

    private void RefreshTables()
    {
        var query = _tableSearch.Text.Trim();
        var category = _categoryFilter.SelectedItem as string ?? "All categories";
        var previous = (_tableList.SelectedItem as SaveTableEntry)?.Name;

        var filtered = _tables.Where(table =>
            (query.Length == 0 || table.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) &&
            (category == "All categories" || table.Category == category)).ToList();

        _tableList.ItemsSource = filtered;
        _tableList.SelectedItem = filtered.FirstOrDefault(item =>
            string.Equals(item.Name, previous, StringComparison.OrdinalIgnoreCase))
            ?? filtered.FirstOrDefault();
        _status.Text = filtered.Count + " of " + _tables.Count +
            " tables  |  All data is read from a temporary snapshot.";
    }

    private void LoadSelected()
    {
        if (_tableList.SelectedItem is not SaveTableEntry item)
        {
            _preview.ItemsSource = null;
            _tableDetails.Text = "No matching table selected.";
            return;
        }

        try
        {
            var data = _session.Preview(item.Name);
            _preview.ItemsSource = data.DefaultView;
            _tableDetails.Text = item.Name + "  |  " + item.Category + "  |  " +
                data.Columns.Count + " columns  |  Showing up to 250 rows";
            _status.Text = "Loaded " + data.Rows.Count +
                " preview rows. Save data cannot be modified from this window.";
        }
        catch (Exception ex)
        {
            _preview.ItemsSource = null;
            _tableDetails.Text = item.Name + " could not be previewed.";
            _status.Text = ex.Message;
        }
    }
}
