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
    private string _sortMode = "Name A to Z";
    private WorkerPortraitConverter Portraits => (WorkerPortraitConverter)FindResource("WorkerPictureConverter");
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
        RefreshPortraits();
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
        SaveButton.IsEnabled = _session != null && _worker != null;
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

    private void RefreshPortraits()
    {
        Portraits.SetFolder(_settings.WorkerPicturesFolderPath);
        if (WorkersList != null) FilterWorkers(_worker?.Id);
        UpdateWorkerPortrait();
    }

    private void UpdateWorkerPortrait()
    {
        if (WorkerPortraitEllipse == null) return;
        var picture = ValueFor("Picture") ?? _worker?.PictureName;
        WorkerPortraitEllipse.Fill = ((Brush?)Portraits.LoadBrush(picture, 260)) ?? Brushes.Transparent;
        WorkerPortraitEllipse.ToolTip = string.IsNullOrWhiteSpace(picture)
            ? "No picture is assigned in the MDB"
            : picture;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => FilterWorkers();
    private void ClearSearch_Click(object sender, RoutedEventArgs e) => SearchBox.Clear();
    private void SortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SortCombo?.SelectedItem is ComboBoxItem option)
            _sortMode = Convert.ToString(option.Content, CultureInfo.InvariantCulture) ?? "Name A to Z";
        FilterWorkers();
    }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void FilterWorkers(int? selectId = null)
    {
        if (WorkersList == null) return;
        var query = SearchBox.Text.Trim();
        var matches = _allWorkers.Where(x =>
            x.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            x.Id.ToString(CultureInfo.InvariantCulture).Contains(query, StringComparison.OrdinalIgnoreCase));
        var filtered = (_sortMode switch {
            "Name Z to A" => matches.OrderByDescending(x => x.Name, StringComparer.OrdinalIgnoreCase),
            "Worker ID" => matches.OrderBy(x => x.Id),
            _ => matches.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
        }).ToList();

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
            WorkerIdBadge.Text = $"Worker ID {selected.Id}";
            UpdateWorkerPortrait();
            BuildWorkerFacts();
            WorkerStatusText.Text = selected.IsActive switch {
                true => "● Active worker", false => "● Inactive worker", _ => "Worker record"
            };

            BuildProfileFields();
            ShowSection(_currentTab);
            SetStatus("Editing " + selected.Name);
        }
        catch (Exception ex) { Error(ex); }
        finally { _loading = false; RefreshDirty(); }
    }


    private static string FriendlyName(string column)
    {
        var specialized = column switch {
            "Shortname" => "Short Name",
            "Birthday" => "Date of Birth",
            "Based_In" => "Based In",
            "DebutDate" => "Debut Date",
            "DeathDate" => "Date of Death",
            "CompetesAgainst" => "Competes Against",
            "WorkerHeight" => "Height",
            "WorkerWeight" => "Weight",
            _ => ""
        };
        if (specialized.Length > 0) return specialized;
        return Regex.Replace(column, @"(?<=[a-z0-9])(?=[A-Z])", " ").Replace("_", " ");
    }

    private string? ValueFor(string field) => _fields.FirstOrDefault(
        x => x.Table == "tblWorker" && x.Column.Equals(field, StringComparison.OrdinalIgnoreCase))?.Value;

    private static string DisplayGender(string? value) => value switch
    {
        "1" => "Male", "2" => "Female",
        null or "" => "Not recorded", _ => "Gender code " + value
    };

    // No guessing about TEW IX enum mappings. Their code remains visible to editors.
    private static string Unmapped(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Not recorded" : "Code " + value + " (unmapped)";

    private static DateTime? ParseStoredDate(string? raw)
        => DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) &&
            d.Year is >= 1800 and <= 2200 ? d : null;

    private static string FriendlyDate(string? raw) =>
        ParseStoredDate(raw)?.ToString("MMM d, yyyy", CultureInfo.CurrentCulture) ?? "Not recorded";

    private void AddHeaderFact(string glyph, string value)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal,
            Margin = new Thickness(0,0,18,6) };
        row.Children.Add(new TextBlock {
            Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 15,
            Foreground = Brushes.LightSkyBlue, Margin = new Thickness(0,0,7,0)
        });
        row.Children.Add(new TextBlock { Text = value, FontSize = 12, Foreground = Muted });
        HeaderFactsPanel.Children.Add(row);
    }

    private void AddQuickFact(string label, string value)
    {
        var cell = new StackPanel { Margin = new Thickness(3,0,10,10) };
        cell.Children.Add(new TextBlock { Text = label, Foreground = Muted, FontSize = 11 });
        cell.Children.Add(new TextBlock { Text = value, FontSize = 12,
            FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = value, Margin = new Thickness(0,2,0,0) });
        QuickFactsPanel.Children.Add(cell);
    }

    private void BuildWorkerFacts()
    {
        if (_worker == null) return;
        HeaderFactsPanel.Children.Clear();
        var gender = DisplayGender(ValueFor("Gender"));
        var dob = ParseStoredDate(ValueFor("Birthday"));
        AddHeaderFact("\uE77B", gender);
        if (dob.HasValue)
        {
            int years = DateTime.Today.Year - dob.Value.Year;
            if (dob.Value.Date > DateTime.Today.AddYears(-years)) years--;
            AddHeaderFact("\uE787", FriendlyDate(ValueFor("Birthday")) + (years >= 0 ? $" ({years} years)" : ""));
        }
        if (ValueFor("Nationality") is string n && n.Length > 0)
            AddHeaderFact("\uE774", "Nationality: " + Unmapped(n));
        if (ValueFor("Based_In") is string b && b.Length > 0)
            AddHeaderFact("\uE80F", "Based in: " + Unmapped(b));
        if (ParseStoredDate(ValueFor("DebutDate")).HasValue)
            AddHeaderFact("\uE787", "Debut: " + FriendlyDate(ValueFor("DebutDate")));

        QuickFactsPanel.Children.Clear();
        AddQuickFact("Worker ID", _worker.Id.ToString(CultureInfo.InvariantCulture));
        AddQuickFact("Gender", gender);
        AddQuickFact("Date of Birth", FriendlyDate(ValueFor("Birthday")));
        AddQuickFact("Status", _worker.IsActive switch
        {
            true => "Active", false => "Inactive", _ => "Not recorded"
        });
        AddQuickFact("Nationality", Unmapped(ValueFor("Nationality")));
        AddQuickFact("Based In", Unmapped(ValueFor("Based_In")));
        AddQuickFact("Wrestling Style", Unmapped(ValueFor("Style")));
        AddQuickFact("Debut Date", FriendlyDate(ValueFor("DebutDate")));
    }


    private void AddField(StackPanel destination, FieldDraft item)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(123) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var label = new TextBlock {
            Text = FriendlyName(item.Column), Foreground = Muted, FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = item.Table + "." + item.Column
        };
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);
        bool gender = item.Table == "tblWorker" &&
            item.Column.Equals("Gender", StringComparison.OrdinalIgnoreCase) &&
            item.Value is "1" or "2";
        if (item.ValueType == typeof(bool) || gender)
        {
            var combo = new ComboBox {
                Style = (Style)FindResource("DarkComboBox"),
                ItemsSource = gender ? new[] { "Male", "Female" } : new[] { "Yes", "No" },
                SelectedIndex = gender ? item.Value == "1" ? 0 : 1 :
                    item.Value.Equals("True", StringComparison.OrdinalIgnoreCase) || item.Value == "1" ? 0 : 1,
                Height = 35,
                ToolTip = gender ? "Stored as Gender 1 or 2 in TEW IX" : "Boolean field"
            };
            combo.SelectionChanged += (_, _) => {
                if (combo.SelectedIndex < 0) return;
                item.Value = gender ? (combo.SelectedIndex == 0 ? "1" : "2") :
                    (combo.SelectedIndex == 0 ? "True" : "False");
                if (!_loading) { RefreshDirty(); BuildWorkerFacts(); }
            };
            Grid.SetColumn(combo, 1);
            grid.Children.Add(combo);
        }
        else if (item.ValueType == typeof(DateTime))
        {
            // WPF's default DatePicker renders an OS-white text field despite setting
            // its outer brushes. The three-part date control matches the approved UI
            // and never changes the stored value until a complete valid date exists.
            var initial = ParseStoredDate(item.Value);
            var holder = new Grid { Height = 36 };
            holder.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });
            holder.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.85, GridUnitType.Star) });
            holder.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
            var months = new[] { "Month", "Jan", "Feb", "Mar", "Apr", "May", "Jun",
                "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
            var month = new ComboBox { Style = (Style)FindResource("DarkComboBox"),
                ItemsSource = months, SelectedIndex = initial?.Month ?? 0,
                Margin = new Thickness(0,0,5,0), ToolTip = "Month" };
            var days = new List<string> { "Day" };
            days.AddRange(Enumerable.Range(1,31).Select(x => x.ToString(CultureInfo.InvariantCulture)));
            var day = new ComboBox { Style = (Style)FindResource("DarkComboBox"),
                ItemsSource = days, SelectedIndex = initial?.Day ?? 0,
                Margin = new Thickness(0,0,5,0), ToolTip = "Day" };
            var years = new List<string> { "Year" };
            years.AddRange(Enumerable.Range(1800,401).Reverse().Select(x => x.ToString(CultureInfo.InvariantCulture)));
            var year = new ComboBox { Style = (Style)FindResource("DarkComboBox"),
                ItemsSource = years, SelectedItem = initial?.Year.ToString(CultureInfo.InvariantCulture) ?? "Year",
                ToolTip = "Year" };
            Grid.SetColumn(day,1);
            Grid.SetColumn(year,2);
            holder.Children.Add(month);
            holder.Children.Add(day);
            holder.Children.Add(year);
            bool selecting = false;
            void Changed(object? sender, SelectionChangedEventArgs args)
            {
                if (selecting || _loading) return;
                int mm = month.SelectedIndex;
                int dd = day.SelectedIndex;
                if (!int.TryParse(year.SelectedItem as string, NumberStyles.None,
                    CultureInfo.InvariantCulture, out var yyyy))
                {
                    item.Value = "";
                    BuildWorkerFacts();
                    RefreshDirty();
                    return;
                }
                if (mm == 0 || dd == 0) { item.Value = ""; BuildWorkerFacts(); RefreshDirty(); return; }
                int last = DateTime.DaysInMonth(yyyy,mm);
                if (dd > last)
                {
                    selecting = true;
                    day.SelectedIndex = last;
                    selecting = false;
                    dd = last;
                }
                item.Value = new DateTime(yyyy, mm, dd).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                RefreshDirty();
                BuildWorkerFacts();
            }
            month.SelectionChanged += Changed;
            day.SelectionChanged += Changed;
            year.SelectionChanged += Changed;
            Grid.SetColumn(holder,1);
            grid.Children.Add(holder);
        }
        else
        {
            var text = new TextBox {
                Style = (Style)FindResource("FormTextBox"),
                Text = item.Value,
                ToolTip = item.Table + "." + item.Column +
                    ((item.Column is "Nationality" or "Based_In" or "Style") ?
                        " (TEW numerical code; label mapping not yet verified)" : "")
            };
            text.TextChanged += (_, _) => {
                item.Value = text.Text;
                if (_loading) return;
                RefreshDirty();
                if (item.Table == "tblWorker" &&
                    item.Column.Equals("Name", StringComparison.OrdinalIgnoreCase))
                    WorkerNameText.Text = string.IsNullOrWhiteSpace(text.Text) ? "(unnamed)" : text.Text;
                if (item.Table == "tblWorker" &&
                    (item.Column == "Nationality" || item.Column == "Based_In" || item.Column == "Style"))
                    BuildWorkerFacts();
                if (item.Table == "tblWorker" && item.Column == "Picture")
                    UpdateWorkerPortrait();
            };
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
        }
        destination.Children.Add(grid);
    }

    private void AddSectionHeader(StackPanel panel, string title)
    {
        panel.Children.Add(new TextBlock {
            Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 13, 0, 13),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D5E6FB")!)
        });
    }

    private void BuildProfileFields()
    {
        ProfileFieldsPanel.Children.Clear();
        var profile = _fields.Where(x => x.Table == "tblWorker").ToList();
        if (profile.Count == 0)
        {
            AddSectionHeader(ProfileFieldsPanel, "No profile record is available for this worker.");
            return;
        }
        // The approved mockup groups common worker fields first, then provides
        // a separate Additional Details section, then opt-in advanced columns.
        // This retains full editing access without a wall of raw MDB columns.
        var core = new[] { "Name", "Gender", "Birthday", "Nationality", "Based_In", "Active", "Style" };
        var details = new[] { "Shortname", "ShortName", "Picture", "AlsoKnownAs", "CommentaryName",
            "RetirementDate", "DebutDate" };
        var coreFields = core.Select(name => profile.FirstOrDefault(x =>
            x.Column.Equals(name, StringComparison.OrdinalIgnoreCase)))
            .Where(x => x != null).Cast<FieldDraft>().Distinct().ToList();
        var detailFields = details.Select(name => profile.FirstOrDefault(x =>
            x.Column.Equals(name, StringComparison.OrdinalIgnoreCase)))
            .Where(x => x != null && !coreFields.Contains(x)).Cast<FieldDraft>()
            .Distinct().ToList();

        foreach (var field in coreFields) AddField(ProfileFieldsPanel, field);
        if (detailFields.Count > 0)
        {
            ProfileFieldsPanel.Children.Add(new Border {
                Height = 1, Background = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#38506A")!),
                Margin = new Thickness(0,18,0,5)
            });
            AddSectionHeader(ProfileFieldsPanel, "◈   Additional Details");
            foreach (var field in detailFields) AddField(ProfileFieldsPanel, field);
        }

        var advanced = profile.Except(coreFields).Except(detailFields).ToList();
        if (advanced.Count > 0)
        {
            var advancedFields = new StackPanel { Visibility = Visibility.Collapsed };
            foreach (var field in advanced) AddField(advancedFields, field);
            var toggle = new Button {
                Style = (Style)FindResource("ActionButton"),
                Content = $"Show {advanced.Count} advanced TEW IX fields",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0,18,0,9),
                FontSize = 12
            };
            toggle.Click += (_, _) => {
                bool showing = advancedFields.Visibility == Visibility.Visible;
                advancedFields.Visibility = showing ? Visibility.Collapsed : Visibility.Visible;
                toggle.Content = showing
                    ? $"Show {advanced.Count} advanced TEW IX fields"
                    : "Hide advanced TEW IX fields";
            };
            ProfileFieldsPanel.Children.Add(toggle);
            ProfileFieldsPanel.Children.Add(advancedFields);
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
        if (BioCountText != null) BioCountText.Text = $"{BioEditor.Text.Length:N0} characters";
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
            WorkerPortraitEllipse.Fill = Brushes.Transparent;
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
            RefreshPortraits();
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
