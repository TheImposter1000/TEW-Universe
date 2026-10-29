using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TEWUniverse.Studio;

namespace TEWUniverse.Desktop;

/// <summary>
/// Review and explicitly repair RecordName text on ALREADY-EXISTING MDB rows.
/// This never invokes the championship generator or changes historical data.
/// </summary>
public partial class MainWindow
{
    private void ReviewExistingTitleRecordNames_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null || _selectedExisting == null || _activeModule != "Titles")
            return;
        if (HasEdits())
        {
            MessageBox.Show(this,
                "Save or discard your unsaved championship information changes " +
                "before previewing a record-name repair.");
            return;
        }
        try
        {
            int? titleUid = (sender as Button)?.Tag as string == "All"
                ? null : _selectedExisting.Id;
            var plan = _session.PreviewTitleRecordNameRepairs(titleUid);
            ShowRecordNameRepairPreview(plan);
        }
        catch (Exception ex) { Error(ex); }
    }

    private void ReviewSelectedEmploymentRecordNames_Click(object sender, RoutedEventArgs e)
        => PreviewEmploymentNameRepair(allWorkers: false);

    private void ReviewAllEmploymentRecordNames_Click(object sender, RoutedEventArgs e)
        => PreviewEmploymentNameRepair(allWorkers: true);

    private void PreviewEmploymentNameRepair(bool allWorkers)
    {
        if (_session == null || _worker == null) return;
        if (HasEdits() || HasUnappliedWorkerExtraForm())
        {
            MessageBox.Show(this,
                "Save or discard the current worker's unsaved fields / employment form " +
                "before previewing existing record-name changes.");
            return;
        }
        try
        {
            var plan = _session.PreviewEmploymentRecordNameRepairs(
                allWorkers ? null : _worker.Id);
            ShowRecordNameRepairPreview(plan);
        }
        catch (Exception ex) { Error(ex); }
    }

    private void ShowRecordNameRepairPreview(RecordNameRepairPlan plan)
    {
        if (_session == null) return;
        bool titles = plan.Table == "tblBeltPreHistory";
        string scope = plan.SelectedOwnerUid.HasValue
            ? titles ? "Selected championship" : "Selected worker"
            : titles ? "All championships" : "All employment history";
        var window = new Window
        {
            Title = $"Review Existing Record Names — {scope}",
            Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = ExtraColor("#0E1C31"), Foreground = Brushes.White,
            Width = 1050, Height = 690, MinWidth = 780, MinHeight = 480,
            ResizeMode = ResizeMode.CanResize, ShowInTaskbar = false
        };
        var layout = new Grid { Margin = new Thickness(23, 18, 23, 18) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 15) };
        heading.Children.Add(new TextBlock
        {
            Text = titles ? "Repair Previously Saved Championship Names" :
                "Repair Previously Saved Employment Names",
            FontSize = 21, FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White
        });
        heading.Children.Add(new TextBlock
        {
            Text = $"{scope} • {plan.ScannedCount:N0} existing record(s) scanned • " +
                $"{plan.Changes.Count:N0} proposed rename(s) • " +
                $"{plan.Warnings.Count:N0} record(s) skipped or flagged.",
            Foreground = ExtraColor("#B6D2ED"), Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        heading.Children.Add(new TextBlock
        {
            Text = titles
                ? "Only tblBeltPreHistory.Recordname changes. All existing title IDs, " +
                  "reign order, holders, capture/loss dates and defense counts remain unchanged."
                : "Only tblCV.RecordName changes. Worker/company UIDs, dates and " +
                  "employment arrangement flags remain unchanged.",
            Foreground = ExtraColor("#9EB9D6"), Margin = new Thickness(0, 7, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        Grid.SetRow(heading, 0);
        layout.Children.Add(heading);

        var warning = new TextBlock
        {
            Text = plan.Warnings.Count == 0 ? "" :
                "Skipped records (unchanged): " +
                string.Join("  •  ", plan.Warnings.Take(4)) +
                (plan.Warnings.Count > 4
                    ? $"  •  ... and {plan.Warnings.Count - 4} more." : ""),
            Foreground = ExtraColor("#F3C879"), TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, plan.Warnings.Count == 0 ? 0 : 14),
            Visibility = plan.Warnings.Count == 0
                ? Visibility.Collapsed : Visibility.Visible,
            MaxHeight = 100
        };
        Grid.SetRow(warning, 1);
        layout.Children.Add(warning);

        var grid = ExtraGrid();
        grid.MinHeight = 210;
        TextColumn(grid, "Existing Record Name", nameof(RecordNameRepair.OldName),
            new DataGridLength(1.35, DataGridLengthUnitType.Star));
        TextColumn(grid, "Proposed Record Name", nameof(RecordNameRepair.NewName),
            new DataGridLength(1.35, DataGridLengthUnitType.Star));
        TextColumn(grid, "Context", nameof(RecordNameRepair.Context),
            new DataGridLength(1, DataGridLengthUnitType.Star));
        grid.ItemsSource = plan.Changes;
        Grid.SetRow(grid, 2);
        layout.Children.Add(grid);

        var footer = new DockPanel { Margin = new Thickness(0, 18, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button
        {
            Content = "Close Without Changes",
            Style = (Style)FindResource("ActionButton"),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 10, 0)
        };
        cancel.Click += (_, _) => window.Close();
        buttons.Children.Add(cancel);
        var apply = new Button
        {
            Content = $"Apply {plan.Changes.Count:N0} Record Name Change(s)",
            Style = (Style)FindResource("BlueButton"),
            Padding = new Thickness(15, 10, 15, 10),
            IsEnabled = plan.Changes.Count > 0
        };
        apply.Click += (_, _) =>
        {
            if (_session == null) return;
            var confirm = MessageBox.Show(window,
                $"Rename {plan.Changes.Count:N0} previously saved " +
                (titles ? "championship-history" : "employment-history") +
                $" record(s) in your ACTIVE scenario MDB?\n\n" +
                "Only the RecordName column will be updated. " +
                "All other data is preserved.\n\n" +
                (plan.Warnings.Count > 0
                    ? $"{plan.Warnings.Count} unsupported record(s) will stay unchanged.\n\n"
                    : "") +
                "This action is not automatically reversible. " +
                "You may choose to create a backup before proceeding.",
                "Confirm Existing Record Name Repair",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;
            if (!OfferOptionalBackupBeforeWrite(
                    "repairing existing record names", window)) return;

            try
            {
                int changed = _session.ApplyRecordNameRepairs(plan);
                _lastSaved = DateTime.Now;
                window.DialogResult = true;
                window.Close();
                if (titles)
                {
                    ClearGeneratedLineage();
                    ResetAdvancedDraft();
                    if (_selectedExisting != null && _activeModule == "Titles" &&
                        TitleLineagePanel.Visibility == Visibility.Visible)
                        LoadTitleLineage();
                }
                else if (_worker != null && _currentTab == "Employment")
                {
                    RefreshWorkerExtraTab("Employment");
                }
                SetStatus($"Repaired {changed:N0} existing " +
                    (titles ? "championship" : "employment") +
                    " RecordName values. No historical facts were changed.");
                RefreshDirty();
                MessageBox.Show(this, $"{changed:N0} existing record name(s) updated.\n\n" +
                    "Run TEW IX Data Check on a copied scenario to confirm all renamed " +
                    "identifiers remain accepted by the game.",
                    "Record Names Updated", MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex) { Error(ex); }
        };
        buttons.Children.Add(apply);
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);
        var help = new TextBlock
        {
            Text = "Preview only until Apply is confirmed. " +
                   "Existing record IDs and all dates are preserved.",
            Foreground = ExtraColor("#9AB9D9"),
            MaxWidth = 370, VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        footer.Children.Add(help);
        Grid.SetRow(footer, 3);
        layout.Children.Add(footer);
        window.Content = layout;
        window.ShowDialog();
    }
}
