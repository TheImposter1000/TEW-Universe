using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TEWUniverse.Studio;

namespace TEWUniverse.Desktop;

/// <summary>Builds a new tblTeam record from existing workers, companies, and observed TEW codes.</summary>
internal sealed class TagTeamCreateDialog : Window
{
    private sealed record Choice(int Id, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly TextBox _name;
    private readonly TextBox _experience;
    private readonly TextBox _formed;
    private readonly TextBox _finisher;
    private readonly TextBox _finisherDesc;
    private readonly ComboBox _worker1;
    private readonly ComboBox _worker2;
    private readonly ComboBox _company;
    private readonly ComboBox _type;
    private readonly ComboBox _active;

    internal TeamCreationDraft? Draft { get; private set; }

    internal TagTeamCreateDialog(IReadOnlyList<WorkerItem> workers,
        IReadOnlyList<ExistingRecordItem> companies, IReadOnlyList<int> typeCodes,
        IReadOnlyList<int> globalCompanyCodes, Type formedFieldType)
    {
        Title = "TEW Universe Studio  |  Create Tag Team";
        Width = 690;
        Height = 810;
        MinWidth = 565;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("#0F2137");
        Foreground = Brushes.White;

        var main = new DockPanel { Margin = new Thickness(22) };
        Content = main;
        var buttons = new StackPanel {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        DockPanel.SetDock(buttons, Dock.Bottom);
        main.Children.Add(buttons);

        var create = new Button {
            Content = "Create Tag Team", Style = (Style)FindResource("BlueButton"),
            Padding = new Thickness(18, 10, 18, 10), MinWidth = 150
        };
        create.Click += Create_Click;
        var cancel = new Button {
            Content = "Cancel", Style = (Style)FindResource("ActionButton"),
            Margin = new Thickness(0, 0, 10, 0), Padding = new Thickness(18, 10, 18, 10)
        };
        cancel.Click += (_, _) => DialogResult = false;
        buttons.Children.Add(cancel);
        buttons.Children.Add(create);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        main.Children.Add(scroll);
        var content = new StackPanel { Margin = new Thickness(0, 0, 13, 0) };
        scroll.Content = content;
        content.Children.Add(new TextBlock {
            Text = "New Tag Team", FontSize = 24, FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 9)
        });
        content.Children.Add(new TextBlock {
            Text = "Create a real TEW IX team record using two existing workers. " +
                "Choose the team's stored type code from codes actually present in this database.",
            TextWrapping = TextWrapping.Wrap, Foreground = Brush("#B6D0EA"),
            FontSize = 12, Margin = new Thickness(0, 0, 0, 19)
        });

        _name = Entry();
        AddField(content, "Team name", _name);
        _worker1 = Picker(workers.Select(w => new Choice(w.Id,
            $"{w.Name}  |  Worker ID {w.Id}")).ToArray());
        _worker2 = Picker(workers.Select(w => new Choice(w.Id,
            $"{w.Name}  |  Worker ID {w.Id}")).ToArray());
        AddField(content, "First worker", _worker1);
        AddField(content, "Second worker", _worker2);
        _company = Picker(globalCompanyCodes.OrderBy(x => x)
            .Select(code => new Choice(code, $"None / game-wide (stored {code})"))
            .Concat(companies.Select(c => new Choice(c.Id,
                $"{c.Name}  |  Company ID {c.Id}"))).ToArray());
        AddField(content, "Company (or game-wide)", _company);

        _type = Picker(typeCodes.Select(code =>
            new Choice(code, TewCodeLabels.Display("tblTeam", "Type",
                code.ToString(CultureInfo.InvariantCulture)))).ToArray());
        _type.IsEditable = false;
        if (typeCodes.Count == 1) _type.SelectedIndex = 0;
        AddField(content, "Team type", _type);
        content.Children.Add(new TextBlock {
            Text = "Team type names are display labels. The original numeric code is used when saving.",
            TextWrapping = TextWrapping.Wrap, Foreground = Brush("#A6BDD9"),
            FontSize = 11, Margin = new Thickness(0, -4, 0, 12)
        });

        _experience = Entry("0");
        AddField(content, "Experience (stored numeric value)", _experience);
        _formed = Entry();
        _formed.ToolTip = formedFieldType == typeof(DateTime)
            ? "Enter MM/DD/YYYY; use 01/01/1666 only when TEW IX treats the date as unknown."
            : $"Enter the stored {formedFieldType.Name} value used by this database.";
        AddField(content, $"Formed ({formedFieldType.Name})", _formed);
        _finisher = Entry();
        AddField(content, "Tag team finisher (optional)", _finisher);
        _finisherDesc = Entry();
        _finisherDesc.AcceptsReturn = true;
        _finisherDesc.TextWrapping = TextWrapping.Wrap;
        _finisherDesc.MinHeight = 65;
        AddField(content, "Finisher description (optional)", _finisherDesc);
        _active = Picker(new[] { "Yes", "No" });
        _active.IsEditable = false;
        _active.SelectedIndex = 0;
        AddField(content, "Active", _active);
    }

    private static SolidColorBrush Brush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex)!);

    private TextBox Entry(string value = "") => new() {
        Style = (Style)FindResource("FormTextBox"),
        Text = value, MinHeight = 35,
        VerticalContentAlignment = VerticalAlignment.Center
    };

    private ComboBox Picker(System.Collections.IEnumerable choices)
    {
        var combo = new ComboBox {
            Style = (Style)FindResource("DarkComboBox"),
            ItemsSource = choices,
            IsEditable = true, IsTextSearchEnabled = true,
            MinHeight = 35, MaxDropDownHeight = 260
        };
        if (choices is IEnumerable<Choice>)
        {
            combo.DisplayMemberPath = nameof(Choice.Label);
            TextSearch.SetTextPath(combo, nameof(Choice.Label));
        }
        return combo;
    }

    private static void AddField(Panel panel, string label, Control input)
    {
        var host = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        host.Children.Add(new TextBlock {
            Text = label, Foreground = Brush("#BDDAF3"),
            FontSize = 12, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        });
        host.Children.Add(input);
        panel.Children.Add(host);
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_name.Text) ||
            _worker1.SelectedItem is not Choice one ||
            _worker2.SelectedItem is not Choice two ||
            _company.SelectedItem is not Choice company ||
            _type.SelectedItem is not Choice type)
        {
            MessageBox.Show(this, "Enter a team name and select two workers, a company, and a team type.",
                "Incomplete team", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (one.Id == two.Id)
        {
            MessageBox.Show(this, "A tag team must contain two different workers.",
                "Duplicate worker", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Draft = new TeamCreationDraft(_name.Text.Trim(), one.Id, two.Id,
            company.Id, type.Id, _experience.Text.Trim(), _formed.Text.Trim(),
            _finisher.Text.Trim(), _finisherDesc.Text.Trim(), _active.SelectedIndex == 0);
        DialogResult = true;
    }
}
