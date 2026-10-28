using System.Windows;
using System.Windows.Controls;
using TEWUniverse.Studio;

namespace TEWUniverse.Desktop;

/// <summary>
/// Biography-style, read-only career record. All contents come directly
/// from real TEW IX championship history and year-end award rows.
/// </summary>
public partial class MainWindow
{
    private TextBox _careerSummaryText = null!;

    private void InitializeAccomplishmentsUI()
    {
        var layout = new DockPanel { Margin = new Thickness(3) };
        AccomplishmentsHost.Children.Add(layout);
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 13) };
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);
        header.Children.Add(ExtraLabel("Accomplishments", true));
        header.Children.Add(new TextBlock {
            Text = "Automatically generated from the selected worker's linked " +
                "championship histories and recorded world/company year-end awards. " +
                "No manual entry and no modifications to the MDB.",
            Foreground = ExtraColor("#A9C6E3"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 6)
        });
        var frame = new Border {
            Style = (Style)FindResource("CardBorder"),
            Padding = new Thickness(16)
        };
        layout.Children.Add(frame);
        _careerSummaryText = new TextBox {
            Style = (Style)FindResource("FormTextBox"),
            Text = "Select a worker to view recorded accomplishments.",
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalContentAlignment = VerticalAlignment.Top,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(11),
            FontSize = 14
        };
        frame.Child = _careerSummaryText;
    }

    // The tab is always derived and read-only: switching workers/tabs cannot
    // lose edits because there are no edits to this particular form.
    private bool HasPendingAccomplishmentEdits() => false;

    private void RefreshAccomplishments()
    {
        if (_careerSummaryText == null) return;
        if (_session == null || _worker == null)
        {
            _careerSummaryText.Text = "Select a worker to view recorded accomplishments.";
            return;
        }
        try
        {
            _careerSummaryText.Text = _session.ReadWorkerCareerSummary(_worker.Id);
            _careerSummaryText.ScrollToHome();
        }
        catch (Exception ex)
        {
            _careerSummaryText.Text =
                "Could not read championship/award history: " + ex.Message;
        }
    }
}
