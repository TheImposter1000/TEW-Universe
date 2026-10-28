using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TEWUniverse.Studio;

namespace TEWUniverse.Desktop;

/// <summary>
/// Biography-style read-only career record with company championship groupings.
/// The source is the live MDB; no separate accomplishments editor or fake wins.
/// </summary>
public partial class MainWindow
{
    private StackPanel _careerContent = null!;

    private void InitializeAccomplishmentsUI()
    {
        var layout = new DockPanel { Margin = new Thickness(3) };
        AccomplishmentsHost.Children.Add(layout);
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 13) };
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);
        header.Children.Add(ExtraLabel("Titles & Accomplishments", true));
        header.Children.Add(new TextBlock {
            Text = "Championships are grouped by promotion. Award wins come from " +
                "the selected worker's recorded global and company year-end results.",
            Foreground = ExtraColor("#A9C6E3"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 4)
        });
        var frame = new Border {
            Style = (Style)FindResource("CardBorder"),
            Padding = new Thickness(17)
        };
        layout.Children.Add(frame);
        var scroll = new ScrollViewer {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        frame.Child = scroll;
        _careerContent = new StackPanel { Margin = new Thickness(4, 0, 15, 0) };
        scroll.Content = _careerContent;
    }

    private bool HasPendingAccomplishmentEdits() => false;

    private void RefreshAccomplishments()
    {
        if (_careerContent == null) return;
        _careerContent.Children.Clear();
        if (_session == null || _worker == null)
        {
            _careerContent.Children.Add(ExtraLabel(
                "Select a worker to view their career record."));
            return;
        }
        try
        {
            var record = _session.ReadWorkerCareerDetails(_worker.Id);
            _careerContent.Children.Add(ExtraLabel("Championships", true));

            if (record.Championships.Count == 0)
                _careerContent.Children.Add(ExtraLabel(
                    "No linked championship reigns recorded."));
            foreach (var company in record.Championships.GroupBy(x => x.Company))
            {
                var heading = ExtraLabel(company.Key, true);
                heading.Margin = new Thickness(0, 20, 0, 12);
                _careerContent.Children.Add(heading);
                foreach (var title in company)
                {
                    var row = new StackPanel {
                        Orientation = Orientation.Horizontal,
                        Margin = new Thickness(8, 0, 0, 7)
                    };
                    row.Children.Add(new TextBlock {
                        Text = "×" + title.Reigns,
                        FontWeight = FontWeights.Bold,
                        Foreground = ExtraColor("#EAF5FF"),
                        Width = 47,
                        FontSize = 15
                    });
                    row.Children.Add(new TextBlock {
                        Text = title.Title,
                        Foreground = ExtraColor("#E4F1FF"),
                        FontSize = 15,
                        TextWrapping = TextWrapping.Wrap
                    });
                    _careerContent.Children.Add(row);

                    // Dates are reference detail, not the primary accomplishment.
                    var dated = title.WonDates.Where(x => !string.IsNullOrWhiteSpace(x))
                        .ToArray();
                    if (dated.Length == 0) continue;
                    var dates = new Expander {
                        Header = "Reign dates",
                        IsExpanded = false,
                        Foreground = ExtraColor("#9CBFE1"),
                        Margin = new Thickness(55, 0, 0, 8)
                    };
                    var dateLines = new StackPanel();
                    foreach (string date in dated)
                    {
                        var value = ExtraLabel("Won " + date);
                        value.Margin = new Thickness(3, 4, 0, 3);
                        dateLines.Children.Add(value);
                    }
                    dates.Content = dateLines;
                    _careerContent.Children.Add(dates);
                }
            }

            var divider = new Separator { Margin = new Thickness(0, 24, 0, 16) };
            _careerContent.Children.Add(divider);
            _careerContent.Children.Add(ExtraLabel("Awards & Other Accomplishments", true));
            if (record.Awards.Count == 0)
                _careerContent.Children.Add(ExtraLabel("No linked year-end award wins recorded."));
            else foreach (var scope in record.Awards.GroupBy(x => x.Scope))
            {
                var heading = ExtraLabel(scope.Key, true);
                heading.Margin = new Thickness(0, 20, 0, 12);
                _careerContent.Children.Add(heading);
                foreach (var award in scope)
                {
                    string year = string.IsNullOrWhiteSpace(award.Year)
                        ? "" : " (" + award.Year + ")";
                    string details = award.Details.Length == 0
                        ? "" : "  " + award.Details;
                    _careerContent.Children.Add(new TextBlock {
                        Text = "• " + award.Category + year + details,
                        Foreground = ExtraColor("#E4F1FF"),
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 14,
                        Margin = new Thickness(9, 0, 0, 9)
                    });
                }
            }
            foreach (string note in record.Notes)
            {
                var warning = ExtraLabel(note);
                warning.TextWrapping = TextWrapping.Wrap;
                warning.Margin = new Thickness(0, 14, 0, 0);
                _careerContent.Children.Add(warning);
            }
        }
        catch (Exception ex)
        {
            _careerContent.Children.Add(ExtraLabel(
                "Could not read championship/award history: " + ex.Message));
        }
    }
}
