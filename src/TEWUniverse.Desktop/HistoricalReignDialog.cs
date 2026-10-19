using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TEWUniverse.Studio;

namespace TEWUniverse.Desktop;

/// <summary>
/// Records historical champions without creating tblWorker rows. Each member
/// is either a real worker UID or a name-only NPC (-1).
/// </summary>
internal sealed class HistoricalReignDialog : Window
{
    private sealed record HolderOption(int Id, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly ComboBox[] _holders = new ComboBox[3];
    private readonly TextBox[] _names = new TextBox[3];
    private readonly TextBox _captured;
    private readonly TextBox _lost;
    private readonly TextBox _defences;
    private readonly int _required;
    private readonly bool _annual;
    private readonly bool _edit;
    internal HistoricalReignDraft? Draft { get; private set; }

    internal HistoricalReignDialog(IReadOnlyList<WorkerItem> workers, int members,
        bool annual, HistoricalReignDraft? original)
    {
        if (members is < 1 or > 3)
            throw new ArgumentOutOfRangeException(nameof(members));
        _required = members;
        _annual = annual;
        _edit = original != null;
        Title = original == null ? "Insert Historical Reign" : "Edit Historical Champions";
        Width = 670;
        Height = members == 3 ? 770 : members == 2 ? 690 : 610;
        MinWidth = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("#0F2137");
        Foreground = Brushes.White;

        var root = new DockPanel { Margin = new Thickness(23) };
        Content = root;
        var actions = new StackPanel {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 13, 0, 0)
        };
        DockPanel.SetDock(actions, Dock.Bottom);
        root.Children.Add(actions);
        var cancel = new Button { Content = "Cancel",
            Style = (Style)FindResource("ActionButton"), Padding = new Thickness(18, 9),
            Margin = new Thickness(0, 0, 10, 0) };
        cancel.Click += (_, _) => DialogResult = false;
        actions.Children.Add(cancel);
        var save = new Button {
            Content = _edit ? "Apply Holders" : "Insert Reign",
            Style = (Style)FindResource("BlueButton"), Padding = new Thickness(18, 9)
        };
        save.Click += Save_Click;
        actions.Children.Add(save);

        var scroller = new ScrollViewer {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        root.Children.Add(scroller);
        var form = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        scroller.Content = form;
        form.Children.Add(new TextBlock {
            Text = _edit ? "Edit historical champions" : "Add one historical reign",
            FontSize = 23, FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        form.Children.Add(new TextBlock {
            Text = "Select an existing wrestler or Name-only NPC for EACH champion. " +
                   "An NPC is saved as holder ID -1 with a historical name; it is NOT " +
                   "created as a playable worker or a tblTeam entry.",
            Foreground = Brush("#B8D2E9"), TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        var options = new[] { new HolderOption(-1, "Name-only NPC (not on roster)") }
            .Concat(workers.Select(w => new HolderOption(w.Id,
                $"{w.Name}  |  Worker ID {w.Id}"))).ToArray();
        for (int i = 0; i < members; i++)
        {
            int index = i;
            var initialId = original?.HolderIds[i] ?? -1;
            Label(form, $"Champion {i + 1} (worker or NPC)");
            var picker = new ComboBox {
                Style = (Style)FindResource("DarkComboBox"),
                ItemsSource = options,
                DisplayMemberPath = nameof(HolderOption.Label),
                SelectedItem = options.FirstOrDefault(o => o.Id == initialId) ?? options[0],
                MaxDropDownHeight = 240, Height = 35, IsTextSearchEnabled = true,
                Margin = new Thickness(0, 0, 0, 6)
            };
            _holders[i] = picker;
            form.Children.Add(picker);
            Label(form, $"Champion {i + 1} historical display name");
            var name = Entry(original?.HolderNames[i] ?? "");
            _names[i] = name;
            picker.SelectionChanged += (_, _) => {
                if (picker.SelectedItem is not HolderOption option || option.Id < 1)
                    return;
                // Preserve a manually entered ring name when switching workers.
                if (string.IsNullOrWhiteSpace(name.Text) ||
                    options.Any(o => o.Id > 0 && o.Label.StartsWith(name.Text + "  |",
                        StringComparison.OrdinalIgnoreCase)))
                    name.Text = workers.First(w => w.Id == option.Id).Name;
            };
            form.Children.Add(name);
        }

        Label(form, "Captured (MM/DD/YYYY)");
        _captured = Entry(StudioDateFormat.Format(original?.Captured ?? DateTime.Today));
        _captured.IsReadOnly = _edit;
        form.Children.Add(_captured);

        Label(form, "Lost (MM/DD/YYYY)");
        _lost = Entry(StudioDateFormat.Format(original?.Lost ??
            (_annual ? new DateTime(1666, 1, 1) : DateTime.Today)));
        _lost.IsReadOnly = _edit || _annual;
        form.Children.Add(_lost);

        Label(form, "Defences");
        _defences = Entry((original?.Defences ?? (_annual ? (short)-1 : (short)0))
            .ToString(CultureInfo.InvariantCulture));
        _defences.IsReadOnly = _edit || _annual;
        form.Children.Add(_defences);
        form.Children.Add(new TextBlock {
            Text = _edit
                ? "This changes ONLY the names and linked IDs in the selected reign. " +
                  "Dates, defenses, UID, record name and order are preserved."
                : "The new reign will be placed by capture date. Existing dates, " +
                  "holder identities and record names are preserved; BeltOrder for " +
                  "this title may be renumbered. Overlapping reign dates are blocked.",
            Foreground = Brush("#B4D4E7"), TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 12)
        });
    }

    private static SolidColorBrush Brush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex)!);

    private void Label(Panel panel, string text) => panel.Children.Add(new TextBlock {
        Text = text, FontSize = 12, Foreground = Brush("#BCD9EE"),
        FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 6)
    });

    private TextBox Entry(string text) => new() {
        Style = (Style)FindResource("FormTextBox"), Text = text,
        MinHeight = 35, Margin = new Thickness(0, 0, 0, 11),
        VerticalContentAlignment = VerticalAlignment.Center
    };

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var ids = new[] { -1, -1, -1 };
            var names = new[] { "", "", "" };
            for (int i = 0; i < _required; i++)
            {
                if (_holders[i].SelectedItem is not HolderOption option)
                    throw new InvalidOperationException("Choose each champion or name-only NPC.");
                ids[i] = option.Id;
                names[i] = _names[i].Text.Trim();
                if (names[i].Length == 0 || names[i].Length > 30 ||
                    names[i].Any(char.IsControl))
                    throw new InvalidOperationException(
                        $"Champion {i + 1} needs a name of 1–30 characters.");
            }
            if (!StudioDateFormat.TryParse(_captured.Text, out DateTime captured) ||
                !StudioDateFormat.TryParse(_lost.Text, out DateTime lost) ||
                !short.TryParse(_defences.Text, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out short defences))
                throw new InvalidOperationException("Enter valid dates as MM/DD/YYYY and numeric defenses.");
            Draft = new HistoricalReignDraft(ids, names, captured, lost, defences);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Check Historical Reign",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
