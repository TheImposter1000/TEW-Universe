using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TEWUniverse.Studio;

namespace TEWUniverse.Desktop;

internal sealed record WorkerFilterChoice(string Key, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Read-only roster filtering, kept separate from worker editing and title
/// generation. Every selected filter combines with all others (logical AND).
/// </summary>
public partial class MainWindow
{
    private WorkerFilterIndex? _workerFilterIndex;
    private Dictionary<int, WorkerFilterRecord> _workerFilterRecords = new();
    private bool _loadingWorkerFilters = true;
    private bool _workerPicturesEvaluated;

    private static readonly WorkerFilterChoice AllFilter =
        new("", "All");

    private static WorkerFilterChoice Choice(string key, string label) =>
        new(key, label);

    private static string FilterKey(ComboBox? combo) =>
        (combo?.SelectedItem as WorkerFilterChoice)?.Key ?? "";

    private static IEnumerable<WorkerFilterChoice> Options(
        string allLabel, IEnumerable<string> values, Func<string, string>? display = null) =>
        new[] { Choice("", allLabel) }.Concat(values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => display?.Invoke(v) ?? v, StringComparer.OrdinalIgnoreCase)
            .Select(v => Choice(v, display?.Invoke(v) ?? v)));

    private static void PopulateFilter(ComboBox combo,
        IEnumerable<WorkerFilterChoice> options, bool enabled = true)
    {
        combo.DisplayMemberPath = nameof(WorkerFilterChoice.Label);
        TextSearch.SetTextPath(combo, nameof(WorkerFilterChoice.Label));
        combo.ItemsSource = options.ToList();
        combo.SelectedIndex = 0;
        combo.IsEnabled = enabled;
    }

    private void InitializeWorkerFiltersUI()
    {
        _loadingWorkerFilters = true;
        try
        {
            PopulateFilter(WorkerActiveFilter, new[] {
                Choice("", "All statuses"), Choice("active", "Active"),
                Choice("inactive", "Inactive")
            });
            PopulateFilter(WorkerPictureFilter, new[] {
                Choice("", "All workers"), Choice("yes", "With pictures"),
                Choice("no", "No pictures")
            });
            PopulateFilter(WorkerGenderFilter, new[] { Choice("", "All genders") }, false);
            PopulateFilter(WorkerCompanyFilter, new[] { Choice("", "All companies") }, false);
            PopulateFilter(WorkerRoleFilter, new[] { Choice("", "All worker types") }, false);
            PopulateFilter(WorkerNationalityFilter,
                new[] { Choice("", "All nationalities") }, false);
            PopulateFilter(WorkerRegionFilter, new[] { Choice("", "All regions") }, false);
            WorkerMinAgeFilter.IsEnabled = WorkerMaxAgeFilter.IsEnabled = false;
            WorkerFilterToggleText.Text = "Filters (all)";
            WorkerFilterNotes.Text = "Choose a database to load roster filter choices.";
        }
        finally { _loadingWorkerFilters = false; }
    }

    private void ReloadWorkerFilterIndex()
    {
        _loadingWorkerFilters = true;
        try
        {
            _workerPicturesEvaluated = false;
            _workerFilterRecords.Clear();
            _workerFilterIndex = _session?.ReadWorkerFilterIndex(_allWorkers);
            var index = _workerFilterIndex;
            if (index == null)
            {
                InitializeWorkerFiltersUI();
                return;
            }
            _workerFilterRecords = index.Workers.ToDictionary(r => r.Id);
            PopulateFilter(WorkerGenderFilter,
                Options("All genders", index.Workers.Select(w => w.Gender),
                    code => TewCodeLabels.Display("tblWorker", "Gender", code)),
                index.HasGender);
            PopulateFilter(WorkerNationalityFilter,
                Options("All nationalities", index.Workers.Select(w => w.Nationality)),
                index.HasNationality);
            PopulateFilter(WorkerRegionFilter,
                Options("All regions", index.Workers.Select(w => w.Region)),
                index.HasRegion);
            PopulateFilter(WorkerRoleFilter,
                Options("All worker types", index.Workers.SelectMany(w => w.Roles)),
                index.HasWorkerRoles);
            PopulateFilter(WorkerCompanyFilter,
                new[] {
                    Choice("", "All companies"),
                    Choice("unemployed", "No current contract")
                }.Concat(index.Companies.OrderBy(c => c.Name,
                    StringComparer.OrdinalIgnoreCase).Select(c =>
                    Choice(c.Id.ToString(CultureInfo.InvariantCulture), c.Name))),
                index.HasContractCompanyLinks);
            WorkerMinAgeFilter.IsEnabled = index.HasBirthday;
            WorkerMaxAgeFilter.IsEnabled = index.HasBirthday;
            WorkerMinAgeFilter.Text = WorkerMaxAgeFilter.Text = "";
            WorkerActiveFilter.SelectedIndex = WorkerPictureFilter.SelectedIndex = 0;
            WorkerAgeFilterHint.Text = "Minimum                    Maximum";
            WorkerAgeFilterHint.Foreground = ExtraColor("#819FBB");
            WorkerFilterNotes.Text = string.Join("  ", new[] {
                !index.HasWorkerRoles ? "Worker roles unavailable in this MDB." : "",
                !index.HasContractCompanyLinks ? "Current company links unavailable." : "",
                !index.HasRegion ? "Based-in region unavailable." : "",
                !index.HasNationality ? "Nationality unavailable." : "",
                !index.HasGender ? "Gender unavailable." : "",
                !index.HasBirthday ? "Birth dates unavailable." : "",
                _settings.WorkerPicturesFolderPath is null or ""
                    ? "Set your worker pictures folder in Settings to filter portraits." : "",
                index.Workers.Any(w =>
                    (w.Gender.StartsWith("Code ", StringComparison.Ordinal) &&
                        !TewCodeLabels.TryGetLabel("tblWorker", "Gender", w.Gender, out _)) ||
                    w.Nationality.StartsWith("Code ", StringComparison.Ordinal) ||
                    w.Region.StartsWith("Code ", StringComparison.Ordinal))
                    ? "Unmapped numeric choices show actual MDB codes." : ""
            }.Where(x => x.Length > 0));
        }
        catch (Exception ex)
        {
            _workerFilterIndex = null;
            _workerFilterRecords.Clear();
            InitializeWorkerFiltersUI();
            WorkerFilterNotes.Text = "Cannot load advanced roster filters: " + ex.Message;
        }
        finally
        {
            _loadingWorkerFilters = false;
            UpdateWorkerFilterCount();
        }
    }

    private void InvalidateWorkerPictureFilter()
    {
        _workerPicturesEvaluated = false;
        if (_workerFilterIndex == null) return;
        _workerFilterRecords = _workerFilterIndex.Workers.ToDictionary(r => r.Id);
    }

    private void EnsureWorkerPictureFlags()
    {
        if (_workerPicturesEvaluated) return;
        var source = _allWorkers.ToDictionary(w => w.Id);
        _workerFilterRecords = _workerFilterRecords.ToDictionary(k => k.Key, k =>
        {
            if (!source.TryGetValue(k.Key, out var worker)) return k.Value;
            return k.Value with {
                HasPicture = Portraits.HasPicture(worker.Name, worker.PictureName)
            };
        });
        _workerPicturesEvaluated = true;
    }

    private static bool TryAge(string input, out int? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(input)) return true;
        if (!int.TryParse(input.Trim(), NumberStyles.None,
                CultureInfo.InvariantCulture, out int years) ||
            years is < 0 or > 120)
            return false;
        value = years;
        return true;
    }

    private bool TryWorkerCriteria(out WorkerFilterCriteria criteria)
    {
        bool minValid = TryAge(WorkerMinAgeFilter.Text, out int? min);
        bool maxValid = TryAge(WorkerMaxAgeFilter.Text, out int? max);
        bool valid = minValid && maxValid &&
            !(min.HasValue && max.HasValue && min > max);
        WorkerAgeFilterHint.Foreground = valid
            ? ExtraColor("#819FBB") : ExtraColor("#F4A9A9");
        WorkerAgeFilterHint.Text = valid
            ? "Minimum                    Maximum"
            : "Enter ages 0–120, with minimum ≤ maximum";
        criteria = new WorkerFilterCriteria(
            SearchBox?.Text ?? "",
            FilterKey(WorkerActiveFilter) switch {
                "active" => true,
                "inactive" => false,
                _ => null
            },
            EmptyToNull(FilterKey(WorkerGenderFilter)),
            min, max,
            int.TryParse(FilterKey(WorkerCompanyFilter),
                NumberStyles.Integer, CultureInfo.InvariantCulture,
                out int company) ? company : null,
            FilterKey(WorkerCompanyFilter) == "unemployed",
            EmptyToNull(FilterKey(WorkerRoleFilter)),
            EmptyToNull(FilterKey(WorkerNationalityFilter)),
            EmptyToNull(FilterKey(WorkerRegionFilter)),
            FilterKey(WorkerPictureFilter) switch {
                "yes" => true, "no" => false, _ => null
            });
        return valid;
    }

    private static string? EmptyToNull(string text) =>
        string.IsNullOrWhiteSpace(text) ? null : text;

    private IEnumerable<WorkerItem> ApplyWorkerRosterFilters(
        IEnumerable<WorkerItem> searchMatches)
    {
        if (_workerFilterIndex == null) return searchMatches;
        if (!TryWorkerCriteria(out var criteria))
            return Enumerable.Empty<WorkerItem>();
        if (criteria.HasPicture.HasValue)
            EnsureWorkerPictureFlags();
        DateTime today = DateTime.Today;
        return searchMatches.Where(worker =>
            _workerFilterRecords.TryGetValue(worker.Id, out var record) &&
            WorkerRosterFilterEngine.Matches(record, criteria, today));
    }

    private void WorkerFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingWorkerFilters || WorkerFilterToggleText == null) return;
        UpdateWorkerFilterCount();
        FilterWorkers();
    }

    private void WorkerAgeFilterChanged(object sender, TextChangedEventArgs e)
    {
        if (_loadingWorkerFilters || WorkerFilterToggleText == null) return;
        UpdateWorkerFilterCount();
        FilterWorkers();
    }

    private void UpdateWorkerFilterCount()
    {
        if (WorkerFilterToggleText == null) return;
        int active = new[] {
            WorkerActiveFilter, WorkerGenderFilter, WorkerCompanyFilter,
            WorkerRoleFilter, WorkerNationalityFilter, WorkerRegionFilter,
            WorkerPictureFilter
        }.Count(c => FilterKey(c).Length > 0);
        if (!string.IsNullOrWhiteSpace(WorkerMinAgeFilter?.Text) ||
            !string.IsNullOrWhiteSpace(WorkerMaxAgeFilter?.Text))
            active++;
        WorkerFilterToggleText.Text = active > 0
            ? $"Filters ({active} active)"
            : "Filters (all)";
        WorkerResetFilters.IsEnabled =
            active > 0 || !string.IsNullOrWhiteSpace(SearchBox?.Text);
    }

    private void WorkerFilterToggle_Click(object sender, RoutedEventArgs e)
    {
        WorkerFilterPanel.Visibility = WorkerFilterPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ResetWorkerFilters_Click(object sender, RoutedEventArgs e)
    {
        _loadingWorkerFilters = true;
        try
        {
            foreach (var combo in new[] {
                WorkerActiveFilter, WorkerGenderFilter, WorkerCompanyFilter,
                WorkerRoleFilter, WorkerNationalityFilter, WorkerRegionFilter,
                WorkerPictureFilter
            }) combo.SelectedIndex = 0;
            WorkerMinAgeFilter.Text = "";
            WorkerMaxAgeFilter.Text = "";
            SearchBox.Clear();
        }
        finally { _loadingWorkerFilters = false; }
        UpdateWorkerFilterCount();
        FilterWorkers(_worker?.Id);
    }
}
