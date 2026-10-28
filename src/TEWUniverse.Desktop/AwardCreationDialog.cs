using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TEWUniverse.Studio;

namespace TEWUniverse.Desktop;

/// <summary>
/// Create a new company or world award year without reusing last year's
/// winner selections. The template supplies only required MDB context.
/// </summary>
internal sealed class AwardCreationDialog : Window
{
    private readonly TextBox _year;
    private readonly Dictionary<string, ComboBox> _pickers =
        new(StringComparer.OrdinalIgnoreCase);

    internal string AwardYear => _year.Text.Trim();
    internal IReadOnlyDictionary<string, string> Winners { get; private set; } =
        new Dictionary<string, string>();

    internal AwardCreationDialog(AwardEntry template, bool companySpecific,
        IEnumerable<WorkerItem> workers, IReadOnlyList<ExistingRecordItem> companies,
        string proposedYear)
    {
        Title = companySpecific ? "Create Company Award Year" : "Create World Award Year";
        Width = 720;
        Height = 760;
        MinWidth = 560;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("#10233A");
        Foreground = Brush("#E8F4FF");
        FontFamily = new FontFamily("Segoe UI");
        var root = new DockPanel { Margin = new Thickness(22) };
        Content = root;

        var bottom = new StackPanel {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        var cancel = new Button { Content = "Cancel", Width = 115, Height = 38,
            Margin = new Thickness(0, 0, 10, 0),
            Style = (Style)Application.Current.FindResource("ActionButton") };
        cancel.Click += (_, _) => DialogResult = false;
        bottom.Children.Add(cancel);
        var create = new Button { Content = "Create New Award Year", MinWidth = 180, Height = 38,
            Style = (Style)Application.Current.FindResource("BlueButton") };
        create.Click += Confirm;
        bottom.Children.Add(create);

        var scroll = new ScrollViewer {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        root.Children.Add(scroll);
        var form = new StackPanel { Margin = new Thickness(0, 0, 13, 0) };
        scroll.Content = form;
        form.Children.Add(new TextBlock {
            Text = Title,
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        form.Children.Add(new TextBlock {
            Text = "Assign the award recipients for the NEW year. All recipients begin " +
                   "unassigned, independently from the selected template. No changes " +
                   "will be made to the template record.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#A9C6E3"),
            Margin = new Thickness(0, 0, 0, 17)
        });
        form.Children.Add(Label("Award year"));
        _year = new TextBox {
            Style = (Style)Application.Current.FindResource("FormTextBox"),
            Text = proposedYear,
            MinHeight = 35,
            Margin = new Thickness(0, 0, 0, 18)
        };
        form.Children.Add(_year);

        string table = companySpecific ? AwardsRules.CompanyTable : AwardsRules.GlobalTable;
        var allWorkers = workers.Select(x => new ExistingRecordItem(x.Id, x.Name))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        allWorkers.Insert(0, new ExistingRecordItem(0, "(No winner recorded)"));
        var allCompanies = companies.OrderBy(x => x.Name,
            StringComparer.OrdinalIgnoreCase).ToList();
        var globalCompanies = new List<ExistingRecordItem> {
            new(0, "(No company selected)")
        };
        globalCompanies.AddRange(allCompanies);

        foreach (var field in template.Fields)
        {
            if (!field.Editable ||
                (!AwardsRules.IsWorkerField(table, field.Name, field.Type) &&
                 !AwardsRules.IsCompanyField(table, field.Name, field.Type)))
                continue;
            bool worker = AwardsRules.IsWorkerField(table, field.Name, field.Type);
            form.Children.Add(Label(AwardsRules.FriendlyName(field.Name)));
            var picker = new ComboBox {
                Style = (Style)Application.Current.FindResource("DarkComboBox"),
                MinHeight = 37,
                Margin = new Thickness(0, 0, 0, 13),
                MaxDropDownHeight = 240,
                IsEditable = true,
                IsTextSearchEnabled = true,
                DisplayMemberPath = nameof(ExistingRecordItem.Name),
                ItemsSource = worker ? allWorkers :
                    AwardsRules.CanBeUnassigned(table, field.Name, field.Type)
                        ? globalCompanies : allCompanies
            };
            TextSearch.SetTextPath(picker, nameof(ExistingRecordItem.Name));
            int.TryParse(field.StoredValue, out int original);
            picker.SelectedItem = worker ||
                AwardsRules.CanBeUnassigned(table, field.Name, field.Type)
                    ? (picker.ItemsSource as IEnumerable<ExistingRecordItem>)!
                        .FirstOrDefault(x => x.Id == 0)
                    : (picker.ItemsSource as IEnumerable<ExistingRecordItem>)!
                        .FirstOrDefault(x => x.Id == original);
            _pickers[field.Name] = picker;
            form.Children.Add(picker);
        }
        form.Children.Add(new TextBlock {
            Text = "Other legacy database fields, including match and show award " +
                   "metadata, are not treated as worker IDs. They remain " +
                   "protected until their formats are confirmed.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#A9C6E3"),
            Margin = new Thickness(0, 12, 0, 8)
        });
    }

    private static TextBlock Label(string text) => new() {
        Text = text,
        Foreground = Brush("#D4E9FC"),
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 0, 0, 6)
    };

    private static Brush Brush(string hex) =>
        (Brush)new BrushConverter().ConvertFromString(hex)!;

    private void Confirm(object sender, RoutedEventArgs e)
    {
        try
        {
            AwardsRules.ValidateYear(AwardYear);
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, picker) in _pickers)
            {
                if (picker.SelectedItem is not ExistingRecordItem item)
                    throw new InvalidOperationException(
                        "Select a listed worker or company for " +
                        AwardsRules.FriendlyName(name) + ".");
                values[name] = item.Id.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
            }
            Winners = values;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Check New Award",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
