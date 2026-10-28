using System.Data;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using TEWUniverse.Studio;

namespace TEWUniverse.Desktop;

public partial class MainWindow
{
    private void ExportSelectedCompanyCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null || _selectedExisting == null || _activeModule != "Companies") return;
        if (HasEdits())
        {
            MessageBox.Show(this, "Save or discard pending company edits before exporting a CSV.");
            return;
        }
        try
        {
            int id = _selectedExisting.Id;
            var records = _session.ReadCompanyCsvSnapshot(id);
            var dialog = new SaveFileDialog {
                Title = "Export selected company and verified linked settings",
                Filter = "CSV files (*.csv)|*.csv", AddExtension = true,
                FileName = $"company-{id}-details.csv"
            };
            if (dialog.ShowDialog(this) != true) return;
            File.WriteAllText(dialog.FileName, CompanyCsvCodec.Export(records),
                new UTF8Encoding(true));
            SetStatus($"Exported {records.Count:N0} scalar fields for " +
                $"{_selectedExisting.Name} (UID {id}). Linked rosters are separate.");
        }
        catch (Exception ex) { Error(ex); }
    }

    private void ImportSelectedCompanyCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null || _selectedExisting == null || _activeModule != "Companies") return;
        if (HasEdits())
        {
            MessageBox.Show(this, "Save or discard pending company edits before importing a CSV.");
            return;
        }
        var dialog = new OpenFileDialog {
            Title = "Import company CSV into the SELECTED company only",
            Filter = "CSV files (*.csv)|*.csv", CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 2_000_000)
                throw new InvalidOperationException("Company CSV exceeds the 2 MB import limit.");
            int companyId = _selectedExisting.Id;
            var current = _session.ReadCompanyCsvSnapshot(companyId);
            var preview = CompanyCsvCodec.Preview(
                File.ReadAllText(dialog.FileName), current, companyId);
            if (preview.Changes.Count == 0)
            {
                SetStatus("Company CSV matches the selected company's current data. No changes.");
                MessageBox.Show(this, "There are no edited company values to import.");
                return;
            }

            var table = new DataTable("Proposed company field changes");
            table.Columns.Add("Section");
            table.Columns.Add("Record ID");
            table.Columns.Add("Field");
            table.Columns.Add("Existing Value");
            table.Columns.Add("New Value");
            foreach (var change in preview.Changes)
            {
                var edit = change.Edit;
                var existing = current.Single(c => c.Table.Equals(edit.Table,
                    StringComparison.OrdinalIgnoreCase) && c.RecordId == change.RecordId &&
                    c.Field.Equals(edit.Field, StringComparison.OrdinalIgnoreCase));
                table.Rows.Add(edit.Table, change.RecordId.ToString(CultureInfo.InvariantCulture),
                    edit.Field, existing.OriginalValue, edit.NewText);
            }

            var window = new Window {
                Owner = this,
                Title = "Review Company CSV Import | " + _selectedExisting.Name,
                Width = 950, Height = 590, MinWidth = 650, MinHeight = 370,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = new SolidColorBrush(Color.FromRgb(16, 33, 54)),
                Foreground = Brushes.White
            };
            var layout = new Grid { Margin = new Thickness(18) };
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            window.Content = layout;

            var heading = new TextBlock {
                Text = $"Company: {_selectedExisting.Name} (UID {companyId})\n" +
                    $"{preview.Changes.Count} value change(s) across {preview.VerifiedRows} verified CSV rows. " +
                    "Only this company's existing scalar fields will be updated. " +
                    "The roster, contracts, worker records and links will not change.",
                Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap,
                FontSize = 13, Margin = new Thickness(0, 0, 0, 15)
            };
            Grid.SetRow(heading, 0); layout.Children.Add(heading);
            var changes = new DataGrid {
                ItemsSource = table.DefaultView, IsReadOnly = true,
                AutoGenerateColumns = true,
                CanUserAddRows = false, CanUserDeleteRows = false,
                MinColumnWidth = 120
            };
            Grid.SetRow(changes, 1); layout.Children.Add(changes);
            var buttons = new WrapPanel {
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            Grid.SetRow(buttons, 2); layout.Children.Add(buttons);
            var cancel = new Button {
                Content = "Cancel", Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(17, 9, 17, 9),
                Style = (Style)FindResource("ActionButton")
            };
            cancel.Click += (_, _) => window.DialogResult = false;
            buttons.Children.Add(cancel);
            var apply = new Button {
                Content = "Apply Changes to Selected Company",
                Padding = new Thickness(17, 9, 17, 9),
                Style = (Style)FindResource("BlueButton")
            };
            apply.Click += (_, _) => window.DialogResult = true;
            buttons.Children.Add(apply);
            if (window.ShowDialog() != true) return;

            if (!OfferOptionalBackupBeforeWrite(
                    "importing " + preview.Changes.Count + " company CSV changes"))
                return;
            // CompanyFieldChanges validates identity, column types, and all
            // row ownership again, and commits everything in one transaction.
            int updated = _session.SaveCompanyFields(companyId, preview.Changes);
            var refreshed = _session.ListExistingRecords("Companies")
                .FirstOrDefault(c => c.Id == companyId);
            if (refreshed != null) LoadExistingRecord(refreshed);
            FilterExistingRecords(companyId);
            RefreshDirty();
            SetStatus($"Applied {updated} verified company field change(s) " +
                $"to company UID {companyId}. No roster or contract links were modified.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Company CSV Import Rejected",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
