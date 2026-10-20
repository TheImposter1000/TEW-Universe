using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TEWUniverse.Studio;

namespace TEWUniverse.Desktop;

/// <summary>Paste or load named historical title winners, with no roster creation.</summary>
internal sealed class HistoricalNamesImportDialog : Window
{
    private readonly TextBox _names;
    private readonly int _members;
    internal IReadOnlyList<NameOnlyChampionImport.Line> Entries { get; private set; } =
        Array.Empty<NameOnlyChampionImport.Line>();

    internal HistoricalNamesImportDialog(int members)
    {
        _members = members;
        Title = "Import Historical Champion Names";
        Width = 695;
        Height = 580;
        MinWidth = 470;
        MinHeight = 370;
        Background = new SolidColorBrush(Color.FromRgb(15, 33, 55));
        Foreground = Brushes.White;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(24) };
        Content = root;
        var actions = new WrapPanel {
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        DockPanel.SetDock(actions, Dock.Bottom);
        root.Children.Add(actions);

        Button Action(string label, RoutedEventHandler callback, bool primary = false)
        {
            var button = new Button {
                Content = label,
                Padding = new Thickness(16, 9, 16, 9),
                Margin = new Thickness(6, 0, 0, 0),
                Style = (Style)FindResource(primary ? "BlueButton" : "ActionButton")
            };
            button.Click += callback;
            actions.Children.Add(button);
            return button;
        }
        Action("Cancel", (_, _) => DialogResult = false);
        Action("Import Names", (_, _) =>
        {
            try
            {
                Entries = NameOnlyChampionImport.Parse(_names.Text, _members);
                DialogResult = true;
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(this, ex.Message, "Check Name List",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }, primary: true);

        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        DockPanel.SetDock(heading, Dock.Top);
        root.Children.Add(heading);
        heading.Children.Add(new TextBlock {
            Text = "Import name-only championship winners",
            FontWeight = FontWeights.Bold, FontSize = 22,
            Margin = new Thickness(0, 0, 0, 9)
        });
        string example = _members switch {
            1 => "Grant Cade\nDamien Ward\nAnother Champion",
            2 => "Carson Cade / Grant Cade\nTeam Member A / Team Member B",
            _ => "Wrestler A / Wrestler B / Wrestler C\nName D / Name E / Name F"
        };
        heading.Children.Add(new TextBlock {
            Text = "Enter ONE champion or complete team per line. You can also load " +
                "a .txt or .csv file. For team titles separate two or three names " +
                "with / or commas. Every imported person is name-only (UID -1), " +
                "not a created roster worker. After importing, set each entry's " +
                "winning years and reign counts in the generator table.\n\nExample:\n" + example,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(182, 209, 233)),
            Margin = new Thickness(0, 0, 0, 9)
        });
        var load = new Button {
            Content = "Load names from .txt / .csv…",
            Padding = new Thickness(12, 7, 12, 7),
            HorizontalAlignment = HorizontalAlignment.Left,
            Style = (Style)FindResource("ActionButton")
        };
        load.Click += (_, _) =>
        {
            var file = new OpenFileDialog {
                Title = "Choose text list of historical champions",
                Filter = "Text or CSV files (*.txt;*.csv)|*.txt;*.csv|All files (*.*)|*.*",
                CheckFileExists = true
            };
            if (file.ShowDialog(this) != true) return;
            try
            {
                if (new FileInfo(file.FileName).Length > 100_000)
                    throw new InvalidOperationException("The import file exceeds 100 KB.");
                _names.Text = File.ReadAllText(file.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Unable to Load Names",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        heading.Children.Add(load);

        _names = new TextBox {
            Style = (Style)FindResource("FormTextBox"),
            AcceptsReturn = true,
            AcceptsTab = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            Padding = new Thickness(12),
            VerticalContentAlignment = VerticalAlignment.Top
        };
        root.Children.Add(_names);
    }
}
