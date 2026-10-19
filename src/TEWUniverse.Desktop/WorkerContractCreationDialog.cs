using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TEWUniverse.Studio;

namespace TEWUniverse.Desktop;

/// <summary>
/// Contract creation is based on a genuine template from the chosen company.
/// A template supplies all undocumented mandatory values; the user can review
/// and override verified editable fields before the new row is created.
/// </summary>
internal sealed class WorkerContractCreationDialog : Window
{
    private readonly DatabaseSession _session;
    private readonly WorkerItem _target;
    private readonly ComboBox _companies;
    private readonly ComboBox _templates;
    private readonly StackPanel _termFields;
    private readonly TextBlock _notice;
    private readonly Dictionary<string, Control> _inputs =
        new(StringComparer.OrdinalIgnoreCase);

    internal WorkerContractTemplate? ChosenTemplate { get; private set; }
    internal IReadOnlyDictionary<string, string> Terms { get; private set; } =
        new Dictionary<string, string>();

    internal WorkerContractCreationDialog(DatabaseSession session, WorkerItem worker,
        IReadOnlyList<ExistingRecordItem> companies)
    {
        _session = session;
        _target = worker;
        Title = "TEW Universe Studio | Create Worker Contract";
        Width = 760;
        Height = 810;
        MinWidth = 580;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("#0F2137");
        Foreground = Brushes.White;

        var root = new DockPanel { Margin = new Thickness(22) };
        Content = root;
        var controls = new StackPanel {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        DockPanel.SetDock(controls, Dock.Bottom);
        root.Children.Add(controls);
        var cancel = new Button { Content = "Cancel",
            Style = (Style)FindResource("ActionButton"),
            Margin = new Thickness(0, 0, 10, 0), Padding = new Thickness(18, 9, 18, 9) };
        cancel.Click += (_, _) => DialogResult = false;
        controls.Children.Add(cancel);
        var create = new Button { Content = "Create Contract",
            Style = (Style)FindResource("BlueButton"), Padding = new Thickness(18, 9, 18, 9) };
        create.Click += Create_Click;
        controls.Children.Add(create);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(scroll);
        var form = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        scroll.Content = form;
        form.Children.Add(new TextBlock { Text = $"New contract for {worker.Name}",
            FontSize = 23, FontWeight = FontWeights.Bold, Margin = new Thickness(0,0,0,9) });
        form.Children.Add(new TextBlock {
            Text = "Choose a company, then an EXISTING contract from that company to use " +
                   "as a template. This copies its actual stored TEW IX contract terms " +
                   "rather than guessing undocumented defaults. Review every setting; " +
                   "you can change supported fields below. No historical CV record is created.",
            Foreground = Brush("#B8D0E7"), TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0,0,0,16)
        });

        Caption(form, "Company");
        _companies = Picker();
        _companies.ItemsSource = companies.Where(c => c.Id > 0)
            .OrderBy(c => c.Name).ToArray();
        _companies.DisplayMemberPath = nameof(ExistingRecordItem.Name);
        form.Children.Add(_companies);

        Caption(form, "Contract template (same company)");
        _templates = Picker();
        _templates.DisplayMemberPath = nameof(WorkerContractTemplate.Label);
        form.Children.Add(_templates);
        _notice = new TextBlock {
            Foreground = Brush("#F0C781"), TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0,6,0,14)
        };
        form.Children.Add(_notice);
        _termFields = new StackPanel();
        form.Children.Add(_termFields);

        _companies.SelectionChanged += (_, _) => RefreshTemplates();
        _templates.SelectionChanged += (_, _) => RefreshTerms();
    }

    private static SolidColorBrush Brush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex)!);

    private ComboBox Picker() => new() {
        Style = (Style)FindResource("DarkComboBox"),
        Height = 35, MaxDropDownHeight = 260,
        IsTextSearchEnabled = true, Margin = new Thickness(0,0,0,9)
    };

    private static void Caption(Panel parent, string label) => parent.Children.Add(
        new TextBlock { Text = label, Foreground = Brush("#BEDBF0"),
            FontSize = 12, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0,6,0,6) });

    private void RefreshTemplates()
    {
        _inputs.Clear();
        _termFields.Children.Clear();
        _templates.ItemsSource = null;
        ChosenTemplate = null;
        if (_companies.SelectedItem is not ExistingRecordItem company) return;
        try
        {
            var templates = _session.ReadContractTemplates(company.Id);
            _templates.ItemsSource = templates;
            _notice.Text = templates.Count == 0
                ? "This company has no existing contract to use as a safe template. " +
                  "Contract creation is unavailable without a verified template."
                : $"Choose one of {templates.Count} real company contracts. " +
                  "The original source contract will NOT be changed.";
        }
        catch (Exception ex)
        {
            _notice.Text = "Cannot load safe contract templates: " + ex.Message;
        }
    }

    private void RefreshTerms()
    {
        _termFields.Children.Clear();
        _inputs.Clear();
        ChosenTemplate = _templates.SelectedItem as WorkerContractTemplate;
        if (ChosenTemplate == null) return;
        _notice.Text = $"Copying terms from {ChosenTemplate.Label}. " +
            "Protected links and identity fields will be assigned to the new record.";
        foreach (var field in ChosenTemplate.Fields)
        {
            Caption(_termFields, field.Name +
                (field.CanEdit ? "" : " (copied/protected)"));
            Control control;
            if (field.CanEdit && field.Type == typeof(bool))
            {
                var picker = Picker();
                picker.ItemsSource = new[] { "True", "False" };
                picker.SelectedItem = field.Value.Equals("True",
                    StringComparison.OrdinalIgnoreCase) || field.Value == "1"
                    ? "True" : "False";
                control = picker;
            }
            else
            {
                var input = new TextBox {
                    Style = (Style)FindResource("FormTextBox"),
                    Text = field.Name.Equals("WorkerUID", StringComparison.OrdinalIgnoreCase)
                        ? _target.Id.ToString(CultureInfo.InvariantCulture)
                        : field.Value,
                    IsReadOnly = !field.CanEdit,
                    MinHeight = 34,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Opacity = field.CanEdit ? 1.0 : 0.66,
                    Margin = new Thickness(0,0,0,6),
                    ToolTip = field.Type == typeof(DateTime)
                        ? "Enter a date as MM/DD/YYYY" : field.Type.Name
                };
                if (field.Name.Equals("FedUID", StringComparison.OrdinalIgnoreCase))
                    input.Text = ChosenTemplate.CompanyId.ToString(CultureInfo.InvariantCulture);
                control = input;
            }
            _termFields.Children.Add(control);
            if (field.CanEdit) _inputs[field.Name] = control;
        }
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var selected = ChosenTemplate ??
                throw new InvalidOperationException("Choose a company and its contract template.");
            var requested = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in selected.Fields.Where(f => f.CanEdit))
            {
                if (!_inputs.TryGetValue(field.Name, out Control? control))
                    throw new InvalidOperationException("Missing contract input: " + field.Name);
                string text = control switch {
                    ComboBox combo => Convert.ToString(combo.SelectedItem,
                        CultureInfo.InvariantCulture) ?? "",
                    TextBox input => input.Text,
                    _ => ""
                };
                if (!string.Equals(text, field.Value, StringComparison.Ordinal))
                    requested[field.Name] = text;
            }
            Terms = requested;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Check New Contract",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
