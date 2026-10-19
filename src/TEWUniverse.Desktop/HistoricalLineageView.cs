using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using TEWUniverse.Studio;

namespace TEWUniverse.Desktop;

public partial class MainWindow
{
    private bool HasHistoricalWriteBackup()
    {
        if (!string.IsNullOrWhiteSpace(_lastManualBackup) &&
            File.Exists(_lastManualBackup) &&
            _session != null &&
            AppSettings.SamePath(_lastBackupDatabase, _session.PathOnDisk))
            return true;
        MessageBox.Show(this,
            "Choose CREATE BACKUP before changing historical title lineage. " +
            "Only a backed-up scenario should be edited, and the result should " +
            "be checked using TEW IX Data Check.",
            "Back up the scenario first", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private HistoricalReignDraft DraftFromSelected(DataRow row)
    {
        var table = row.Table;
        string Name(string a, string b) =>
            table.Columns.Contains(a) ? a :
            table.Columns.Contains(b) ? b :
            throw new InvalidOperationException("Unknown holder name column: " + a);
        var names = new[] { Name("Holder_Name1", "HolderName1"),
            Name("Holder_Name2", "HolderName2"), Name("Holder_Name3", "HolderName3") };
        int[] holders = { -1, -1, -1 };
        string[] labels = { "", "", "" };
        for (int i = 0; i < 3; i++)
        {
            string field = "Holder" + (i + 1);
            holders[i] = row[field] is DBNull ? -1 :
                Convert.ToInt32(row[field], CultureInfo.InvariantCulture);
            labels[i] = row[names[i]] is DBNull ? "" :
                Convert.ToString(row[names[i]], CultureInfo.InvariantCulture) ?? "";
        }
        if (row["BeltCaptured"] is not DateTime captured ||
            row["BeltLost"] is not DateTime lost)
            throw new InvalidOperationException("The recorded dates are missing.");
        return new HistoricalReignDraft(holders, labels, captured, lost,
            Convert.ToInt16(row["Defences"], CultureInfo.InvariantCulture));
    }

    private bool ReadyForHistoricalEdit()
    {
        if (_session == null || _selectedExisting == null || _activeModule != "Titles")
            return false;
        if (HasEdits())
        {
            MessageBox.Show(this, "Save or discard championship detail edits before " +
                "modifying title lineage.");
            return false;
        }
        return HasHistoricalWriteBackup();
    }

    private void EditHistoricalReign_Click(object sender, RoutedEventArgs e)
    {
        if (!ReadyForHistoricalEdit()) return;
        try
        {
            if (TitleLineageGrid.SelectedItem is not DataRowView view)
            {
                MessageBox.Show(this, "Select the existing reign to edit in Title Lineage.");
                return;
            }
            var row = view.Row;
            var draft = DraftFromSelected(row);
            int uid = Convert.ToInt32(row["UID"], CultureInfo.InvariantCulture);
            int order = Convert.ToInt32(row["BeltOrder"], CultureInfo.InvariantCulture);
            var dialog = new HistoricalReignDialog(
                _allWorkers, (int)CurrentTitleFormat(), IsAnnualTournament(), draft)
                { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Draft == null) return;
            var proposed = dialog.Draft;
            string participants = string.Join(" / ",
                proposed.HolderNames.Where(n => !string.IsNullOrWhiteSpace(n)));
            if (MessageBox.Show(this,
                $"Change champions in existing reign #{order} of {_selectedExisting!.Name}?\n\n" +
                $"{participants}\n\n" +
                "The existing dates, UID, defenses and record identity are preserved. " +
                "Name-only NPCs have no tblWorker record. This writes to the active MDB.",
                "Confirm Historical Champions", MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            _session!.EditHistoricalReign(_selectedExisting.Id, uid, order,
                proposed, (int)CurrentTitleFormat(), IsAnnualTournament());
            _lastSaved = DateTime.Now;
            LoadTitleLineage();
            SetStatus($"Updated historical champion names for reign #{order}.");
        }
        catch (Exception ex) { Error(ex); }
    }

    private void InsertHistoricalReign_Click(object sender, RoutedEventArgs e)
    {
        if (!ReadyForHistoricalEdit()) return;
        try
        {
            var history = _session!.ReadTitleLineage(_selectedExisting!.Id);
            if (history.LinkField != "BeltUID" || history.OrderField != "BeltOrder")
                throw new InvalidOperationException("This title history has no verified ordering.");
            var dialog = new HistoricalReignDialog(
                _allWorkers, (int)CurrentTitleFormat(), IsAnnualTournament(), null)
                { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Draft == null) return;
            var draft = dialog.Draft;
            string participants = string.Join(" / ",
                draft.HolderNames.Where(n => !string.IsNullOrWhiteSpace(n)));
            if (MessageBox.Show(this,
                $"INSERT ONE NEW historical reign into {_selectedExisting.Name}?\n\n" +
                $"Champions: {participants}\n" +
                $"Captured: {StudioDateFormat.Format(draft.Captured)}\n" +
                (IsAnnualTournament()
                    ? "Annual tournament victory\n"
                    : $"Lost: {StudioDateFormat.Format(draft.Lost)}\n") +
                "\nExisting dates, champions, UIDs and Recordnames will remain unchanged. " +
                "BeltOrder for this title will be renumbered chronologically in one transaction. " +
                "Conflicting/overlapping dates will be rejected. The new person is NOT " +
                "added to the worker or tag-team database tables.\n\nProceed?",
                "Confirm Insert Historical Reign", MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            int uid = _session.InsertHistoricalReign(_selectedExisting.Id, draft,
                (int)CurrentTitleFormat(), IsAnnualTournament(), history.Rows.Rows.Count);
            _lastSaved = DateTime.Now;
            LoadTitleLineage();
            SetStatus($"Inserted historical reign UID {uid} for {_selectedExisting.Name}.");
        }
        catch (Exception ex) { Error(ex); }
    }
}
