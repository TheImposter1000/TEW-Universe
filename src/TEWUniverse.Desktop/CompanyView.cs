using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TEWUniverse.Studio;

namespace TEWUniverse.Desktop;

public partial class MainWindow
{
    private readonly Dictionary<FieldDraft, int> _companyFieldRecordIds = new();

    private void BuildCompanyEditor(RecordSection record)
    {
        _companyFieldRecordIds.Clear();
        ExistingFieldsPanel.Children.Clear();
        DataRow row = record.Data.Rows[0];
        var sections = new Dictionary<string, List<(DataColumn, object)>>(
            StringComparer.OrdinalIgnoreCase);
        string[] order = {
            "Identity and presentation", "Operating history and dates",
            "Location and reach", "Business and finances",
            "Product and presentation settings", "Personnel and policies",
            "Other TEW IX company settings"
        };

        foreach (DataColumn column in record.Data.Columns)
        {
            string group = CompanyFieldRules.Group(column.ColumnName);
            if (!sections.TryGetValue(group, out var list))
                sections[group] = list = new List<(DataColumn, object)>();
            list.Add((column, row[column]));
        }

        var intro = new TextBlock {
            Text = "All columns available in this company's tblFed record are " +
                "shown below, grouped by function. Scalar settings are editable; " +
                "record IDs and unverified linked references are visible but protected. " +
                "Other company tables are displayed after the main record.",
            Foreground = Brushes.LightBlue,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 14),
            TextWrapping = TextWrapping.Wrap
        };
        ExistingFieldsPanel.Children.Add(intro);

        foreach (string category in order)
        {
            if (!sections.TryGetValue(category, out var fields)) continue;
            var panel = CreateCompanySection(
                category + " (" + fields.Count.ToString(CultureInfo.InvariantCulture) + ")",
                category is "Identity and presentation" or
                    "Operating history and dates" or "Business and finances");
            AddCompanyCards(panel, fields.Select(pair => (
                pair.Item1, pair.Item2, "tblFed", "UID", _selectedExisting!.Id)));
        }

        if (_session == null || _selectedExisting == null) return;
        var related = _session.ReadCompanyRelated(_selectedExisting.Id);
        int available = related.Count(x => x.Rows.Rows.Count > 0);
        ExistingFieldsPanel.Children.Add(new TextBlock {
            Text = "LINKED COMPANY RECORDS  |  " + available + " data sections",
            FontSize = 16, FontWeight = FontWeights.Bold,
            Foreground = Brushes.White, Margin = new Thickness(0, 22, 0, 8)
        });
        ExistingFieldsPanel.Children.Add(new TextBlock {
            Text = "These are actual records from the company-specific TEW IX " +
                "tables. A single verified row exposes ordinary scalar edits. " +
                "Tables with multiple rows are shown in full as read-only grids " +
                "until their individual record relationships can be safely edited.",
            Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        foreach (var section in related)
        {
            var panel = CreateCompanySection(section.Label + "  |  " +
                section.Rows.Rows.Count.ToString(CultureInfo.InvariantCulture) +
                " record(s)", section.Table is "tblFedOver" or "tblFedStyle");
            panel.Children.Add(new TextBlock {
                Text = section.Explanation + "  Source: " + section.Table,
                Foreground = Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            });
            if (section.Rows.Rows.Count == 0) continue;
            var data = section.Rows;
            if (data.Rows.Count > 1 || section.Truncated)
            {
                var grid = new DataGrid {
                    ItemsSource = data.DefaultView,
                    AutoGenerateColumns = true,
                    IsReadOnly = true, CanUserAddRows = false,
                    CanUserDeleteRows = false, CanUserSortColumns = true,
                    HeadersVisibility = DataGridHeadersVisibility.All,
                    Height = 300, MinColumnWidth = 95,
                    Background = (Brush)FindResource("WindowBrush"),
                    Foreground = Brushes.White
                };
                grid.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty,
                    ScrollBarVisibility.Auto);
                grid.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty,
                    ScrollBarVisibility.Auto);
                panel.Children.Add(grid);
                continue;
            }

            DataRow item = data.Rows[0];
            string recordKey;
            int recordId;
            if (data.Columns.Contains("UID") && item["UID"] is not DBNull &&
                int.TryParse(DatabaseSession.Display(item["UID"]),
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out int uid) &&
                uid > 0)
            {
                recordKey = "UID";
                recordId = uid;
            }
            else
            {
                recordKey = "FedUID";
                recordId = _selectedExisting.Id;
            }

            AddCompanyCards(panel, data.Columns.Cast<DataColumn>()
                .Select(column => (column, item[column],
                    section.Table, recordKey, recordId)));
        }
        ExistingFieldsDescription.Text = "Company record plus linked TEW IX details. " +
            "Save Changes commits verified edits transactionally; internal IDs " +
            "and unverified references remain protected.";
    }

    private StackPanel CreateCompanySection(string title, bool expanded = false)
    {
        var content = new StackPanel { Margin = new Thickness(0, 3, 0, 8) };
        ExistingFieldsPanel.Children.Add(new Expander {
            Header = title, IsExpanded = expanded,
            Foreground = Brushes.White,
            FontSize = 14, FontWeight = FontWeights.SemiBold,
            Background = (Brush)new BrushConverter().ConvertFromString("#162B45")!,
            BorderBrush = (Brush)new BrushConverter().ConvertFromString("#3B5673")!,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 10),
            Content = content
        });
        return content;
    }

    private void AddCompanyCards(StackPanel target,
        IEnumerable<(DataColumn Column, object Value, string Table,
            string Key, int RecordId)> entries)
    {
        var items = entries.ToArray();
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < items.Length; i++)
        {
            int column = i % 2, rowIndex = i / 2;
            if (column == 0)
                layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var wrapper = new StackPanel {
                Margin = column == 0 ? new Thickness(0, 0, 7, 0) :
                    new Thickness(7, 0, 0, 0)
            };
            var entry = items[i];
            if (CompanyFieldRules.CanEdit(entry.Column))
            {
                var current = DatabaseSession.Display(entry.Value);
                var draft = new FieldDraft {
                    Table = entry.Table, Key = entry.Key,
                    Column = entry.Column.ColumnName, Original = current,
                    Value = current, ValueType = entry.Column.DataType
                };
                _existingFields.Add(draft);
                _companyFieldRecordIds[draft] = entry.RecordId;
                AddExistingField(draft, wrapper);
            }
            else
            {
                string value = entry.Value is byte[] raw
                    ? "[binary data, " + raw.Length + " bytes]"
                    : DatabaseSession.Display(entry.Value);
                var block = new Border {
                    Background = (Brush)new BrushConverter().ConvertFromString("#13253B")!,
                    BorderBrush = (Brush)new BrushConverter().ConvertFromString("#304C6B")!,
                    BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 10)
                };
                var content = new StackPanel();
                content.Children.Add(new TextBlock {
                    Text = FriendlyName(entry.Column.ColumnName) + "  (read only)",
                    Foreground = Muted, FontSize = 11,
                    ToolTip = entry.Table + "." + entry.Column.ColumnName
                });
                content.Children.Add(new TextBlock {
                    Text = string.IsNullOrWhiteSpace(value) ? "Not recorded" : value,
                    TextWrapping = TextWrapping.Wrap, FontSize = 12,
                    Foreground = Brushes.White, Margin = new Thickness(0, 5, 0, 0)
                });
                block.Child = content;
                wrapper.Children.Add(block);
            }
            Grid.SetRow(wrapper, rowIndex);
            Grid.SetColumn(wrapper, column);
            layout.Children.Add(wrapper);
        }
        target.Children.Add(layout);
    }
}
