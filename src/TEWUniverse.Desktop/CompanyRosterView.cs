using System.Collections;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
using TEWUniverse.Studio;

namespace TEWUniverse.Desktop;

public partial class MainWindow
{
    private void AddCompanyCsvAndRoster()
    {
        if (_session == null || _selectedExisting == null) return;

        ExistingFieldsPanel.Children.Add(new TextBlock {
            Text = "Use Export Record CSV and Import Record CSV in the top toolbar " +
                "to update the selected company. Protected links and roster contracts " +
                "are not changed through company CSV imports.",
            Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        });

        var content = CreateCompanySection("Current Roster  •  tblContract", true);
        content.Children.Add(new TextBlock {
            Text = "Current contracted workers for the selected company. " +
                "This is linked from tblContract, not historical tblCV employment. " +
                "Open a worker to edit their contracts; importing company CSV will " +
                "not create, remove, or reassign workers.",
            Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });

        try
        {
            var roster = _session.ReadCompanyRoster(_selectedExisting.Id);
            var view = CollectionViewSource.GetDefaultView(roster);
            var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 9) };
            var count = new TextBlock {
                Text = $"{roster.Count:N0} current workers",
                FontWeight = FontWeights.SemiBold, Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 18, 6)
            };
            bar.Children.Add(count);
            var search = new TextBox {
                Width = 230, ToolTip = "Filter current company roster by worker name or ID",
                Style = (Style)FindResource("FormTextBox"),
                Margin = new Thickness(0, 0, 8, 6)
            };
            bar.Children.Add(search);
            search.TextChanged += (_, _) =>
            {
                string term = search.Text.Trim();
                view.Filter = item => item is CompanyRosterMember person &&
                    (person.WorkerName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    person.WorkerUid.ToString(CultureInfo.InvariantCulture)
                        .Contains(term, StringComparison.OrdinalIgnoreCase));
                count.Text = $"{view.Cast<object>().Count():N0} of {roster.Count:N0} current workers";
            };
            content.Children.Add(bar);

            var grid = new DataGrid {
                ItemsSource = view, AutoGenerateColumns = false,
                IsReadOnly = true, CanUserAddRows = false, CanUserDeleteRows = false,
                SelectionMode = DataGridSelectionMode.Single, Height = 270,
                HeadersVisibility = DataGridHeadersVisibility.Column
            };
            grid.Columns.Add(new DataGridTextColumn {
                Header = "Worker", Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                Binding = new Binding(nameof(CompanyRosterMember.WorkerName))
            });
            grid.Columns.Add(new DataGridTextColumn {
                Header = "Worker UID", Width = 105,
                Binding = new Binding(nameof(CompanyRosterMember.WorkerUid))
            });
            grid.Columns.Add(new DataGridTextColumn {
                Header = "Active", Width = 70,
                Binding = new Binding(nameof(CompanyRosterMember.Active))
            });
            grid.Columns.Add(new DataGridTextColumn {
                Header = "Contracts", Width = 85,
                Binding = new Binding(nameof(CompanyRosterMember.ContractCount))
            });
            content.Children.Add(grid);

            var buttons = new WrapPanel { Margin = new Thickness(0, 9, 0, 0) };
            var open = new Button {
                Content = "Open Selected Worker", Padding = new Thickness(13, 7, 13, 7),
                Margin = new Thickness(0, 0, 9, 0),
                Style = (Style)FindResource("ActionButton")
            };
            open.Click += (_, _) =>
            {
                if (grid.SelectedItem is not CompanyRosterMember person) return;
                var worker = _allWorkers.FirstOrDefault(w => w.Id == person.WorkerUid);
                if (worker == null)
                {
                    MessageBox.Show(this, "The linked worker no longer exists in the roster.");
                    return;
                }
                SelectModule("Workers");
                if (_activeModule != "Workers") return;
                SearchBox.Clear();
                LoadWorker(worker);
                FilterWorkers(worker.Id);
            };
            buttons.Children.Add(open);
            var rosterCsv = new Button {
                Content = "Export Roster CSV", Padding = new Thickness(13, 7, 13, 7),
                Style = (Style)FindResource("ActionButton")
            };
            rosterCsv.Click += (_, _) => ExportCompanyRosterCsv(roster);
            buttons.Children.Add(rosterCsv);
            content.Children.Add(buttons);
        }
        catch (Exception ex)
        {
            content.Children.Add(new TextBlock {
                Text = "Roster cannot be verified: " + ex.Message,
                Foreground = Brushes.Orange, TextWrapping = TextWrapping.Wrap
            });
        }
    }

    private void ExportCompanyRosterCsv(IReadOnlyList<CompanyRosterMember> roster)
    {
        if (_selectedExisting == null) return;
        var dialog = new SaveFileDialog {
            Title = "Export current company roster",
            Filter = "CSV files (*.csv)|*.csv", AddExtension = true,
            FileName = "company-" + _selectedExisting.Id + "-current-roster.csv"
        };
        if (dialog.ShowDialog(this) != true) return;
        static string Quoted(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
        var lines = new List<string> {
            "CompanyUID,CompanyName,WorkerUID,WorkerName,Active,ContractCount"
        };
        lines.AddRange(roster.Select(r => string.Join(",", new[] {
            _selectedExisting.Id.ToString(CultureInfo.InvariantCulture),
            Quoted(_selectedExisting.Name),
            r.WorkerUid.ToString(CultureInfo.InvariantCulture),
            Quoted(r.WorkerName),
            r.Active.HasValue ? r.Active.Value.ToString() : "",
            r.ContractCount.ToString(CultureInfo.InvariantCulture)
        })));
        try
        {
            System.IO.File.WriteAllText(dialog.FileName,
                string.Join("\r\n", lines) + "\r\n", new UTF8Encoding(true));
            SetStatus($"Exported {roster.Count} current roster members as a read-only report.");
        }
        catch (Exception ex) { Error(ex); }
    }
}
