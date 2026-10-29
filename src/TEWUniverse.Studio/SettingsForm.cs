using System.Drawing;
using System.Windows.Forms;

namespace TEWUniverse.Studio;

internal sealed class SettingsForm : Form
{
    private readonly TextBox _defaultPath = new();
    private readonly TextBox _currentPath = new();
    private readonly TextBox _backupPath = new();
    private readonly CheckBox _rememberPassword = new();
    public AppSettings? ResultSettings { get; private set; }

    public SettingsForm(AppSettings settings)
    {
        Text = "Settings | TEW Universe Studio";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(830, 447);
        MinimumSize = new Size(830, 447);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(20, 30, 44);
        ForeColor = Color.White;

        var title = new Label {
            Text = "DATABASE PATHS", AutoSize = false, Location = new Point(22, 18),
            Size = new Size(700, 31), ForeColor = Color.FromArgb(60, 205, 171),
            Font = new Font("Segoe UI", 12, FontStyle.Bold)
        };
        Controls.Add(title);

        _defaultPath.Text = settings.DefaultDatabasePath;
        _currentPath.Text = settings.CurrentCopyPath;
        _backupPath.Text = settings.BackupFolderPath;

        AddRow(63, "Default Database", _defaultPath,
            "Original TEW IX scenario database. Used as the source for Import. Never edited.",
            () => BrowseMdb(_defaultPath, "Choose original TEW IX scenario database"));
        AddRow(141, "Current Copy", _currentPath,
            "Working MDB that is edited and reopened on startup. You can type a new destination.",
            () => BrowseMdb(_currentPath, "Choose an existing editable working copy"));
        AddRow(219, "Backup Folder", _backupPath,
            "Location for manual Create Backup snapshots. Blank uses the copy's Backups folder.",
            () => BrowseFolder());

        _rememberPassword.Text = "Remember database password on this Windows account";
        _rememberPassword.Checked = settings.RememberDatabasePassword;
        _rememberPassword.SetBounds(177, 304, 570, 29);
        _rememberPassword.AutoSize = false;
        _rememberPassword.ForeColor = Color.White;
        Controls.Add(_rememberPassword);
        var passwordNote = new Label {
            Text = "Optional. Encrypted using Windows DPAPI. Stored only on this PC, never in GitHub or settings.json.",
            Left = 177, Top = 338, Width = 630, Height = 40,
            ForeColor = Color.FromArgb(177, 190, 209), Font = new Font("Segoe UI", 8.5f)
        };
        Controls.Add(passwordNote);

        var save = new Button {
            Text = "Save Settings", Location = new Point(661, 394), Size = new Size(145, 35),
            BackColor = Color.FromArgb(60, 205, 171), FlatStyle = FlatStyle.Flat,
            ForeColor = Color.FromArgb(15, 35, 45), Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        save.Click += (_, _) => SaveSettings();
        Controls.Add(save);

        var cancel = new Button {
            Text = "Cancel", Location = new Point(552, 394), Size = new Size(101, 35),
            BackColor = Color.FromArgb(43, 59, 77), ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat, DialogResult = DialogResult.Cancel
        };
        Controls.Add(cancel);
        AcceptButton = save;
        CancelButton = cancel;
    }

    private void AddRow(int top, string labelText, TextBox input, string note, Action browse)
    {
        var label = new Label {
            Text = labelText, Left = 22, Top = top, Width = 150, Height = 23,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        input.SetBounds(177, top - 2, 519, 31);
        input.BackColor = Color.White;
        input.ForeColor = Color.Black;
        var button = new Button {
            Text = "Browse...", Left = 703, Top = top - 2, Width = 103, Height = 31,
            BackColor = Color.FromArgb(43, 59, 77), ForeColor = Color.White, FlatStyle = FlatStyle.Flat
        };
        button.Click += (_, _) => browse();
        var hint = new Label {
            Text = note, Left = 177, Top = top + 34, Width = 629, Height = 34,
            ForeColor = Color.FromArgb(177, 190, 209), Font = new Font("Segoe UI", 8.5f)
        };
        Controls.AddRange(new Control[] { label, input, button, hint });
    }

    private void BrowseMdb(TextBox field, string caption)
    {
        using var dialog = new OpenFileDialog {
            Title = caption, Filter = "Microsoft Access database (*.mdb)|*.mdb",
            CheckFileExists = true
        };
        if (File.Exists(field.Text))
        {
            dialog.FileName = field.Text;
            dialog.InitialDirectory = Path.GetDirectoryName(field.Text);
        }
        if (dialog.ShowDialog(this) == DialogResult.OK) field.Text = dialog.FileName;
    }

    private void BrowseFolder()
    {
        using var dialog = new FolderBrowserDialog {
            Description = "Choose where optional database backups will be stored",
            UseDescriptionForTitle = true
        };
        if (Directory.Exists(_backupPath.Text)) dialog.SelectedPath = _backupPath.Text;
        if (dialog.ShowDialog(this) == DialogResult.OK) _backupPath.Text = dialog.SelectedPath;
    }

    private void SaveSettings()
    {
        try
        {
            var candidate = new AppSettings {
                DefaultDatabasePath = _defaultPath.Text,
                CurrentCopyPath = _currentPath.Text,
                BackupFolderPath = _backupPath.Text,
                RememberDatabasePassword = _rememberPassword.Checked
            };
            candidate.NormalizeAndValidate();
            if (candidate.CurrentCopyPath.Length > 0 && File.Exists(candidate.CurrentCopyPath) &&
                MessageBox.Show(this,
                    "The Current Copy points to an existing database. This file will be modified when you save worker changes. " +
                    "Confirm that it is a working copy and NOT the original TEW IX scenario.",
                    "Confirm editable database", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            ResultSettings = candidate;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Invalid settings", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
