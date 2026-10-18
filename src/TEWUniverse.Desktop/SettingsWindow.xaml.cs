using System.Windows;
using Microsoft.Win32;
using TEWUniverse.Studio;

namespace TEWUniverse.Desktop;

public partial class SettingsWindow : Window
{
    internal AppSettings? ResultSettings { get; private set; }
    private readonly AppSettings _original;


    internal SettingsWindow(AppSettings current)
    {
        InitializeComponent();
        _original = current.Copy();
        ActivePath.Text = current.ActiveDatabasePath;
        BackupPath.Text = current.BackupFolderPath;
        PicturePath.Text = current.WorkerPicturesFolderPath;
        RememberPassword.IsChecked = current.RememberDatabasePassword;
    }

    private void ActiveBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog {
            Title = "Select the TEW IX scenario database to edit directly",
            Filter = "Microsoft Access database (*.mdb)|*.mdb"
        };
        if (File.Exists(ActivePath.Text)) dialog.FileName = ActivePath.Text;
        if (dialog.ShowDialog(this) == true) ActivePath.Text = dialog.FileName;
    }

    private void BackupBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose a folder for on-demand backups" };
        if (Directory.Exists(BackupPath.Text)) dialog.InitialDirectory = BackupPath.Text;
        if (dialog.ShowDialog(this) == true) BackupPath.Text = dialog.FolderName;
    }

    private void PictureBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose the TEW IX People picture folder" };
        if (Directory.Exists(PicturePath.Text)) dialog.InitialDirectory = PicturePath.Text;
        if (dialog.ShowDialog(this) == true) PicturePath.Text = dialog.FolderName;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Preserve app-only title categories when other settings change.
            var next = _original.Copy();
            next.ActiveDatabasePath = ActivePath.Text;
            next.BackupFolderPath = BackupPath.Text;
            next.WorkerPicturesFolderPath = PicturePath.Text;
            next.RememberDatabasePassword = RememberPassword.IsChecked == true;
            next.NormalizeAndValidate();
            if (!string.IsNullOrWhiteSpace(next.ActiveDatabasePath))
            {
                if (!File.Exists(next.ActiveDatabasePath))
                    throw new InvalidOperationException("The Active Database MDB does not exist.");
                if (Path.GetFileName(next.ActiveDatabasePath)
                    .Equals("TEW9Save.mdb", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Do not select a TEW IX save-game database.");
                if (MessageBox.Show(this,
                    "Save Changes will modify this MDB IN PLACE:\n\n" + next.ActiveDatabasePath +
                    "\n\nClose the TEW IX database editor first. Create Backup remains manual. Continue?",
                    "Confirm direct database editing", MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;
            }
            ResultSettings = next;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Invalid settings",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
