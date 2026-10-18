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
    private string _activeModule = "Workers";
    private readonly List<FieldDraft> _existingFields = new();
    private List<ExistingRecordItem> _availableExistingRecords = new();
    private ExistingRecordItem? _selectedExisting;
    private bool _suppressExistingSelection;
    private List<GeneratedTitleReign> _generatedTitleReigns = new();
    private int? _draftTitleId;
    private int _existingLineageSlots;
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

    private bool HasEdits() => _activeModule == "Workers"
        ? (_fields.Any(x => x.Dirty) || (_bioExists && BioEditor.Text != _originalBio))
        : _existingFields.Any(x => x.Dirty);

    private bool MayLeave()
    {
        if (!HasEdits()) return true;
        return MessageBox.Show(this,
            "Discard unsaved changes? Select Save Changes first if you want to keep them.",
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
        SaveButton.IsEnabled = _session != null && (_activeModule == "Workers"
            ? _worker != null : _selectedExisting != null);
        BackupButton.IsEnabled = _session != null;
        SaveAsButton.IsEnabled = _session != null;
        WorkingText.Text = _session == null ? "No active database" :
            "Active Database: " + Path.GetFileName(_session.PathOnDisk);
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
        var workerName = ValueFor("Name") ?? _worker?.Name;
        var picture = ValueFor("Picture") ?? _worker?.PictureName;
        var portrait = Portraits.LoadBrush(workerName, picture, 260);
        WorkerPortraitEllipse.Fill = ((Brush?)portrait) ?? Brushes.Transparent;
        // If a PNG has transparency, the old silhouette/background must NOT
        // remain visible through it. Only restore the placeholder if no file loads.
        WorkerPortraitPlaceholder.Visibility = portrait == null
            ? Visibility.Visible : Visibility.Collapsed;
        WorkerPortraitEllipse.ToolTip = portrait == null
            ? "No matching worker-name or MDB picture was found in the chosen folder"
            : "Portrait matched by worker name, with MDB filename as fallback";
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
                {
                    WorkerNameText.Text = string.IsNullOrWhiteSpace(text.Text) ? "(unnamed)" : text.Text;
                    UpdateWorkerPortrait();
                }
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
        if (_activeModule != "Workers")
        {
            SaveExisting();
            return;
        }
        if (_session == null || _worker == null) return;
        try
        {
            var edits = _fields.Where(x => x.Dirty).Select(x => x.ToEdit()).ToList();
            if (_bioExists && BioEditor.Text != _originalBio)
                edits.Add(new FieldEdit("tblWorkerBio", "UID", "Profile", typeof(string), BioEditor.Text));
            if (edits.Count == 0) { SetStatus("No changes to save"); return; }
            if (MessageBox.Show(this,
                    $"Save {edits.Count} changed field(s) directly to the active MDB?\n" +
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
            SetStatus($"Saved {count} field(s) to active MDB. No backup created.");
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
        if (HasEdits()) { MessageBox.Show(this, "Save or discard changes before exporting a copy."); return; }
        var dialog = new SaveFileDialog {
            Filter = "Access database (*.mdb)|*.mdb", FileName =
            Path.GetFileNameWithoutExtension(_session.PathOnDisk) + "-copy.mdb",
            InitialDirectory = _session.ProjectFolder, OverwritePrompt = false
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            _session.CopyTo(dialog.FileName);
            SetStatus("Exported an independent copy. The active database has NOT changed.");
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
        var file = new OpenFileDialog {
            Title = "Choose the TEW IX scenario MDB to edit directly",
            Filter = "Microsoft Access scenario (*.mdb)|*.mdb"
        };
        if (File.Exists(_settings.ActiveDatabasePath))
            file.FileName = _settings.ActiveDatabasePath;
        if (file.ShowDialog(this) != true) return;
        if (MessageBox.Show(this,
                "TEW Universe Studio will EDIT this database DIRECTLY:\n\n" +
                file.FileName +
                "\n\nClose the TEW IX database editor before continuing. " +
                "Create Backup is available separately. Proceed?",
                "Confirm Direct Database Editing",
                MessageBoxButton.YesNo, MessageBoxImage.Warning)
            != MessageBoxResult.Yes) return;
        try
        {
            var opened = OpenWithCredential(p => DatabaseSession.OpenDatabase(file.FileName, p));
            if (opened.HasValue) SetSession(opened.Value.session, opened.Value.password);
        }
        catch (Exception ex) { Error(ex); }
    }

    private void RefreshDatabase_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null || _sessionPassword == null)
        {
            MessageBox.Show(this, "Select a database first.");
            return;
        }
        if (!MayLeave()) return;
        try
        {
            var opened = DatabaseSession.OpenDatabase(_session.PathOnDisk, _sessionPassword);
            SetSession(opened, _sessionPassword);
            SetStatus("Refreshed active MDB. External additions and updates have been reloaded.");
        }
        catch (Exception ex) { Error(ex); }
    }

    private void RestoreProject()
    {
        // Older settings do not automatically activate any copy as a direct-edit target.
        if (string.IsNullOrWhiteSpace(_settings.ActiveDatabasePath))
        {
            SetStatus("Select Database to choose the MDB to edit directly.");
            return;
        }
        if (!File.Exists(_settings.ActiveDatabasePath))
        {
            SetStatus("Active MDB not found. Choose Select Database to locate it.");
            return;
        }
        if (MessageBox.Show(this,
                "Reopen this MDB for DIRECT editing?\n\n" + _settings.ActiveDatabasePath +
                "\n\nDo not edit it simultaneously in TEW IX.",
                "Open Active Database", MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        try
        {
            var opened = OpenWithCredential(p =>
                DatabaseSession.OpenDatabase(_settings.ActiveDatabasePath, p));
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
            copy.ActiveDatabasePath = opened.PathOnDisk;
            copy.Save();
            _session?.Dispose();
            _session = opened;
            _sessionPassword = password;
            _settings = copy;
            _allWorkers = list;
            _worker = null;
            WorkerPortraitEllipse.Fill = Brushes.Transparent;
            WorkerPortraitPlaceholder.Visibility = Visibility.Visible;
            _fields.Clear();
            _selectedExisting = null;
            _existingFields.Clear();
            _availableExistingRecords.Clear();
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
            if (_activeModule != "Workers") LoadExistingCatalog();
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
        bool changed = !AppSettings.SamePath(next.ActiveDatabasePath, _settings.ActiveDatabasePath) &&
            !(string.IsNullOrWhiteSpace(next.ActiveDatabasePath) && string.IsNullOrWhiteSpace(_settings.ActiveDatabasePath));
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
                _existingFields.Clear();
                _availableExistingRecords.Clear();
                _selectedExisting = null;
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
        if (_session == null)
        {
            MessageBox.Show(this, "Select a database first.");
            return;
        }
        Process.Start(new ProcessStartInfo(_session.ProjectFolder) { UseShellExecute = true });
    }

    private void WorkersNav_Click(object sender, RoutedEventArgs e)
    {
        SelectModule("Workers");
        if (_activeModule == "Workers") ShowSection(_currentTab);
    }

    private void FutureNav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string section) return;
        if (ExistingEditors.TryGet(section, out _))
        {
            SelectModule(section);
            return;
        }
        MessageBox.Show(this,
            $"{section} is not yet available for editing. Existing records only; " +
            "new record creation and unverified linked-record changes remain disabled.",
            "Module not implemented", MessageBoxButton.OK, MessageBoxImage.Information);
    }


    private void SelectModule(string section)
    {
        if (_activeModule == section) return;
        if (!MayLeave()) return;

        // Confirmation to discard changes means reload the selected worker from disk
        // when we eventually return, rather than preserving stale unsaved form text.
        _activeModule = section;
        bool workers = section == "Workers";
        WorkerBrowserPanel.Visibility = workers ? Visibility.Visible : Visibility.Collapsed;
        WorkerDetailPanel.Visibility = workers ? Visibility.Visible : Visibility.Collapsed;
        ExistingWorkspace.Visibility = workers ? Visibility.Collapsed : Visibility.Visible;
        ClearGeneratedLineage();
        TitleEditorTabs.Visibility = section == "Titles" ? Visibility.Visible : Visibility.Collapsed;
        ShowTitleDetails();

        var navigation = new[] {
            (Section: "Workers", Button: WorkersNav),
            (Section: "Companies", Button: CompaniesNav),
            (Section: "Titles", Button: TitlesNav),
            (Section: "Events", Button: EventsNav),
            (Section: "Stables", Button: StablesNav)
        };
        foreach (var item in navigation)
        {
            item.Button.Style = (Style)FindResource(
                item.Section == section ? "BlueButton" : "NavigationButton");
            item.Button.HorizontalContentAlignment = HorizontalAlignment.Left;
        }

        _existingFields.Clear();
        _selectedExisting = null;
        if (workers)
        {
            // Reset the worker editor after a confirmed discard, preserving the selection.
            if (_worker != null) LoadWorker(_worker);
            FilterWorkers(_worker?.Id);
            SetStatus("Workers editor ready");
        }
        else
        {
            ExistingListTitle.Text = section == "Titles" ? "Championships" : section;
            ExistingRecordTitle.Text = "Select an existing " +
                (section == "Titles" ? "championship" : section.TrimEnd('s').ToLowerInvariant());
            ExistingRecordSubtitle.Text = "All record IDs and linked references remain unchanged.";
            ExistingFieldsTitle.Text = section + " Information";
            ExistingFieldsPanel.Children.Clear();
            ExistingSearch.Clear();
            LoadExistingCatalog();
        }
        RefreshDirty();
    }

    private void LoadExistingCatalog()
    {
        _availableExistingRecords.Clear();
        _selectedExisting = null;
        _existingFields.Clear();
        _suppressExistingSelection = true;
        ExistingList.ItemsSource = null;
        ExistingList.SelectedItem = null;
        _suppressExistingSelection = false;
        ExistingFieldsPanel.Children.Clear();
        ClearGeneratedLineage();
        TitleLineageGrid.ItemsSource = null;
        TitleLineageSummary.Text = "Select a championship to view its lineage.";
        if (_session == null)
        {
            ExistingCount.Text = "Open an editable database to browse records.";
            RefreshDirty();
            return;
        }
        try
        {
            _availableExistingRecords = _session.ListExistingRecords(_activeModule);
            FilterExistingRecords();
            SetStatus($"Loaded {_availableExistingRecords.Count:N0} existing {_activeModule.ToLowerInvariant()}");
        }
        catch (Exception ex) { Error(ex); }
        RefreshDirty();
    }

    private void ExistingSearch_TextChanged(object sender, TextChangedEventArgs e)
        => FilterExistingRecords();

    private void FilterExistingRecords(int? selectedId = null)
    {
        if (ExistingList == null) return;
        var term = ExistingSearch?.Text?.Trim() ?? "";
        var result = _availableExistingRecords.Where(x =>
            x.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            x.Id.ToString(CultureInfo.InvariantCulture).Contains(term, StringComparison.OrdinalIgnoreCase))
            .ToList();
        _suppressExistingSelection = true;
        ExistingList.ItemsSource = result;
        int? id = selectedId ?? _selectedExisting?.Id;
        ExistingList.SelectedItem = id.HasValue
            ? result.FirstOrDefault(x => x.Id == id.Value) : null;
        _suppressExistingSelection = false;
        ExistingCount.Text = $"Showing {result.Count:N0} of {_availableExistingRecords.Count:N0} records";
    }

    private void ExistingList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressExistingSelection || _session == null ||
            ExistingList.SelectedItem is not ExistingRecordItem selected ||
            selected.Id == _selectedExisting?.Id) return;

        if (!MayLeave())
        {
            _suppressExistingSelection = true;
            ExistingList.SelectedItem = _selectedExisting == null
                ? null : ExistingList.Items.Cast<ExistingRecordItem>()
                    .FirstOrDefault(x => x.Id == _selectedExisting.Id);
            _suppressExistingSelection = false;
            return;
        }
        LoadExistingRecord(selected);
    }

    private void LoadExistingRecord(ExistingRecordItem selected)
    {
        if (_session == null || !ExistingEditors.TryGet(_activeModule, out var spec)) return;
        try
        {
            var record = _session.ReadExistingRecord(_activeModule, selected.Id);
            if (!record.Exists || record.Data.Rows.Count != 1)
                throw new InvalidOperationException("The selected record is missing from the working database.");

            _selectedExisting = selected;
            ClearGeneratedLineage();
            TitleLineageGrid.ItemsSource = null;
            TitleLineageSummary.Text = "Open Title Lineage to load pre-game reigns.";
            _existingFields.Clear();
            ExistingFieldsPanel.Children.Clear();
            ExistingRecordTitle.Text = selected.Name;
            ExistingRecordSubtitle.Text =
                $"Record ID {selected.Id}  •  {spec.Table}  •  Existing record only";
            ExistingFieldsTitle.Text = _activeModule == "Titles"
                ? "Championship Information" : _activeModule + " Information";

            var row = record.Data.Rows[0];
            foreach (var name in spec.EditableColumns)
            {
                if (!record.Data.Columns.Contains(name)) continue;
                var column = record.Data.Columns[name]!;
                if (!DatabaseSession.IsEditable(column.DataType)) continue;
                var original = DatabaseSession.Display(row[column]);
                var field = new FieldDraft {
                    Table = spec.Table, Key = "UID", Column = name,
                    ValueType = column.DataType, Original = original, Value = original
                };
                _existingFields.Add(field);
                AddExistingField(field);
            }
            if (_existingFields.Count == 0)
                ExistingFieldsPanel.Children.Add(new TextBlock {
                    Text = "No approved fields are present in this database record.",
                    Foreground = Muted, TextWrapping = TextWrapping.Wrap
                });
            if (_activeModule == "Titles" && TitleLineagePanel.Visibility == Visibility.Visible)
                LoadTitleLineage();
            SetStatus($"Editing existing {_activeModule.ToLowerInvariant()} record {selected.Id}");
            RefreshDirty();
        }
        catch (Exception ex) { Error(ex); }
    }

    private void TitleLineageGrid_AutoGeneratingColumn(object sender,
        DataGridAutoGeneratingColumnEventArgs e)
    {
        if (e.Column is DataGridTextColumn col)
        {
            col.Width = new DataGridLength(1, DataGridLengthUnitType.Auto);
            col.MinWidth = 110;
            col.MaxWidth = 230;
            if (e.PropertyType == typeof(DateTime))
                col.Binding = new System.Windows.Data.Binding(e.PropertyName) {
                    StringFormat = "MMM d, yyyy"
                };
        }
    }

    private void ClearGeneratedLineage()
    {
        _generatedTitleReigns.Clear();
        _draftTitleId = null;
        _existingLineageSlots = 0;
        if (ApplyGeneratedLineageButton != null)
            ApplyGeneratedLineageButton.IsEnabled = false;
        if (ExportGeneratedLineageButton != null)
            ExportGeneratedLineageButton.IsEnabled = false;
    }

    private void UseRecordedChampions_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null || _selectedExisting == null || _activeModule != "Titles") return;
        var data = _session.ReadTitleLineage(_selectedExisting.Id);
        if (data.LinkField == null) return;
        var names = data.Rows.Rows.Cast<DataRow>().SelectMany(row =>
            new[] { "Holder_Name1", "Holder_Name2" }
                .Where(data.Rows.Columns.Contains)
                .Select(field => DatabaseSession.Display(row[field])))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        LineageWorkerPool.Text = string.Join(Environment.NewLine, names);
        GeneratorHelp.Text = $"Loaded {names.Count} recorded holder name(s). " +
            "Only names matching an existing worker can be used for generated reigns.";
    }

    private void GenerateLineage_Click(object sender, RoutedEventArgs e)
    {
        if (_activeModule != "Titles" || _selectedExisting == null || _session == null) return;
        try
        {
            if (!int.TryParse(LineageReignCount.Text.Trim(), out int count) || count < 1 || count > 100)
                throw new InvalidOperationException("Choose 1 to 100 reigns.");
            if (!int.TryParse(LineageStartYear.Text.Trim(), out int startYear)
                || !int.TryParse(LineageEndYear.Text.Trim(), out int endYear)
                || startYear < 1800 || endYear > 2100 || startYear > endYear)
                throw new InvalidOperationException("Choose a valid year range from 1800 to 2100.");
            bool tag = LineageTeamType.SelectedIndex == 1;
            var tokens = LineageWorkerPool.Text.Split(
                new[] { '\r', '\n', ';' }, StringSplitOptions.TrimEntries
                    | StringSplitOptions.RemoveEmptyEntries);
            var eligible = new List<WorkerItem>();
            foreach (var token in tokens)
            {
                WorkerItem? worker;
                if (token.StartsWith("#", StringComparison.Ordinal)
                    && int.TryParse(token[1..], out int id))
                    worker = _allWorkers.FirstOrDefault(w => w.Id == id);
                else
                {
                    var matching = _allWorkers.Where(w =>
                        w.Name.Equals(token, StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (matching.Length > 1)
                        throw new InvalidOperationException(
                            $"More than one worker is named '{token}'. Use #workerID instead.");
                    worker = matching.FirstOrDefault();
                }
                if (worker == null)
                    throw new InvalidOperationException(
                        $"'{token}' is not an existing worker. Enter an exact name or #workerID.");
                if (worker.Name.Length > 30)
                    throw new InvalidOperationException(
                        $"'{worker.Name}' is longer than the TEW IX title holder name limit.");
                if (eligible.All(w => w.Id != worker.Id)) eligible.Add(worker);
            }
            if (eligible.Count < (tag ? 2 : 1))
                throw new InvalidOperationException(
                    tag ? "Choose at least two existing workers for tag team reigns." :
                          "Choose at least one existing worker for singles reigns.");

            var start = new DateTime(startYear, 1, 1);
            var end = new DateTime(endYear, 12, 31);
            int days = (end - start).Days + 1;
            if (days < count)
                throw new InvalidOperationException("The selected date range is too short.");

            var proposed = new List<GeneratedTitleReign>();
            int previous = -1, previousPartner = -1;
            for (int i = 0; i < count; i++)
            {
                var choices = eligible.Where(w => w.Id != previous).ToList();
                if (choices.Count == 0) choices = eligible;
                var first = choices[Random.Shared.Next(choices.Count)];
                WorkerItem? second = null;
                if (tag)
                {
                    var partners = eligible.Where(w => w.Id != first.Id
                        && !(w.Id == previousPartner && first.Id == previous)).ToList();
                    if (partners.Count == 0)
                        partners = eligible.Where(w => w.Id != first.Id).ToList();
                    second = partners[Random.Shared.Next(partners.Count)];
                }
                int captureDays = (int)((long)i * days / count);
                int nextDays = (int)((long)(i + 1) * days / count);
                DateTime captured = start.AddDays(captureDays);
                DateTime lost = start.AddDays(nextDays - 1);
                short defences = (short)Math.Min(short.MaxValue,
                    Math.Max(0, (nextDays - captureDays) / 45));
                proposed.Add(new GeneratedTitleReign(
                    i + 1, first.Id, first.Name, second?.Id ?? 0, second?.Name ?? "",
                    captured, lost, defences));
                previous = first.Id;
                previousPartner = second?.Id ?? -1;
            }

            var history = _session.ReadTitleLineage(_selectedExisting.Id);
            _existingLineageSlots = history.LinkField == null ? 0 : history.Rows.Rows.Count;
            _draftTitleId = _selectedExisting.Id;
            _generatedTitleReigns = proposed;
            var preview = new DataTable("Generated Reigns");
            preview.Columns.Add("Reign", typeof(int));
            preview.Columns.Add("Champion", typeof(string));
            preview.Columns.Add("Partner", typeof(string));
            preview.Columns.Add("Captured", typeof(DateTime));
            preview.Columns.Add("Lost", typeof(DateTime));
            preview.Columns.Add("Defences", typeof(short));
            preview.Columns.Add("Worker IDs", typeof(string));
            foreach (var reign in proposed)
                preview.Rows.Add(reign.Order, reign.HolderName1, reign.HolderName2,
                    reign.Captured, reign.Lost, reign.Defences,
                    reign.Holder2 > 0 ? $"{reign.Holder1}, {reign.Holder2}" :
                        reign.Holder1.ToString(CultureInfo.InvariantCulture));
            TitleLineageGrid.ItemsSource = preview.DefaultView;
            var canApply = _existingLineageSlots == count &&
                history.LinkField?.Equals("BeltUID", StringComparison.OrdinalIgnoreCase) == true &&
                history.OrderField?.Equals("BeltOrder", StringComparison.OrdinalIgnoreCase) == true;
            ApplyGeneratedLineageButton.IsEnabled = canApply;
            ExportGeneratedLineageButton.IsEnabled = true;
            TitleLineageSummary.Text = $"GENERATED PREVIEW · {count} reign(s) · " +
                $"{_existingLineageSlots} existing history slot(s) for this championship.";
            GeneratorHelp.Text = canApply
                ? "Preview only. Replace Existing Reigns will overwrite holders, captured/lost dates, " +
                  "and defences for this title's existing history rows, after confirmation. " +
                  "Other titles and history record IDs remain unchanged."
                : $"This title has {_existingLineageSlots} existing lineage slot(s). " +
                  "You can export the draft, but applying requires exactly one generated reign " +
                  "per existing slot. No new MDB records are inserted.";
            SetStatus($"Generated {count} draft reigns for {_selectedExisting.Name}");
        }
        catch (Exception ex) { Error(ex); }
    }

    private void ApplyGeneratedLineage_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null || _activeModule != "Titles" ||
            _selectedExisting == null || _draftTitleId != _selectedExisting.Id ||
            _generatedTitleReigns.Count == 0) return;
        if (HasEdits())
        {
            MessageBox.Show(this, "Save or discard unsaved championship details before replacing lineage.");
            return;
        }
        if (MessageBox.Show(this,
                $"Replace ALL {_generatedTitleReigns.Count} existing reigns for " +
                $"{_selectedExisting.Name} (ID {_selectedExisting.Id})?\n\n" +
                "This changes historical holders, capture dates, loss dates and defences " +
                "in your active MDB. It cannot be undone without a backup.\n\n" +
                "No new history records are created. Please make a backup first.",
                "Replace Championship Lineage", MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            int changed = _session.ReplaceExistingTitleLineage(
                _selectedExisting.Id, _generatedTitleReigns);
            _lastSaved = DateTime.Now;
            ClearGeneratedLineage();
            LoadTitleLineage();
            RefreshDirty();
            SetStatus($"Replaced {changed} existing lineage reign(s). Please validate in TEW IX.");
        }
        catch (Exception ex) { Error(ex); }
    }

    private void ExportGeneratedLineage_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedExisting == null || _generatedTitleReigns.Count == 0 ||
            _draftTitleId != _selectedExisting.Id) return;
        var dialog = new SaveFileDialog {
            Title = "Export title-lineage draft (not a verified TEW IX import file)",
            FileName = $"title-lineage-{_selectedExisting.Id}-draft.csv",
            Filter = "CSV document (*.csv)|*.csv", AddExtension = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            static string Csv(object? o)
            {
                var value = Convert.ToString(o, CultureInfo.InvariantCulture) ?? "";
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }
            var lines = new List<string> {
                "BeltUID,BeltOrder,Holder1,Holder_Name1,Holder2,Holder_Name2,BeltCaptured,BeltLost,Defences"
            };
            lines.AddRange(_generatedTitleReigns.Select(r => string.Join(",",
                Csv(_selectedExisting.Id), Csv(r.Order), Csv(r.Holder1),
                Csv(r.HolderName1), Csv(r.Holder2), Csv(r.HolderName2),
                Csv(r.Captured.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                Csv(r.Lost.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                Csv(r.Defences))));
            File.WriteAllLines(dialog.FileName, lines, System.Text.Encoding.UTF8);
            SetStatus("Exported title-lineage draft CSV. Not verified for direct TEW IX import.");
        }
        catch (Exception ex) { Error(ex); }
    }

    private void ShowSavedLineage_Click(object sender, RoutedEventArgs e)
        => LoadTitleLineage();

    private void TitleInfoTab_Click(object sender, RoutedEventArgs e) => ShowTitleDetails();

    private void TitleLineageTab_Click(object sender, RoutedEventArgs e)
    {
        if (_activeModule != "Titles") return;
        TitleLineagePanel.Visibility = Visibility.Visible;
        ExistingDetailsPanel.Visibility = Visibility.Collapsed;
        TitleInfoTab.Style = (Style)FindResource("ActionButton");
        TitleLineageTab.Style = (Style)FindResource("BlueButton");
        LoadTitleLineage();
    }

    private void ShowTitleDetails()
    {
        if (TitleLineagePanel == null || ExistingDetailsPanel == null) return;
        TitleLineagePanel.Visibility = Visibility.Collapsed;
        ExistingDetailsPanel.Visibility = Visibility.Visible;
        TitleInfoTab.Style = (Style)FindResource("BlueButton");
        TitleLineageTab.Style = (Style)FindResource("ActionButton");
    }

    private void LoadTitleLineage()
    {
        if (_activeModule != "Titles" || _session == null || _selectedExisting == null)
        {
            TitleLineageGrid.ItemsSource = null;
            TitleLineageSummary.Text = "Select a championship to view its lineage.";
            return;
        }
        try
        {
            var history = _session.ReadTitleLineage(_selectedExisting.Id);
            TitleLineageGrid.ItemsSource = history.LinkField == null
                ? null : history.Rows.DefaultView;
            TitleLineageSummary.Text = history.Message;
            if (_generatedTitleReigns.Count == 0 && history.Rows.Rows.Count > 0)
            {
                LineageReignCount.Text = history.Rows.Rows.Count.ToString(CultureInfo.InvariantCulture);
                if (history.Rows.Columns.Contains("Holder2"))
                {
                    bool tag = history.Rows.Rows.Cast<DataRow>().Any(row =>
                        row["Holder2"] != DBNull.Value &&
                        Convert.ToInt32(row["Holder2"], CultureInfo.InvariantCulture) > 0);
                    LineageTeamType.SelectedIndex = tag ? 1 : 0;
                }
            }
            SetStatus(history.LinkField == null
                ? "Lineage schema needs review" : $"Read {history.Rows.Rows.Count:N0} title lineage entries.");
        }
        catch (Exception ex)
        {
            TitleLineageGrid.ItemsSource = null;
            TitleLineageSummary.Text = "Unable to load title lineage. " + ex.Message;
        }
    }

    private void AddExistingField(FieldDraft field)
    {
        var card = new Border {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#142840")!),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#304C6B")!),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7),
            Padding = new Thickness(13,10,13,12), Margin = new Thickness(0,0,0,10)
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock {
            Text = FriendlyName(field.Column),
            FontSize = 12, Foreground = Muted, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0,0,0,7),
            ToolTip = $"{field.Table}.{field.Column}"
        });

        if (field.ValueType == typeof(bool))
        {
            var combo = new ComboBox {
                Style = (Style)FindResource("DarkComboBox"),
                ItemsSource = new[] { "Yes", "No" },
                SelectedIndex = field.Value.Equals("True", StringComparison.OrdinalIgnoreCase) ||
                    field.Value == "1" ? 0 :
                    field.Value.Equals("False", StringComparison.OrdinalIgnoreCase) ||
                    field.Value == "0" ? 1 : -1,
                ToolTip = field.Table + "." + field.Column
            };
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedIndex == -1) return;
                field.Value = combo.SelectedIndex == 0 ? "True" : "False";
                RefreshDirty();
            };
            stack.Children.Add(combo);
        }
        else
        {
            bool multiline = field.Column.Equals("Profile", StringComparison.OrdinalIgnoreCase);
            var entry = new TextBox {
                Style = (Style)FindResource("FormTextBox"),
                Text = field.Value,
                TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
                AcceptsReturn = multiline,
                MinHeight = multiline ? 150 : 35,
                VerticalScrollBarVisibility = multiline
                    ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
                VerticalContentAlignment = multiline
                    ? VerticalAlignment.Top : VerticalAlignment.Center,
                ToolTip = field.ValueType == typeof(DateTime)
                    ? "Date format: YYYY-MM-DD (stored in the original date field)"
                    : $"{field.Table}.{field.Column} ({field.ValueType.Name})"
            };
            entry.TextChanged += (_, _) =>
            {
                field.Value = entry.Text;
                if (field.Column == "Name")
                    ExistingRecordTitle.Text = string.IsNullOrWhiteSpace(field.Value)
                        ? "(unnamed record)" : field.Value;
                RefreshDirty();
            };
            stack.Children.Add(entry);
        }
        card.Child = stack;
        ExistingFieldsPanel.Children.Add(card);
    }

    private void SaveExisting()
    {
        if (_session == null || _selectedExisting == null) return;
        var edits = _existingFields.Where(x => x.Dirty).Select(x => x.ToEdit()).ToList();
        if (edits.Count == 0) { SetStatus("No changes to save"); return; }
        if (MessageBox.Show(this,
                $"Update {edits.Count} field(s) of {_selectedExisting.Name} directly in the active MDB?\n\n" +
                "The record UID stays the same. No backup is created automatically.",
                "Confirm Existing Record Update", MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        try
        {
            int id = _selectedExisting.Id;
            int updated = _session.SaveExistingRecord(_activeModule, id, edits);
            _lastSaved = DateTime.Now;
            _availableExistingRecords = _session.ListExistingRecords(_activeModule);
            var refreshed = _availableExistingRecords.FirstOrDefault(x => x.Id == id);
            if (refreshed != null) LoadExistingRecord(refreshed);
            FilterExistingRecords(id);
            SetStatus($"Saved {updated} {_activeModule.ToLowerInvariant()} field(s). No backup created.");
        }
        catch (Exception ex) { Error(ex); }
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
