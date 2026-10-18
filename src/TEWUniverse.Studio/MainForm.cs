using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace TEWUniverse.Studio;

internal sealed class MainForm : Form
{
    private readonly Color _bg = Color.FromArgb(18, 26, 38);
    private readonly Color _panel = Color.FromArgb(28, 39, 55);
    private readonly Color _accent = Color.FromArgb(60, 205, 171);
    private readonly Color _muted = Color.FromArgb(174, 187, 204);
    private DatabaseSession? _session;
    private WorkerItem? _currentWorker;
    private List<WorkerItem> _workers = new();
    private List<WorkerItem> _visible = new();
    private readonly ListBox _workerList = new();
    private readonly TextBox _search = new();
    private readonly Label _workerCount = new();
    private readonly Label _header = new();
    private readonly Label _status = new();
    private readonly TabControl _tabs = new();
    private readonly Dictionary<string, DataGridView> _grids = new();
    private readonly TextBox _biography = new();
    private bool _bioExists;
    private string _originalBio = "";
    private bool _changingSelection;
    private readonly Button _saveButton = new();
    private readonly Button _updatesButton = new();
    private readonly string[] _tables = { "tblWorker", "tblWorkerSkill", "tblWorkerOver", "tblWorkerPhysical" };

    private sealed record FieldRef(string Table, string Key, string Name, Type Type, string Before);

    public MainForm()
    {
        Text = "TEW Universe Studio | TEW IX";
        Width = 1360;
        Height = 820;
        MinimumSize = new Size(1050, 660);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = _bg;
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10);

        var bar = new FlowLayoutPanel {
            Dock = DockStyle.Top, Height = 70, Padding = new Padding(16, 13, 12, 8),
            WrapContents = false, BackColor = _panel
        };
        var import = MakeButton("Import TEW9.mdb", 172);
        import.Click += (_, _) => ImportDatabase();
        bar.Controls.Add(import);
        var reopen = MakeButton("Open working project", 192);
        reopen.Click += (_, _) => OpenWorkingProject();
        bar.Controls.Add(reopen);
        var folder = MakeButton("Projects folder", 147);
        folder.Click += (_, _) => OpenProjectsFolder();
        bar.Controls.Add(folder);
        _saveButton.Text = "Save worker changes";
        ConfigureButton(_saveButton, 190, true);
        _saveButton.Enabled = false;
        _saveButton.Click += (_, _) => SaveWorker();
        bar.Controls.Add(_saveButton);
        ConfigureButton(_updatesButton, 150, false);
        _updatesButton.Text = "Check for updates";
        _updatesButton.Click += async (_, _) => await CheckForUpdatesAsync(true);
        bar.Controls.Add(_updatesButton);

        var split = new SplitContainer {
            Dock = DockStyle.Fill, SplitterDistance = 325, FixedPanel = FixedPanel.Panel1,
            BackColor = _bg, SplitterWidth = 6
        };
        var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16), BackColor = _bg };
        var leftTitle = new Label { Text = "EXISTING WORKERS", ForeColor = _accent, Dock = DockStyle.Top, Height = 40, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
        _search.Dock = DockStyle.Top;
        _search.Height = 34;
        _search.PlaceholderText = "Search name or worker ID";
        _search.BackColor = Color.White;
        _search.ForeColor = Color.Black;
        _search.TextChanged += (_, _) => FilterWorkers();
        _workerCount.Dock = DockStyle.Top;
        _workerCount.Height = 50;
        _workerCount.Padding = new Padding(0, 10, 0, 0);
        _workerCount.ForeColor = _muted;
        _workerCount.Text = "Import a scenario database to begin.";
        _workerList.Dock = DockStyle.Fill;
        _workerList.BackColor = _panel;
        _workerList.ForeColor = Color.White;
        _workerList.BorderStyle = BorderStyle.FixedSingle;
        _workerList.IntegralHeight = false;
        _workerList.Font = new Font("Segoe UI", 10);
        _workerList.SelectedIndexChanged += (_, _) => WorkerSelected();
        left.Controls.Add(_workerList);
        left.Controls.Add(_workerCount);
        left.Controls.Add(_search);
        left.Controls.Add(leftTitle);
        split.Panel1.Controls.Add(left);

        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14) };
        _header.Dock = DockStyle.Top;
        _header.Height = 75;
        _header.Text = "Select an existing worker\nChanges are saved only to your separate working copy.";
        _header.Font = new Font("Segoe UI", 15, FontStyle.Bold);
        _header.ForeColor = Color.White;
        _tabs.Dock = DockStyle.Fill;

        foreach (var (label, table) in new[] {
            ("Profile", "tblWorker"), ("Skills", "tblWorkerSkill"),
            ("Popularity", "tblWorkerOver"), ("Physical", "tblWorkerPhysical")
        })
        {
            var page = new TabPage(label) { BackColor = _panel, ForeColor = Color.Black, Padding = new Padding(6) };
            var grid = new DataGridView {
                Dock = DockStyle.Fill, AllowUserToAddRows = false,
                AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
                RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = _panel, BorderStyle = BorderStyle.None,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2,
                EnableHeadersVisualStyles = false
            };
            var fieldCol = new DataGridViewTextBoxColumn { HeaderText = "TEW IX database field", Name = "Field", FillWeight = 42, ReadOnly = true };
            var valueCol = new DataGridViewTextBoxColumn { HeaderText = "Value (editable)", Name = "Value", FillWeight = 58 };
            grid.Columns.AddRange(fieldCol, valueCol);
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(46, 66, 87);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            grid.DefaultCellStyle.BackColor = Color.White;
            grid.DefaultCellStyle.ForeColor = Color.FromArgb(20, 30, 43);
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(236, 243, 245);
            grid.DataError += (_, args) => { args.ThrowException = false; SetStatus("Invalid cell value. Please correct it."); };
            page.Controls.Add(grid);
            _grids[table] = grid;
            _tabs.TabPages.Add(page);
        }
        var bioPage = new TabPage("Biography") { Padding = new Padding(10), BackColor = _panel };
        _biography.Dock = DockStyle.Fill;
        _biography.Multiline = true;
        _biography.ScrollBars = ScrollBars.Vertical;
        _biography.Font = new Font("Segoe UI", 11);
        _biography.Enabled = false;
        bioPage.Controls.Add(_biography);
        _tabs.TabPages.Add(bioPage);

        right.Controls.Add(_tabs);
        right.Controls.Add(_header);
        split.Panel2.Controls.Add(right);

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 39, BackColor = _panel };
        _status.Dock = DockStyle.Fill;
        _status.Padding = new Padding(16, 8, 0, 0);
        _status.Text = "No project open. Original TEW IX files will never be edited by this app.";
        _status.ForeColor = _muted;
        footer.Controls.Add(_status);
        Controls.Add(split);
        Controls.Add(footer);
        Controls.Add(bar);

        Shown += async (_, _) => await CheckForUpdatesAsync(false);
        FormClosed += (_, _) => _session?.Dispose();
    }

    private Button MakeButton(string text, int width)
    {
        var button = new Button();
        ConfigureButton(button, width, false);
        button.Text = text;
        return button;
    }

    private void ConfigureButton(Button button, int width, bool prominent)
    {
        button.Width = width;
        button.Height = 37;
        button.Margin = new Padding(0, 0, 10, 0);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = prominent ? _accent : Color.FromArgb(98, 132, 165);
        button.FlatAppearance.BorderSize = 1;
        button.BackColor = prominent ? _accent : Color.FromArgb(34, 54, 74);
        button.ForeColor = prominent ? Color.FromArgb(12, 35, 40) : Color.White;
        button.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
    }

    private void SetStatus(string message) => _status.Text = message;

    private static string? AskPassword(IWin32Window parent)
    {
        using var dlg = new Form {
            Text = "Database password (not stored)", Width = 445, Height = 190,
            StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false
        };
        var note = new Label { Text = "Enter the TEW IX MDB password. It stays in memory for this session.", Left = 17, Top = 17, Width = 395, Height = 39 };
        var input = new TextBox { Left = 17, Top = 59, Width = 394, UseSystemPasswordChar = true };
        var ok = new Button { Text = "Connect", Left = 230, Top = 99, Width = 86, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 325, Top = 99, Width = 86, DialogResult = DialogResult.Cancel };
        dlg.Controls.AddRange(new Control[] { note, input, ok, cancel });
        dlg.AcceptButton = ok;
        dlg.CancelButton = cancel;
        return dlg.ShowDialog(parent) == DialogResult.OK ? input.Text : null;
    }

    private bool MayReplaceProject()
    {
        if (GetEdits().Count == 0) return true;
        return MessageBox.Show(this, "Discard unsaved changes to the currently selected worker?",
            "Unsaved changes", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
    }

    private void ImportDatabase()
    {
        if (!MayReplaceProject()) return;
        using var dlg = new OpenFileDialog { Title = "Select an original TEW IX scenario MDB", Filter = "Access database (*.mdb)|*.mdb" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var password = AskPassword(this);
        if (password == null) return;
        try
        {
            var opened = DatabaseSession.ImportSource(dlg.FileName, password);
            ReplaceSession(opened);
            MessageBox.Show(this, "A separate working copy was created in your local Projects folder.\nYour selected original was not modified.", "Working copy ready");
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void OpenWorkingProject()
    {
        if (!MayReplaceProject()) return;
        Directory.CreateDirectory(DatabaseSession.ProjectRoot);
        using var dlg = new OpenFileDialog {
            Title = "Open an existing TEW Universe Studio working copy",
            Filter = "Access database (*.mdb)|*.mdb", InitialDirectory = DatabaseSession.ProjectRoot
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var password = AskPassword(this);
        if (password == null) return;
        try { ReplaceSession(DatabaseSession.OpenWorkingCopy(dlg.FileName, password)); }
        catch (Exception ex) { ShowError(ex); }
    }

    private void ReplaceSession(DatabaseSession opened)
    {
        try
        {
            var workers = opened.ListWorkers();
            _session?.Dispose();
            _session = opened;
            _workers = workers;
            _currentWorker = null;
            _search.Text = "";
            FilterWorkers();
            ClearEditor();
            SetStatus($"Connected using {opened.Provider}. {workers.Count} worker records found. Editing: {opened.PathOnDisk}");
        }
        catch { opened.Dispose(); throw; }
    }

    private void OpenProjectsFolder()
    {
        Directory.CreateDirectory(DatabaseSession.ProjectRoot);
        Process.Start(new ProcessStartInfo { FileName = DatabaseSession.ProjectRoot, UseShellExecute = true });
    }

    private void FilterWorkers()
    {
        _changingSelection = true;
        try
        {
            var term = _search.Text.Trim();
            _visible = _workers.Where(w =>
                w.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                w.Id.ToString(CultureInfo.InvariantCulture).Contains(term, StringComparison.OrdinalIgnoreCase)
            ).Take(500).ToList();
            _workerList.BeginUpdate();
            _workerList.Items.Clear();
            foreach (var worker in _visible) _workerList.Items.Add(worker);
            _workerList.EndUpdate();
            _workerCount.Text = $"Showing {_visible.Count} of {_workers.Count} workers (maximum 500 displayed).";
        }
        finally { _changingSelection = false; }
    }

    private void WorkerSelected()
    {
        if (_changingSelection || _session == null || _workerList.SelectedIndex < 0) return;
        var selected = _visible[_workerList.SelectedIndex];
        if (_currentWorker?.Id == selected.Id) return;
        if (GetEdits().Count > 0 && MessageBox.Show(this,
            "Discard unsaved changes to this worker?", "Unsaved changes", MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            _changingSelection = true;
            _workerList.ClearSelected();
            _changingSelection = false;
            return;
        }
        LoadWorker(selected);
    }

    private void ClearEditor()
    {
        foreach (var grid in _grids.Values) grid.Rows.Clear();
        _biography.Text = "";
        _originalBio = "";
        _bioExists = false;
        _biography.Enabled = false;
        _saveButton.Enabled = false;
        _header.Text = "Choose an existing worker\nNo new worker creation in this release.";
    }

    private void LoadWorker(WorkerItem worker)
    {
        if (_session == null) return;
        try
        {
            foreach (var table in _tables)
            {
                var record = _session.ReadSection(table, worker.Id);
                var grid = _grids[table];
                grid.Rows.Clear();
                if (!record.Exists) continue;
                foreach (DataColumn column in record.Data.Columns)
                {
                    if (column.ColumnName.Equals(record.Key, StringComparison.OrdinalIgnoreCase) ||
                        !DatabaseSession.IsEditable(column.DataType)) continue;
                    string oldValue = DatabaseSession.Display(record.Data.Rows[0][column]);
                    var index = grid.Rows.Add(column.ColumnName, oldValue);
                    grid.Rows[index].Tag = new FieldRef(table, record.Key, column.ColumnName, column.DataType, oldValue);
                }
            }
            var bio = _session.ReadSection("tblWorkerBio", worker.Id);
            _bioExists = bio.Exists && bio.Data.Columns.Contains("Profile");
            _originalBio = _bioExists ? DatabaseSession.Display(bio.Data.Rows[0]["Profile"]) : "";
            _biography.Text = _originalBio;
            _biography.Enabled = _bioExists;
            _currentWorker = worker;
            _saveButton.Enabled = true;
            _header.Text = $"{worker.Name}  |  Worker ID {worker.Id}\nEdit values in the tabs, then save to your working copy.";
            SetStatus($"Editing existing worker {worker.Id}. Missing linked records remain read only.");
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private List<FieldEdit> GetEdits()
    {
        var edits = new List<FieldEdit>();
        if (_session == null || _currentWorker == null) return edits;
        foreach (var grid in _grids.Values)
        {
            if (grid.IsCurrentCellInEditMode) grid.EndEdit();
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.Tag is not FieldRef field) continue;
                string text = Convert.ToString(row.Cells["Value"].Value, CultureInfo.InvariantCulture) ?? "";
                if (!string.Equals(text, field.Before, StringComparison.Ordinal))
                    edits.Add(new FieldEdit(field.Table, field.Key, field.Name, field.Type, text));
            }
        }
        if (_bioExists && _biography.Text != _originalBio)
            edits.Add(new FieldEdit("tblWorkerBio", "UID", "Profile", typeof(string), _biography.Text));
        return edits;
    }

    private void SaveWorker()
    {
        if (_session == null || _currentWorker == null) return;
        try
        {
            var edits = GetEdits();
            if (edits.Count == 0) { SetStatus("Nothing changed. No save required."); return; }
            if (MessageBox.Show(this,
                $"Save {edits.Count} changed field(s) for {_currentWorker.Name} to the working copy?\nA new backup will be created first.",
                "Confirm database update", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            var savedId = _currentWorker.Id;
            var result = _session.Save(savedId, edits);
            _workers = _session.ListWorkers();
            var updated = _workers.FirstOrDefault(w => w.Id == savedId);
            if (updated != null) LoadWorker(updated);
            SetStatus($"Saved {result.Modified} changes. Backup: {result.BackupFile}");
            MessageBox.Show(this, $"Saved {result.Modified} field(s) to your working copy.\nBackup created at:\n{result.BackupFile}\n\nCheck TEW IX's Data Check before using the scenario.",
                "Save completed", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void ShowError(Exception ex) =>
        MessageBox.Show(this, ex.Message, "TEW Universe Studio", MessageBoxButtons.OK, MessageBoxIcon.Error);

    private async Task CheckForUpdatesAsync(bool manual)
    {
        _updatesButton.Enabled = false;
        try
        {
            var update = await UpdateService.FindUpdateAsync();
            if (update == null)
            {
                if (manual) MessageBox.Show(this, "You are running the latest available release or no release is published yet.", "Updates");
                return;
            }
            if (MessageBox.Show(this,
                $"TEW Universe Studio {update.Version} is available.\nDownload the verified installer and install it when the editor closes?",
                "Update available", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            SetStatus("Downloading update. Please wait...");
            var installer = await UpdateService.DownloadVerifiedAsync(update);
            var updater = Path.Combine(AppContext.BaseDirectory, "TEWUniverse.Updater.exe");
            if (!File.Exists(updater))
                throw new InvalidOperationException("Updater executable not found. Install a published GitHub Release build.");
            var info = new ProcessStartInfo { FileName = updater, UseShellExecute = false };
            info.ArgumentList.Add("--waitpid");
            info.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            info.ArgumentList.Add("--installer");
            info.ArgumentList.Add(installer);
            info.ArgumentList.Add("--restart");
            info.ArgumentList.Add(Environment.ProcessPath ?? "");
            Process.Start(info);
            Close();
        }
        catch (Exception ex)
        {
            if (manual) ShowError(ex);
            else SetStatus("Automatic update check unavailable. The editor can still be used offline.");
        }
        finally { if (!IsDisposed) _updatesButton.Enabled = true; }
    }
}
