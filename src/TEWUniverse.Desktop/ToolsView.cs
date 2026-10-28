using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TEWUniverse.Studio;

namespace TEWUniverse.Desktop;

/// <summary>
/// Tools are read-only diagnostics and shortcuts into existing editors.
/// No auto-fix is permitted: freeform biographies require manual review.
/// </summary>
public partial class MainWindow
{
    private DataGrid _toolsIssues = null!;
    private TextBlock _toolsSummary = null!;
    private TextBox _toolsSearch = null!;
    private ComboBox _toolsSeverity = null!;
    private List<StudioDataIssue> _toolsResults = new();

    private void InitializeToolsUI()
    {
        var outer = new DockPanel { Margin = new Thickness(2) };
        ToolsWorkspace.Children.Add(outer);
        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 13) };
        DockPanel.SetDock(top, Dock.Top);
        outer.Children.Add(top);
        top.Children.Add(ExtraLabel("Studio Tools & Data Check", true));
        top.Children.Add(new TextBlock {
            Text = "Inspect links between workers, title histories, tag teams, " +
                "year-end awards, and biographies. Nothing is changed automatically. " +
                "Review findings before editing the original data.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = ExtraColor("#B9D2E8"), Margin = new Thickness(0, 4, 0, 12)
        });
        var controls = new WrapPanel();
        top.Children.Add(controls);
        var run = ExtraButton("Run Data Check", true, (_, _) => RunStudioDataCheck());
        controls.Children.Add(run);
        var export = ExtraButton("Export Results CSV", false, (_, _) => ExportToolsReport());
        controls.Children.Add(export);
        controls.Children.Add(ExtraButton("Open Workers", false,
            (_, _) => SelectModule("Workers")));
        controls.Children.Add(ExtraButton("Open Titles / Repairs", false,
            (_, _) => SelectModule("Titles")));
        controls.Children.Add(ExtraButton("Open Awards", false,
            (_, _) => SelectAwardsModule()));
        _toolsSummary = ExtraLabel("Run the read-only check to inspect the active MDB.");
        _toolsSummary.TextWrapping = TextWrapping.Wrap;
        top.Children.Add(_toolsSummary);

        var filters = new DockPanel { Margin = new Thickness(0, 10, 0, 10) };
        top.Children.Add(filters);
        _toolsSeverity = ExtraPicker();
        _toolsSeverity.ItemsSource = new[] { "All findings", "Errors", "Needs Review", "Unavailable" };
        _toolsSeverity.SelectedIndex = 0;
        _toolsSeverity.Width = 170;
        _toolsSeverity.Margin = new Thickness(0, 0, 12, 0);
        DockPanel.SetDock(_toolsSeverity, Dock.Right);
        filters.Children.Add(_toolsSeverity);
        _toolsSeverity.SelectionChanged += (_, _) => FilterToolsResults();
        _toolsSearch = ExtraTextBox();
        _toolsSearch.ToolTip = "Filter by issue area, record ID, worker, championship, or description";
        _toolsSearch.Margin = new Thickness(0, 0, 0, 0);
        _toolsSearch.TextChanged += (_, _) => FilterToolsResults();
        filters.Children.Add(_toolsSearch);

        var card = new Border { Style = (Style)FindResource("CardBorder"),
            Padding = new Thickness(12) };
        outer.Children.Add(card);
        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        card.Child = body;
        _toolsIssues = ExtraGrid();
        TextColumn(_toolsIssues, "Status", "Severity", new DataGridLength(105));
        TextColumn(_toolsIssues, "Area", "Area", new DataGridLength(160));
        TextColumn(_toolsIssues, "Record", "Record",
            new DataGridLength(1, DataGridLengthUnitType.Star));
        TextColumn(_toolsIssues, "Finding", "Description",
            new DataGridLength(2.3, DataGridLengthUnitType.Star));
        Grid.SetRow(_toolsIssues, 0);
        body.Children.Add(_toolsIssues);
        var info = ExtraLabel("Error = a missing structured record link. " +
            "Review = an ambiguous or possibly historical mismatch. " +
            "Unavailable = a table could not be checked. " +
            "Existing record-name repairs remain under Titles → Title Lineage.");
        info.TextWrapping = TextWrapping.Wrap;
        info.Margin = new Thickness(2, 12, 2, 2);
        Grid.SetRow(info, 1);
        body.Children.Add(info);
    }

    private void SelectToolsModule()
    {
        if (_activeModule == "Tools") return;
        if (!MayLeave()) return;
        _activeModule = "Tools";
        WorkerBrowserPanel.Visibility = Visibility.Collapsed;
        WorkerDetailPanel.Visibility = Visibility.Collapsed;
        ExistingWorkspace.Visibility = Visibility.Collapsed;
        RelationshipsWorkspace.Visibility = Visibility.Collapsed;
        AwardsWorkspace.Visibility = Visibility.Collapsed;
        ToolsWorkspace.Visibility = Visibility.Visible;
        WorkersNav.Style = (Style)FindResource("NavigationButton");
        RelationshipsNav.Style = (Style)FindResource("NavigationButton");
        AwardsNav.Style = (Style)FindResource("NavigationButton");
        ToolsNav.Style = (Style)FindResource("BlueButton");
        foreach (var button in new[] {
            CompaniesNav, TitlesNav, TagTeamsNav, EventsNav, StablesNav
        }) button.Style = (Style)FindResource("NavigationButton");
        RefreshDirty();
    }

    private void RunStudioDataCheck()
    {
        if (_session == null)
        {
            MessageBox.Show(this, "Select a TEW IX scenario MDB first.");
            return;
        }
        try
        {
            SetStatus("Reading linked title, team, award, and worker records...");
            _toolsResults = _session.RunStudioDataCheck();
            FilterToolsResults();
            SetStatus($"Data Check finished: {_toolsResults.Count} findings (read-only).");
        }
        catch (Exception ex) { Error(ex); }
    }

    private void FilterToolsResults()
    {
        if (_toolsIssues == null) return;
        string search = _toolsSearch?.Text.Trim() ?? "";
        string severity = _toolsSeverity?.SelectedItem as string ?? "All findings";
        var visible = _toolsResults.Where(x =>
            (severity switch {
                "Errors" => x.Severity == "Error",
                "Needs Review" => x.Severity == "Review",
                "Unavailable" => x.Severity == "Unavailable",
                _ => true
            }) &&
            (search.Length == 0 || x.SearchText.Contains(search,
                StringComparison.OrdinalIgnoreCase))).ToList();
        _toolsIssues.ItemsSource = visible;
        int errors = _toolsResults.Count(x => x.Severity == "Error");
        int reviews = _toolsResults.Count(x => x.Severity == "Review");
        int unavailable = _toolsResults.Count(x => x.Severity == "Unavailable");
        _toolsSummary.Text = _toolsResults.Count == 0 ?
            "No reported inconsistencies in the checked records. This is not " +
            "a substitute for TEW IX's own Data Check." :
            $"Showing {visible.Count} of {_toolsResults.Count} findings: " +
            $"{errors} structured errors, {reviews} need review, " +
            $"{unavailable} unverified/unavailable tables.";
    }

    private void ExportToolsReport()
    {
        if (_toolsResults.Count == 0)
        {
            MessageBox.Show(this, "Run a Data Check with findings to export first.");
            return;
        }
        var picker = new SaveFileDialog {
            Title = "Export TEW Universe Data Check", Filter = "CSV document (*.csv)|*.csv",
            FileName = "TEW-Universe-Data-Check.csv", AddExtension = true
        };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            var csv = new StringBuilder();
            csv.AppendLine("Severity,Area,Record,Description,WorkerUID");
            foreach (var issue in _toolsResults)
                csv.AppendLine(string.Join(",", new[] {
                    DataCheckRules.Csv(issue.Severity), DataCheckRules.Csv(issue.Area),
                    DataCheckRules.Csv(issue.Record), DataCheckRules.Csv(issue.Description),
                    issue.WorkerUid.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }));
            File.WriteAllText(picker.FileName, csv.ToString(), new UTF8Encoding(true));
            SetStatus("Exported read-only Data Check report.");
        }
        catch (Exception ex) { Error(ex); }
    }
}
