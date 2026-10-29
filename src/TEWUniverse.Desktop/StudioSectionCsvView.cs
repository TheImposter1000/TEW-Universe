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
    // A new ExistingEditors-backed module automatically gets CSV tools.
    // Bespoke record editors declare their adapters here. Workers and the
    // diagnostic Tools workspace intentionally have no importable records.
    private bool SectionHasCsvAdapter() =>
        _activeModule is "Awards" or "Relationships" ||
        (_activeModule != "Workers" &&
         ExistingEditors.TryGet(_activeModule, out _));

    private int? CsvSelectedRecordId() => _activeModule switch
    {
        "Awards" => _selectedAward?.Uid,
        "Relationships" => _selectedRelationship?.Uid,
        _ => _selectedExisting?.Id
    };

    private void UpdateSectionCsvButtons()
    {
        if (SectionCsvExportButton == null || SectionCsvImportButton == null)
            return;
        bool enabled = _session != null && SectionHasCsvAdapter();
        SectionCsvExportButton.Visibility = enabled
            ? Visibility.Visible : Visibility.Collapsed;
        SectionCsvImportButton.Visibility = enabled
            ? Visibility.Visible : Visibility.Collapsed;
        bool selected = enabled && CsvSelectedRecordId().HasValue;
        SectionCsvExportButton.IsEnabled = selected;
        SectionCsvImportButton.IsEnabled = selected;
    }

    private IReadOnlyList<StudioCsvField> ReadCurrentCsvFields()
    {
        if (_session == null || CsvSelectedRecordId() is not int uid)
            throw new InvalidOperationException("Select an existing record before using CSV.");
        return _activeModule switch
        {
            "Awards" => _session.ReadAwardCsvSnapshot(_awardCompanySpecific, uid),
            "Relationships" => _session.ReadRelationshipCsvSnapshot(uid),
            _ => _session.ReadSectionCsvSnapshot(_activeModule, uid)
        };
    }

    private void ExportSectionCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_activeModule == "Companies")
        {
            ExportSelectedCompanyCsv_Click(sender, e);
            return;
        }
        if (_session == null || !SectionHasCsvAdapter()) return;
        if (HasEdits())
        {
            MessageBox.Show(this, "Save or discard unsaved record edits before exporting CSV.");
            return;
        }
        try
        {
            var fields = ReadCurrentCsvFields();
            var dialog = new SaveFileDialog {
                Title = "Export " + fields[0].Section + " record CSV",
                Filter = "CSV files (*.csv)|*.csv", AddExtension = true,
                FileName = _activeModule.Replace(" ", "_").ToLowerInvariant() +
                    "-" + fields[0].RecordUid + "-details.csv"
            };
            if (dialog.ShowDialog(this) != true) return;
            File.WriteAllText(dialog.FileName,
                StudioSectionCsvCodec.Export(fields), new UTF8Encoding(true));
            SetStatus($"Exported {fields.Count:N0} fields from selected " +
                $"{_activeModule} record UID {fields[0].RecordUid}. " +
                "Edit only the Value column, not the identifiers.");
        }
        catch (Exception ex) { Error(ex); }
    }

    private void ImportSectionCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_activeModule == "Companies")
        {
            ImportSelectedCompanyCsv_Click(sender, e);
            return;
        }
        if (_session == null || !SectionHasCsvAdapter()) return;
        if (HasEdits())
        {
            MessageBox.Show(this, "Save or discard unsaved record edits before importing CSV.");
            return;
        }
        var picker = new OpenFileDialog {
            Title = "Import existing " + _activeModule + " record CSV",
            Filter = "CSV files (*.csv)|*.csv", CheckFileExists = true
        };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(picker.FileName).Length > 4_000_000)
                throw new InvalidOperationException("CSV exceeds the 4 MB limit.");
            var current = ReadCurrentCsvFields();
            string section = current[0].Section;
            int recordUid = current[0].RecordUid;
            var preview = StudioSectionCsvCodec.Preview(
                File.ReadAllText(picker.FileName), current, section, recordUid);
            if (preview.Changes.Count == 0)
            {
                MessageBox.Show(this, "The selected record already matches this CSV.");
                return;
            }
            if (!ConfirmSectionCsvPreview(preview, section, recordUid)) return;
            if (!OfferOptionalBackupBeforeWrite(
                    "importing " + preview.Changes.Count + " CSV field changes"))
                return;

            int saved = SaveSectionCsvPreview(preview, recordUid);
            _lastSaved = DateTime.Now;
            if (_activeModule == "Awards")
                ReloadAwards(recordUid);
            else if (_activeModule == "Relationships")
                ReloadRelationships(recordUid);
            else
            {
                _availableExistingRecords = _session.ListExistingRecords(_activeModule);
                var again = _availableExistingRecords.FirstOrDefault(x => x.Id == recordUid);
                if (again != null) LoadExistingRecord(again);
                FilterExistingRecords(recordUid);
            }
            RefreshDirty();
            SetStatus($"Imported {saved} field(s) for {_activeModule} record UID {recordUid}. " +
                "Record identity was preserved.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Record CSV Import Rejected",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private int SaveSectionCsvPreview(StudioCsvPreview preview, int uid)
    {
        if (_session == null) throw new InvalidOperationException("No database is open.");
        switch (_activeModule)
        {
            case "Awards":
            {
                var award = _session.ReadAwards(_awardCompanySpecific)
                    .Single(x => x.Uid == uid);
                var fields = preview.Changes.ToDictionary(x => x.Field.Field,
                    x => x.NewValue, StringComparer.OrdinalIgnoreCase);
                return _session.SaveAward(uid, _awardCompanySpecific, award, fields);
            }
            case "Relationships":
            {
                var original = _session.ReadRelationships().Single(x => x.Uid == uid);
                string Value(string field, string fallback) =>
                    preview.Changes.FirstOrDefault(x => x.Field.Field == field)?.NewValue ??
                    fallback;
                int Code(string field, int originalValue) =>
                    int.Parse(Value(field, originalValue.ToString(CultureInfo.InvariantCulture)),
                        CultureInfo.InvariantCulture);
                bool mentor = bool.Parse(Value("Mentor", original.Mentor.ToString()));
                _session.UpdateRelationship(original,
                    Code("P_Rel", original.Personal),
                    Code("R_Rel", original.Romantic),
                    Code("F_Rel", original.Family), mentor);
                return preview.Changes.Count;
            }
            default:
            {
                if (!ExistingEditors.TryGet(_activeModule, out _))
                    throw new InvalidOperationException("No verified section CSV writer exists.");
                var edits = preview.Changes.Select(change =>
                    new FieldEdit(change.Field.Table, change.Field.KeyColumn,
                        change.Field.Field,
                        Type.GetType("System." + change.Field.FieldType)
                            ?? throw new InvalidOperationException("Unsupported CSV field type."),
                        change.NewValue)).ToArray();
                return _session.SaveExistingRecord(_activeModule, uid, edits);
            }
        }
    }

    private bool ConfirmSectionCsvPreview(
        StudioCsvPreview preview, string section, int uid)
    {
        var table = new DataTable();
        foreach (string columnTitle in new[] { "Table", "Field", "Original value", "New value" })
            table.Columns.Add(columnTitle);
        foreach (var change in preview.Changes)
            table.Rows.Add(change.Field.Table, change.Field.Field,
                change.Field.OriginalValue, change.NewValue);

        var window = new Window {
            Owner = this, Title = "Review " + section + " CSV Import",
            Width = 880, Height = 590, MinWidth = 600, MinHeight = 350,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(15, 30, 50)),
            Foreground = Brushes.White
        };
        var layout = new DockPanel { Margin = new Thickness(17) };
        window.Content = layout;
        var heading = new TextBlock {
            Text = $"{section}, record UID {uid}: {preview.Changes.Count} field change(s). " +
                "All source values, row identities and field types have been checked. " +
                "Only the selected existing record will be updated. " +
                "No database write happens until you confirm.",
            Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 13)
        };
        DockPanel.SetDock(heading, Dock.Top);
        layout.Children.Add(heading);
        var buttons = new WrapPanel {
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 13, 0, 0)
        };
        DockPanel.SetDock(buttons, Dock.Bottom);
        layout.Children.Add(buttons);
        var cancel = new Button {
            Content = "Cancel", Margin = new Thickness(0, 0, 9, 0),
            Padding = new Thickness(16, 8, 16, 8),
            Style = (Style)FindResource("ActionButton")
        };
        cancel.Click += (_, _) => window.DialogResult = false;
        buttons.Children.Add(cancel);
        var apply = new Button {
            Content = "Import Changes", Padding = new Thickness(16, 8, 16, 8),
            Style = (Style)FindResource("BlueButton")
        };
        apply.Click += (_, _) => window.DialogResult = true;
        buttons.Children.Add(apply);
        layout.Children.Add(new DataGrid {
            ItemsSource = table.DefaultView, AutoGenerateColumns = true,
            IsReadOnly = true, CanUserAddRows = false,
            CanUserDeleteRows = false, MinColumnWidth = 110
        });
        return window.ShowDialog() == true;
    }
}
