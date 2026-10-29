using System.Windows;
using TEWUniverse.Studio;

namespace TEWUniverse.Desktop;

public partial class MainWindow
{
    /// <summary>
    /// A user-facing choice for database writes: Yes creates a backup now,
    /// No proceeds without a backup, and Cancel leaves the database unchanged.
    /// Neither a preexisting snapshot nor the lack of one is a prerequisite.
    /// </summary>
    private bool OfferOptionalBackupBeforeWrite(string operation, Window? owner = null)
    {
        if (_session == null) return false;
        var dialogOwner = owner ?? this;
        var answer = MessageBox.Show(dialogOwner,
            $"Before {operation}, would you like to back up the active TEW IX database?\n\n" +
            "YES — Create a backup now, then continue.\n" +
            "NO — Continue without creating a backup.\n" +
            "CANCEL — Stop; make no changes.\n\n" +
            "Backups are recommended but optional. " +
            "Run TEW IX Data Check after testing database changes.",
            "Optional Database Backup", MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        if (answer == MessageBoxResult.No) return true;
        if (answer != MessageBoxResult.Yes) return false;
        return CreateBackupWithFeedback(dialogOwner);
    }
}
