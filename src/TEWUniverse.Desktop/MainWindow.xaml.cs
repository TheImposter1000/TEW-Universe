using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using TEWUniverse.Studio;

namespace TEWUniverse.Desktop;

public partial class MainWindow : Window
{
    private AppSettings _settings = AppSettings.Load();
    private DatabaseSession? _session;
    private string? _sessionPassword;
    private WorkerItem? _worker;
    private List<WorkerItem> _allWorkers = new();
    private readonly List<FieldDraft> _fields = new();
    private string _originalBio = "";
    private bool _bioExists;
    private bool _suppressSelection;
    private bool _syncBio;
    private bool _loading;
    private string _currentTab = "Profile";
    private DateTime? _lastSaved;
    private static readonly Brush Muted = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A6BDD9")!);

    private sealed class FieldDraft
    {
        public required string Table { get; init; }
        public required string Key { get; init; }
        public required string Column { get; init; }
        public required Type ValueType { get; init; }
        public required string Original { get; init; }
        public string Value { get; set; } = "";
        public bool Dirty => !string.Equals(Value, Original, StringComparison.Ordinal);
        public FieldEdit ToEdit() => new(Table, Key, Column, ValueType, Value);
    }

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            RestoreProject();
            await CheckUpdatesAsync(false);
        };
        ShowSection("Profile");
    }

    private bool HasEdits() =>
        _fields.Any(x => x.Dirty) || (_bioExists && BioEditor.Text != _originalBio);

    private bool MayLeave()
    {
        if (!HasEdits()) return true;
        return MessageBox.Show(this,
            "Discard unsaved worker changes? Save Changes first to keep them.",
            "Unsaved changes", MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!MayLeave()) { e.Cancel = true; return; }
        _session?.Dispose();
    }

    private void SetStatus(string text) => StatusText.Text = text;

    private void RefreshDirty()
    {
        bool dirty = HasEdits();
        SaveButton.IsEnabled = _session != null && _worker != null && dirty;
        BackupButton.IsEnabled = _session != null;
        SaveAsButton.IsEnabled = _session != null;
        WorkingText.Text = _session == null ? "No working copy" :
            "Working Copy: " + Path.GetFileName(_session.PathOnDisk);
        SaveStateText.Text = dirty ? "● Unsaved changes" :
            _lastSaved is DateTime saved ? "Saved " + saved.ToString("HH:mm", CultureInfo.CurrentCulture) :
            "No unsaved edits";
        SaveStateText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
            dirty ? "#F5C96A" : "#70E4C6")!);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => FilterWorkers();

    private void FilterWorkers(int? selectId = null)
    {
        if (WorkersList == null) return;
        var query = SearchBox.Text.Trim();
        var filtered = _allWorkers.Where(x =>
            x.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            x.Id.ToString(CultureInfo.InvariantCulture).Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(700).ToList();

        _suppressSelection = true;
        WorkersList.ItemsSource = filtered;
        var desired = selectId ?? _worker?.Id;
        WorkersList.SelectedItem = desired.HasValue
            ? filtered.FirstOrDefault(x => x.Id == desired) : null;
        _suppressSelection = false;
        WorkerCountText.Text = $"Showing {filtered.Count:N0} of {_allWorkers.Count:N0} workers";
        FilteredText.Text = $"{filtered.Count:N0} of {_allWorkers.Count:N0} workers (filtered)";
    }

    private void WorkersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection || WorkersList.SelectedItem is not WorkerItem chosen ||
            chosen.Id == _worker?.Id || _session == null) return;
        if (!MayLeave())
        {
            _suppressSelection = true;
            WorkersList.SelectedItem = _worker == null
                ? null : WorkersList.Items.Cast<WorkerItem>().FirstOrDefault(x => x.Id == _worker.Id);
            _suppressSelection = false;
            return;
        }
        LoadWorker(chosen);
    }

    private void LoadWorker(WorkerItem selected)
    {
        if (_session == null) return;
        try
        {
            _loading = true;
            _fields.Clear();
            foreach (var table in new[] { "tblWorker", "tblWorkerSkill", "tblWorkerOver", "tblWorkerPhysical" })
            {
                var section = _session.ReadSection(table, selected.Id);
                if (!section.Exists) continue;
                var row = section.Data.Rows[0];
                foreach (DataColumn column in section.Data.Columns)
                {
                    if (column.ColumnName.Equals(section.Key, StringComparison.OrdinalIgnoreCase) ||
                        !DatabaseSession.IsEditable(column.DataType)) continue;
                    var value = DatabaseSession.Display(row[column]);
                    _fields.Add(new FieldDraft {
                        Table = table, Key = section.Key, Column = column.ColumnName,
                        Original = value, Value = value, ValueType = column.DataType
                    });
                }
            }

            var bio = _session.ReadSection("tblWorkerBio", selected.Id);
            _bioExists = bio.Exists && bio.Data.Columns.Contains("Profile");
            _originalBio = _bioExists ? DatabaseSession.Display(bio.Data.Rows[0]["Profile"]) : "";
            BioEditor.IsEnabled = FullBioEditor.IsEnabled = _bioExists;
            BioEditor.Text = _originalBio;
            FullBioEditor.Text = _originalBio;

            _worker = selected;
            WorkerNameText.Text = selected.Name;
            WorkerSummaryText.Text = $"Worker ID {selected.Id}  •  Existing TEW IX worker  •  Working database";
            WorkerStatusText.Text = selected.IsActive switch {
                true => "● Active worker", false => "● Inactive worker", _ => "Worker record"
            };
            QuickId.Text = "Worker ID: " + selected.Id;
            QuickActive.Text = "Status: " + (selected.IsActive switch {
                true => "Active", false => "Inactive", _ => "Not available"
            });
            QuickDatabase.Text = "Current Copy: " + Path.GetFileName(_session.PathOnDisk);
            BuildProfileFields();
            ShowSection(_currentTab);
            SetStatus("Editing " + selected.Name);
        }
        catch (Exception ex) { Error(ex); }
        finally { _loading = false; RefreshDirty(); }
    }

    private static string FriendlyName(string column)
    {
        var result = Regex.Replace(column, @"(?<=[a-z0-9])(?=[A-Z])", " ");
        return result.Replace("_", " ");
    }

    private void AddField(StackPanel destination, FieldDraft item)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 9) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var name = new TextBlock {
            Text = FriendlyName(item.Column), Foreground = Muted, FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = $"{item.Table}.{item.Column}"
        };
        Grid.SetColumn(name, 0);
        grid.Children.Add(name);
        if (item.ValueType == typeof(bool))
        {
            var box = new ComboBox {
                ItemsSource = new[] { "True", "False" },
                SelectedItem = item.Value.Equals("True", StringComparison.OrdinalIgnoreCase) ||
                               item.Value == "1" ? "True" : "False",
                Height = 32, Background = (Brush)FindResource("RaisedBrush"),
                Foreground = Brushes.Black, Padding = new Thickness(7, 5, 7, 5),
                ToolTip = $"TEW field: {item.Column}"
            };
            box.SelectionChanged += (_, _) =>
            {
                item.Value = box.SelectedItem as string ?? "";
                if (!_loading) RefreshDirty();
            };
            Grid.SetColumn(box, 1);
            grid.Children.Add(box);
        }
        else
        {
            var text = new TextBox {
                Style = (Style)FindResource("FormTextBox"), Text = item.Value,
                ToolTip = $"{item.Table}.{item.Column} ({item.ValueType.Name})"
            };
            text.TextChanged += (_, _) =>
            {
                item.Value = text.Text;
                if (!_loading) RefreshDirty();
                if (item.Table == "tblWorker" && item.Column.Equals("Name", StringComparison.OrdinalIgnoreCase))
                    WorkerNameText.Text = string.IsNullOrWhiteSpace(text.Text) ? "(unnamed)" : text.Text;
            };
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
        }
        destination.Children.Add(grid);
    }

    private void AddSectionHeader(StackPanel panel, string title)
    {
        panel.Children.Add(new TextBlock {
            Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 16, 0, 12)
        });
    }

    private void BuildProfileFields()
    {
        ProfileFieldsPanel.Children.Clear();
        var profile = _fields.Where(x => x.Table == "tblWorker").ToList();
        if (profile.Count == 0)
        {
            AddSectionHeader(ProfileFieldsPanel, "No profile record found for this worker.");
            return;
        }
        // Prioritize known frequently edited columns while still exposing every editable column.
        var priority = new[] { "Name", "ShortName", "Gender", "Sex", "Active", "DateOfBirth",
            "BirthDate", "DOB", "Nationality", "BasedIn", "Style" };
        var first = profile.Where(x => priority.Contains(x.Column, StringComparer.OrdinalIgnoreCase))
            .OrderBy(x => Array.FindIndex(priority,
                p => p.Equals(x.Column, StringComparison.OrdinalIgnoreCase))).ToList();
        if (first.Count > 0)
        {
            AddSectionHeader(ProfileFieldsPanel, "Core fields");
            foreach (var item in first) AddField(ProfileFieldsPanel, item);
        }
        var others = profile.Except(first).ToList();
        if (others.Count > 0)
        {
            AddSectionHeader(ProfileFieldsPanel, "Additional TEW IX fields");
            foreach (var item in others) AddField(ProfileFieldsPanel, item);
        }
    }

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string tab) ShowSection(tab);
    }

    private void ShowSection(string tab)
    {
        _currentTab = tab;
        ProfileView.Visibility = tab == "Profile" ? Visibility.Visible : Visibility.Collapsed;
        BiographyView.Visibility = tab == "Biography" ? Visibility.Visible : Visibility.Collapsed;
        DetailsView.Visibility = tab is "Skills" or "Popularity" or "Physical"
            ? Visibility.Visible : Visibility.Collapsed;
        foreach (var button in new[] { ProfileTab, SkillsTab, PopularityTab, PhysicalTab, BiographyTab })
            button.Style = (Style)FindResource(button.Tag as string == tab ? "BlueButton" : "ActionButton");
        if (DetailsView.Visibility != Visibility.Visible) return;
        var table = tab switch {
            "Skills" => "tblWorkerSkill", "Popularity" => "tblWorkerOver",
            _ => "tblWorkerPhysical"
        };
        DetailsTitle.Text = tab;
        DetailsSubtitle.Text = "Edit existing TEW IX fields. Field names are shown as stored in the database.";
        DetailsFieldsPanel.Children.Clear();
        var fields = _fields.Where(x => x.Table == table).ToList();
        if (fields.Count == 0)
            AddSectionHeader(DetailsFieldsPanel, "No linked record is available for this worker.");
        else foreach (var field in fields) AddField(DetailsFieldsPanel, field);
    }

    private void BioEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncBio) return;
        _syncBio = true;
        if (FullBioEditor != null) FullBioEditor.Text = BioEditor.Text;
        _syncBio = false;
        if (!_loading) RefreshDirty();
    }

    private void FullBioEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncBio) return;
        _syncBio = true;
        if (BioEditor != null) BioEditor.Text = FullBioEditor.Text;
        _syncBio = false;
        if (!_loading) RefreshDirty();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null || _worker == null) return;
        try
        {
            var edits = _fields.Where(x => x.Dirty).Select(x => x.ToEdit()).ToList();
            if (_bioExists && BioEditor.Text != _originalBio)
                edits.Add(new FieldEdit("tblWorkerBio", "UID", "Profile", typeof(string), BioEditor.Text));
            if (edits.Count == 0) { SetStatus("No changes to save"); return; }
            if (MessageBox.Show(this,
                    $"Save {edits.Count} changed field(s) to the current working database?\n" +
                    "No backup will be created.",
                    "Save Changes", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            int savedId = _worker.Id;
            int count = _session.Save(savedId, edits);
            _allWorkers = _session.ListWorkers();
            _lastSaved = DateTime.Now;
            var refreshed = _allWorkers.FirstOrDefault(x => x.Id == savedId);
            if (refreshed != null) LoadWorker(refreshed);
            FilterWorkers(savedId);
            SetStatus($"Saved {count} field(s) to working copy. No backup created.");
        }
        catch (Exception ex) { Error(ex); }
    }

    private void Backup_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null) return;
        try
        {
            if (HasEdits() && MessageBox.Show(this,
                    "Unsaved edits will not be included in this backup. Continue?",
                    "Unsaved edits", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                != MessageBoxResult.Yes) return;
            var copy = _session.Backup(_settings.BackupFolderPath);
            SetStatus("Backup created: " + copy);
            MessageBox.Show(this, "Backup created:\n" + copy, "Create Backup",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { Error(ex); }
    }

    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null || _sessionPassword == null) return;
        if (HasEdits()) { MessageBox.Show(this, "Save or discard changes before saving a new copy."); return; }
        var dialog = new SaveFileDialog {
            Filter = "Access database (*.mdb)|*.mdb", FileName =
            Path.GetFileNameWithoutExtension(_session.PathOnDisk) + "-copy.mdb",
            InitialDirectory = _session.ProjectFolder, OverwritePrompt = false
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            _session.CopyTo(dialog.FileName, _settings.DefaultDatabasePath);
            var opened = DatabaseSession.OpenWorkingCopy(
                dialog.FileName, _sessionPassword, _settings.DefaultDatabasePath);
            SetSession(opened, _sessionPassword);
            SetStatus("Created and opened new working copy");
        }
        catch (Exception ex) { Error(ex); }
    }

    private (DatabaseSession session, string password)? OpenWithCredential(
        Func<string, DatabaseSession> open)
    {
        var saved = _settings.RememberDatabasePassword ? CredentialStore.TryRead() : null;
        if (!string.IsNullOrEmpty(saved))
        {
            try { return (open(saved), saved); }
            catch { SetStatus("Saved password was not accepted. Please enter it again."); }
        }
        var asked = PasswordDialog.Ask(this);
        if (asked == null) return null;
        return (open(asked), asked);
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        if (!MayLeave()) return;
        var original = new OpenFileDialog { Title = "Choose original TEW IX database",
            Filter = "Access database (*.mdb)|*.mdb" };
        if (File.Exists(_settings.DefaultDatabasePath)) original.FileName = _settings.DefaultDatabasePath;
        if (original.ShowDialog(this) != true) return;
        Directory.CreateDirectory(DatabaseSession.ProjectRoot);
        var target = new SaveFileDialog {
            Title = "Choose where to create your new editable working copy",
            Filter = "Access database (*.mdb)|*.mdb", OverwritePrompt = false,
            InitialDirectory = DatabaseSession.ProjectRoot,
            FileName = "TEW9-working.mdb"
        };
        if (target.ShowDialog(this) != true) return;
        if (File.Exists(target.FileName) || AppSettings.SamePath(original.FileName, target.FileName))
        {
            MessageBox.Show(this, "Choose a new file name. Existing databases are never overwritten.");
            return;
        }
        var password = PasswordDialog.Ask(this);
        if (password == null) return;
        try
        {
            var opened = DatabaseSession.ImportSource(original.FileName, password, target.FileName);
            _settings.DefaultDatabasePath = original.FileName;
            SetSession(opened, password);
            SetStatus("Imported separate working copy at " + target.FileName);
        }
        catch (Exception ex) { Error(ex); }
    }

    private void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        if (!MayLeave()) return;
        var file = new OpenFileDialog {
            Filter = "Access database (*.mdb)|*.mdb",
            Title = "Open editable WORKING database, not the original scenario",
            InitialDirectory = Directory.Exists(DatabaseSession.ProjectRoot)
                ? DatabaseSession.ProjectRoot : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (File.Exists(_settings.CurrentCopyPath)) file.FileName = _settings.CurrentCopyPath;
        if (file.ShowDialog(this) != true) return;
        if (AppSettings.SamePath(file.FileName, _settings.DefaultDatabasePath))
        {
            MessageBox.Show(this, "That is your protected original database. Import a copy instead.");
            return;
        }
        if (MessageBox.Show(this, "Confirm this is an editable copy and NOT your original TEW scenario.",
                "Open working copy", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        try
        {
            var opened = OpenWithCredential(p =>
                DatabaseSession.OpenWorkingCopy(file.FileName, p, _settings.DefaultDatabasePath));
            if (opened.HasValue) SetSession(opened.Value.session, opened.Value.password);
        }
        catch (Exception ex) { Error(ex); }
    }

    private void RestoreProject()
    {
        if (!File.Exists(_settings.CurrentCopyPath))
        {
            SetStatus("Choose Import Database or Open Project to begin.");
            return;
        }
        try
        {
            var opened = OpenWithCredential(p =>
                DatabaseSession.OpenWorkingCopy(_settings.CurrentCopyPath, p, _settings.DefaultDatabasePath));
            if (opened.HasValue) SetSession(opened.Value.session, opened.Value.password);
        }
        catch (Exception ex) { Error(ex); }
    }

    private void SetSession(DatabaseSession opened, string password)
    {
        try
        {
            var list = opened.ListWorkers();
            var copy = _settings.Copy();
            copy.CurrentCopyPath = opened.PathOnDisk;
            copy.Save();
            _session?.Dispose();
            _session = opened;
            _sessionPassword = password;
            _settings = copy;
            _allWorkers = list;
            _worker = null;
            _fields.Clear();
            _originalBio = "";
            _bioExists = false;
            _syncBio = true;
            BioEditor.Text = FullBioEditor.Text = "";
            _syncBio = false;
            WorkerNameText.Text = "Choose an existing worker";
            WorkerSummaryText.Text = "Search on the left to open a worker profile.";
            WorkerStatusText.Text = "Database loaded";
            ProfileFieldsPanel.Children.Clear();
            DetailsFieldsPanel.Children.Clear();
            ProjectPathLabel.Text = opened.PathOnDisk;
            _lastSaved = null;
            SearchBox.Text = "";
            FilterWorkers();
            RefreshDirty();
            SetStatus($"Loaded {list.Count:N0} existing workers");
            if (_settings.RememberDatabasePassword)
            {
                try { CredentialStore.Save(password); }
                catch (Exception ex) { SetStatus("Loaded database, but Windows did not save the password: " + ex.Message); }
            }
        }
        catch
        {
            if (!ReferenceEquals(_session, opened)) opened.Dispose();
            throw;
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_settings) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.ResultSettings == null) return;
        var next = dialog.ResultSettings;
        bool changed = !AppSettings.SamePath(next.CurrentCopyPath, _settings.CurrentCopyPath) &&
            !(string.IsNullOrWhiteSpace(next.CurrentCopyPath) && string.IsNullOrWhiteSpace(_settings.CurrentCopyPath));
        if (changed && !MayLeave()) return;
        try
        {
            next.Save();
            if (!next.RememberDatabasePassword) CredentialStore.Delete();
            else if (_sessionPassword != null) CredentialStore.Save(_sessionPassword);
            _settings = next;
            SetStatus("Settings saved.");
            if (changed)
            {
                _session?.Dispose();
                _session = null;
                _sessionPassword = null;
                _worker = null;
                _fields.Clear();
                _allWorkers.Clear();
                FilterWorkers();
                BackupButton.IsEnabled = SaveAsButton.IsEnabled = SaveButton.IsEnabled = false;
                RestoreProject();
            }
        }
        catch (Exception ex) { Error(ex); }
    }

    private void ProjectsFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(DatabaseSession.ProjectRoot);
        Process.Start(new ProcessStartInfo(DatabaseSession.ProjectRoot) { UseShellExecute = true });
    }

    private void WorkersNav_Click(object sender, RoutedEventArgs e) => ShowSection("Profile");

    private void FutureNav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b)
            MessageBox.Show(this, $"{b.Tag} is reserved for the next data editor module.\n" +
                "The current release edits existing workers only.",
                "Module not yet implemented", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Error(Exception ex)
    {
        SetStatus("Operation failed.");
        MessageBox.Show(this, ex.Message, "TEW Universe Studio",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void UpdateLog_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(UpdateService.LogPath))
        {
            MessageBox.Show(this, "No update log found yet.");
            return;
        }
        Process.Start(new ProcessStartInfo("notepad.exe", "\"" + UpdateService.LogPath + "\"") {
            UseShellExecute = true
        });
    }

    private async void Updates_Click(object sender, RoutedEventArgs e) => await CheckUpdatesAsync(true);

    private async Task CheckUpdatesAsync(bool manual)
    {
        if (!UpdatesButton.IsEnabled) return;
        UpdatesButton.IsEnabled = false;
        try
        {
            var release = await UpdateService.FindUpdateAsync();
            if (release == null)
            {
                if (manual) MessageBox.Show(this, "No newer published release is available.");
                return;
            }
            if (MessageBox.Show(this, $"Download and install TEW Universe Studio {release.Version}?",
                    "Update available", MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            if (!MayLeave()) return;
            var progress = new Progress<UpdateProgress>(p =>
                SetStatus(p.TotalBytes.HasValue && p.TotalBytes.Value > 0
                    ? $"Downloading update: {p.BytesReceived * 100.0 / p.TotalBytes.Value:N0}%"
                    : $"Downloading update: {p.BytesReceived / 1048576.0:N1} MB"));
            var installer = await UpdateService.DownloadVerifiedAsync(release, progress);
            var updater = Path.Combine(AppContext.BaseDirectory, "TEWUniverse.Updater.exe");
            if (!File.Exists(updater))
                throw new FileNotFoundException("Updater agent was not installed.", updater);
            var staged = Path.Combine(Path.GetDirectoryName(installer)!, "TEWUniverse.Updater.exe");
            File.Copy(updater, staged, overwrite: true);
            var info = new ProcessStartInfo(staged) { UseShellExecute = false };
            info.ArgumentList.Add("--waitpid");
            info.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            info.ArgumentList.Add("--installer");
            info.ArgumentList.Add(installer);
            info.ArgumentList.Add("--restart");
            info.ArgumentList.Add(Environment.ProcessPath!);
            Process.Start(info);
            Close();
        }
        catch (Exception ex)
        {
            UpdateService.Log("WPF update failed: " + ex.Message);
            if (manual) Error(ex);
            else SetStatus("Could not check for updates. Manual check is available.");
        }
        finally { UpdatesButton.IsEnabled = true; }
    }
}
