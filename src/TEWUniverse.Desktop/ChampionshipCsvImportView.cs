using System.Data;
using System.Globalization;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using TEWUniverse.Studio;

namespace TEWUniverse.Desktop;

/// <summary>
/// A CSV never writes directly. Loading it produces a normal reviewable
/// Advanced Generator draft. The existing apply path handles confirmation,
/// optional backup, schema safeguards, and transactional MDB writes.
/// </summary>
public partial class MainWindow
{
    private void ImportChampionshipCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null || _selectedExisting == null || _activeModule != "Titles")
            return;
        if (HasEdits())
        {
            MessageBox.Show(this,
                "Save or discard the unsaved championship details before importing history.");
            return;
        }

        var dialog = new OpenFileDialog {
            Title = "Import a TEW Universe Studio Championship History CSV",
            Filter = "CSV files (*.csv)|*.csv",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 2_000_000)
                throw new InvalidOperationException("CSV exceeds the 2 MB import limit.");

            int titleId = _selectedExisting.Id;
            var saved = _session.ReadTitleLineage(titleId);
            if (saved.LinkField != "BeltUID" || saved.OrderField != "BeltOrder")
                throw new InvalidOperationException(
                    "This title history has no verified BeltUID/BeltOrder schema.");
            var existing = ExistingSlots(saved);
            int format = (int)CurrentTitleFormat();
            bool annual = IsAnnualTournament();

            var parsed = ChampionshipCsvImport.Parse(
                File.ReadAllText(dialog.FileName), titleId, format, annual,
                _allWorkers.Select(w => w.Id).ToHashSet(), existing);
            if (!parsed.Targeted && parsed.Reigns.Count == 0 ||
                parsed.Targeted && parsed.Changes.Count == 0)
                throw new InvalidOperationException("CSV has no valid title history records.");

            // Switch only after the CSV has passed validation. This prevents
            // a canceled or invalid file from modifying the current draft.
            AdvancedLineageTab_Click(this, new RoutedEventArgs());
            ResetAdvancedDraft();
            _advancedDraftTitleId = titleId;
            _advancedExistingSlots = existing.Count;
            _advancedDraftIsAnnualTournament = annual;
            _advancedFullRebuild = !parsed.Targeted;
            _advancedSlotMode = parsed.Targeted;
            _advancedDraft = parsed.Reigns.ToList();
            _advancedSlotPatches = parsed.Changes.ToList();
            _advancedEmploymentWarnings = 0;

            var preview = new DataTable("CSV Championship History Preview");
            preview.Columns.Add("Reign #", typeof(int));
            preview.Columns.Add("Year", typeof(int));
            preview.Columns.Add("Champion(s)", typeof(string));
            preview.Columns.Add("Worker UID(s)", typeof(string));
            preview.Columns.Add("Captured", typeof(DateTime));
            preview.Columns.Add("Lost", typeof(DateTime));
            preview.Columns.Add("Defenses", typeof(short));
            preview.Columns.Add("Action", typeof(string));

            if (parsed.Targeted)
            {
                foreach (var p in parsed.Changes)
                    preview.Rows.Add(p.Order, p.Captured.Year,
                        string.Join(" / ", new[] { p.Name1, p.Name2, p.Name3 }
                            .Where(n => !string.IsNullOrWhiteSpace(n))),
                        string.Join(", ", new[] { p.Holder1, p.Holder2, p.Holder3 }
                            .Take(format)),
                        p.Captured, p.Lost, p.Defences,
                        "Replace name-only holders; preserve existing reign UID " + p.Uid);
            }
            else
            {
                foreach (var p in parsed.Reigns)
                    preview.Rows.Add(p.Order, p.Captured.Year,
                        string.Join(" / ", new[] { p.HolderName1, p.HolderName2, p.HolderName3 }
                            .Where(n => !string.IsNullOrWhiteSpace(n))),
                        string.Join(", ", new[] { p.Holder1, p.Holder2, p.Holder3 }
                            .Take(format)),
                        p.Captured, p.Lost, p.Defences,
                        "Full title history rebuild");
            }

            AdvancedPreviewGrid.ItemsSource = preview.DefaultView;
            AdvancedApplyButton.IsEnabled = true;
            AdvancedExportButton.IsEnabled = true;
            string count = (parsed.Targeted ? parsed.Changes.Count : parsed.Reigns.Count)
                .ToString("N0", CultureInfo.InvariantCulture);
            AdvancedGenerationStatus.Text = parsed.Targeted
                ? $"CSV LOADED • {count} targeted holder substitution(s) for {_selectedExisting.Name}. " +
                  $"The {existing.Count} saved reigns, their dates, defenses and record UIDs " +
                  "are preserved. Only the selected name-only placeholders can change. " +
                  "Employment history was not checked from this CSV. Review before importing."
                : $"CSV LOADED • {count} reign(s) for {_selectedExisting.Name}. " +
                  $"Import will replace the title's {existing.Count} existing reign(s) " +
                  "with these CSV records, creating new history UIDs. " +
                  "Other championships and workers are not modified. " +
                  "Employment history was not checked from this CSV. Review before importing.";
            ShowAdvancedWorkspace(true);
            SetStatus($"Validated {count} championship CSV records for {_selectedExisting.Name}. " +
                "Preview only; the MDB has not changed.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message,
                "Championship CSV Import Rejected",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
