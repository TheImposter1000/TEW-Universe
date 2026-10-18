using System.Windows;
using Microsoft.Win32;
using TEWUniverse.Studio;

namespace TEWUniverse.Desktop;

public partial class SettingsWindow : Window
{
    internal AppSettings? ResultSettings { get; private set; }

    internal SettingsWindow(AppSettings current)
    {
        InitializeComponent();
        DefaultPath.Text = current.DefaultDatabasePath;
        CopyPath.Text = current.CurrentCopyPath;
        BackupPath.Text = current.BackupFolderPath;
        PicturePath.Text = current.WorkerPicturesFolderPath;
        RememberPassword.IsChecked = current.RememberDatabasePassword;
    }

    private static string? ChooseMdb(Window owner, string current)
    {
        var dialog = new OpenFileDialog { Filter = "Microsoft Access database (*.mdb)|*.mdb" };
        if (File.Exists(current)) dialog.FileName = current;
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    private void DefaultBrowse_Click(object sender, RoutedEventArgs e)
    {
        var path = ChooseMdb(this, DefaultPath.Text);
        if (path != null) DefaultPath.Text = path;
    }

    private void CopyBrowse_Click(object sender, RoutedEventArgs e)
    {
        var path = ChooseMdb(this, CopyPath.Text);
        if (path != null) CopyPath.Text = path;
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
            var next = new AppSettings {
                DefaultDatabasePath = DefaultPath.Text,
                CurrentCopyPath = CopyPath.Text,
                BackupFolderPath = BackupPath.Text,
                WorkerPicturesFolderPath = PicturePath.Text,
                RememberDatabasePassword = RememberPassword.IsChecked == true
            };
            next.NormalizeAndValidate();
            if (!string.IsNullOrWhiteSpace(next.CurrentCopyPath) && File.Exists(next.CurrentCopyPath) &&
                MessageBox.Show(this, "Confirm that Current Copy is an EDITABLE copy, not the original TEW IX scenario.",
                    "Protect original database", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                != MessageBoxResult.Yes) return;
            ResultSettings = next;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Invalid settings", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
