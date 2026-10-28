using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using TEWUniverse.Studio;

namespace TEWUniverse.Desktop;

public partial class MainWindow
{
    private HistoricalDateWindow ResolveTitleTimeline(
        int fromYear, int throughYear, int startingMonth,
        bool respectCompany, string manualEnd, bool advanced = true)
    {
        if (_session == null || _selectedExisting == null)
            throw new InvalidOperationException("Select a championship and database first.");
        int? selectedId = advanced ? EmploymentCompanyUid() :
            LineageCompany.SelectedItem is ExistingRecordItem selected && selected.Id > 0
                ? selected.Id : _session.ReadTitleOriginalCompany(_selectedExisting.Id);
        if (respectCompany && !selectedId.HasValue)
            throw new InvalidOperationException(
                "This championship has no OriginalFed company link. Choose its historical " +
                "company from the company selector above, or uncheck Respect company dates " +
                "to use a manual final date. No unrestricted history will be generated silently.");
        var company = _session.ReadTitleScenarioChronology(
            _selectedExisting.Id, selectedId, includeCompany: respectCompany);
        DateTime? overrideDate = string.IsNullOrWhiteSpace(manualEnd)
            ? null : StudioDateFormat.Parse(manualEnd);
        var range = ScenarioDateBounds.Resolve(
            ScenarioDateBounds.MonthStart(fromYear, startingMonth),
            ScenarioDateBounds.MonthEnd(throughYear, 12),
            company, respectCompany, overrideDate);
        string origin = respectCompany
            ? company.CompanyName + ": " : "Manual company-date override: ";
        return range with { Explanation = origin + range.Explanation +
            " Effective period " + StudioDateFormat.Format(range.From) +
            " through " + StudioDateFormat.Format(range.Through) + "." };
    }

    private void UpdateTitleTimelineGuidance()
    {
        if (_selectedExisting == null || _session == null) return;
        try
        {
            int? selectedId = _activeModule == "Titles" &&
                AdvancedLineagePanel.Visibility == Visibility.Visible
                ? EmploymentCompanyUid()
                : LineageCompany.SelectedItem is ExistingRecordItem choice && choice.Id > 0
                    ? choice.Id : _session.ReadTitleOriginalCompany(_selectedExisting.Id);
            var company = _session.ReadTitleScenarioChronology(
                _selectedExisting.Id, selectedId);
            string opened = company.CompanyOpened.HasValue
                ? StudioDateFormat.Format(company.CompanyOpened.Value)
                : "unknown";
            string closed = company.CompanyClosed.HasValue
                ? StudioDateFormat.Format(company.CompanyClosed.Value)
                : "not recorded";
            string note = "Scenario begins " +
                company.ScenarioStart.ToString("MMMM yyyy", CultureInfo.InvariantCulture) +
                ". Date-limit company: " + company.CompanyName +
                " (opened " + opened + ", closed " + closed + "). " +
                "Automatic dates never extend into the scenario's first month. " +
                "Company constraints are optional; the scenario boundary always applies.";
            if (AdvancedDateLimits != null) AdvancedDateLimits.Text = note;
            if (LineageDateLimits != null) LineageDateLimits.Text = note;
        }
        catch (Exception ex)
        {
            string message = "Could not verify scenario/company dates: " + ex.Message;
            if (AdvancedDateLimits != null) AdvancedDateLimits.Text = message;
            if (LineageDateLimits != null) LineageDateLimits.Text = message;
        }
    }

    private void LineageTimelineChanged(object sender, RoutedEventArgs e)
    {
        if (ApplyGeneratedLineageButton == null) return;
        ClearGeneratedLineage();
    }

    private void EditScenarioStart_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null)
        {
            MessageBox.Show(this, "Open a scenario MDB to edit its start date.");
            return;
        }
        if (HasEdits())
        {
            MessageBox.Show(this,
                "Save or discard current record edits before changing the scenario date.");
            return;
        }

        try
        {
            DateTime previous = _session.ReadScenarioStart();
            var dialog = new Window {
                Owner = this,
                Title = "Scenario Start | TEW Universe Studio",
                Width = 550, Height = 360,
                MinWidth = 470,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = ExtraColor("#10233A"),
                Foreground = ExtraColor("#E9F4FF"),
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI")
            };
            var content = new StackPanel { Margin = new Thickness(23) };
            dialog.Content = content;
            content.Children.Add(ExtraLabel("Scenario start in tblInfo", true));
            var info = ExtraLabel(
                "TEW IX stores the scenario's starting month and year, not an exact day. " +
                "Studio uses the first day of that month as the start of the game " +
                "and the previous day as the latest pre-game history date.");
            info.TextWrapping = TextWrapping.Wrap;
            info.Margin = new Thickness(0, 7, 0, 16);
            content.Children.Add(info);
            var fields = new WrapPanel();
            content.Children.Add(fields);
            var monthGroup = new StackPanel { Width = 215, Margin = new Thickness(0, 0, 14, 9) };
            monthGroup.Children.Add(ExtraLabel("Start month"));
            var monthBox = ExtraPicker();
            monthBox.ItemsSource = Enumerable.Range(1, 12).Select(month =>
                CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month)).ToArray();
            monthBox.SelectedIndex = previous.Month - 1;
            monthGroup.Children.Add(monthBox);
            fields.Children.Add(monthGroup);

            var yearGroup = new StackPanel { Width = 150 };
            yearGroup.Children.Add(ExtraLabel("Start year"));
            var yearBox = ExtraTextBox();
            yearBox.Text = previous.Year.ToString(CultureInfo.InvariantCulture);
            yearGroup.Children.Add(yearBox);
            fields.Children.Add(yearGroup);

            var caution = ExtraLabel(
                "Changing this value updates the active scenario MDB. " +
                "It does not automatically rewrite existing championship histories.");
            caution.TextWrapping = TextWrapping.Wrap;
            caution.Margin = new Thickness(0, 12, 0, 16);
            content.Children.Add(caution);
            var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
            content.Children.Add(buttons);
            buttons.Children.Add(ExtraButton("Cancel", false,
                (_, _) => dialog.DialogResult = false));
            buttons.Children.Add(ExtraButton("Save Scenario Start", true, (_, _) =>
            {
                try
                {
                    if (!int.TryParse(yearBox.Text.Trim(), NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out int year))
                        throw new InvalidOperationException("Enter a valid start year.");
                    DateTime next = ScenarioDateBounds.MonthStart(year, monthBox.SelectedIndex + 1);
                    if (next == previous)
                    {
                        dialog.DialogResult = true;
                        return;
                    }
                    if (MessageBox.Show(dialog,
                        "Change this scenario's start from " +
                        previous.ToString("MMMM yyyy", CultureInfo.InvariantCulture) +
                        " to " + next.ToString("MMMM yyyy", CultureInfo.InvariantCulture) +
                        "?\n\nExisting title histories will NOT be rewritten. " +
                        "The generator will use the new cutoff immediately.",
                        "Confirm Scenario Date", MessageBoxButton.YesNo,
                        MessageBoxImage.Warning) != MessageBoxResult.Yes)
                        return;
                    if (!OfferOptionalBackupBeforeWrite("changing the scenario start month/year"))
                        return;
                    _session!.UpdateScenarioStart(year, next.Month);
                    _lastSaved = DateTime.Now;
                    ResetAdvancedDraft();
                    ClearGeneratedLineage();
                    UpdateTitleTimelineGuidance();
                    RefreshDirty();
                    SetStatus("Scenario now begins " +
                        next.ToString("MMMM yyyy", CultureInfo.InvariantCulture) + ".");
                    dialog.DialogResult = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(dialog, ex.Message, "Scenario Date",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }));
            dialog.ShowDialog();
        }
        catch (Exception ex) { Error(ex); }
    }
}
