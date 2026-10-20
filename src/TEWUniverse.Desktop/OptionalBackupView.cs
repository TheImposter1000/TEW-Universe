using System.Windows;

namespace TEWUniverse.Desktop;

public partial class MainWindow
{
    /// <summary>
    /// A user-facing choice for low-impact record writes. The application never
    /// silently creates backups or requires one as a prerequisite: Yes creates
    /// a backup now, No proceeds knowingly without one, Cancel makes no change.
    /// Destructive full-title-lineage rebuilds retain their separate safeguards.
    /// </summary>
    private bool OfferOptionalBackupBeforeWrite(string operation)
    {
        if (_session == null) return false;
        if (!string.IsNullOrWhiteSpace(_lastManualBackup) &&
            File.Exists(_lastManualBackup) &&
            AppSettings.SamePath(_lastBackupDatabase, _session.PathOnDisk))
            return true;

        var answer = MessageBox.Show(this,
            $"Before {operation}, would you like to back up the active TEW IX database?\n\n" +
            "YES — Create a backup now, then continue.\n" +
            "NO — Continue without creating a backup.\n" +
            "CANCEL — Stop; make no changes.\n\n" +
            "Backups are recommended, but are optional for this operation. " +
            "Run TEW IX Data Check after testing database changes.",
            "Optional Database Backup", MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        if (answer == MessageBoxResult.Cancel) return false;
        if (answer == MessageBoxResult.No) return true;
        Backup_Click(this, new RoutedEventArgs());
        // Backups can fail or the user can cancel the existing unsaved-edit
        // warning; in either case, never claim that a backup exists.
        return !string.IsNullOrWhiteSpace(_lastManualBackup) &&
            File.Exists(_lastManualBackup) &&
            AppSettings.SamePath(_lastBackupDatabase, _session.PathOnDisk);
    }
}
