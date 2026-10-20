using System.Globalization;
using System.Windows;

namespace TEWUniverse.Desktop;

public partial class MainWindow
{
    private void AdvancedImportNames_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null || _selectedExisting == null ||
            _activeModule != "Titles") return;
        if (AdvancedGenerationMode.SelectedIndex != 1)
        {
            MessageBox.Show(this,
                "Switch Lineage operation to 'Build new full history' before " +
                "importing name-only champions. Existing-slot replacement needs " +
                "linked roster workers and does not create new reigns.");
            return;
        }
        int members = (int)CurrentTitleFormat();
        if (!int.TryParse(AdvancedStartYear.Text, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int from) ||
            !int.TryParse(AdvancedEndYear.Text, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int to) ||
            from < 1800 || to > 2100 || from > to)
        {
            MessageBox.Show(this, "Enter valid history start/end years (1800–2100) first.");
            return;
        }

        var dialog = new HistoricalNamesImportDialog(members) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try
        {
            if (dialog.Entries.Count + _advancedParticipants.Count > 500)
                throw new InvalidOperationException(
                    "The generator supports at most 500 champion/team entries.");
            var additions = new List<LineageParticipant>(dialog.Entries.Count);
            foreach (var entry in dialog.Entries)
            {
                var names = entry.Names;
                bool alreadyPresent = _advancedParticipants.Any(p => p.WorkerId == -1 &&
                    p.DisplayName.Equals(names[0], StringComparison.OrdinalIgnoreCase) &&
                    (members == 1 ||
                        p.Partner2Uid == "-1" &&
                        p.Partner2Name.Equals(names[1], StringComparison.OrdinalIgnoreCase)) &&
                    (members < 3 ||
                        p.Partner3Uid == "-1" &&
                        p.Partner3Name.Equals(names[2], StringComparison.OrdinalIgnoreCase)));
                if (alreadyPresent)
                    throw new InvalidOperationException(
                        $"Already entered as a name-only champion/team: {string.Join(" / ", names)}");
                additions.Add(new LineageParticipant {
                    DisplayName = names[0],
                    WorkerId = -1,
                    Partner2Uid = members >= 2 ? "-1" : "",
                    Partner2Name = members >= 2 ? names[1] : "",
                    Partner3Uid = members == 3 ? "-1" : "",
                    Partner3Name = members == 3 ? names[2] : "",
                    FirstYear = from.ToString(CultureInfo.InvariantCulture),
                    LastYear = to.ToString(CultureInfo.InvariantCulture),
                    RequiredReigns = "1",
                    WinYears = from == to
                        ? from.ToString(CultureInfo.InvariantCulture) : "",
                    ReignLengths = ""
                });
            }
            foreach (var entry in additions) _advancedParticipants.Add(entry);
            ResetAdvancedDraft();
            AdvancedCandidatesGrid.Items.Refresh();
            AdvancedGenerationStatus.Text =
                $"Imported {additions.Count} named {(members == 1 ? "champion(s)" :
                    members == 2 ? "tag team(s)" : "trios team(s)")}. " +
                "Every member is name-only (UID -1) and is not created in tblWorker. " +
                "Set winning years and reign counts in the table, then Generate Preview.";
            SetStatus($"Imported {additions.Count} historical champion/team entries.");
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, ex.Message,
                "Historical Name Import", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
