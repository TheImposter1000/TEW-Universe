using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using TEWUniverse.Studio;

namespace TEWUniverse.Desktop;

/// <summary>
/// Worker-specific editors for attribute rows and historical company periods.
/// The normal worker fields continue to use the main Save Changes action;
/// these linked rows use explicit Add / Save Selected / Remove actions.
/// </summary>
public partial class MainWindow
{
    private DataGrid _attributesGrid = null!;
    private ComboBox _attributeName = null!;
    private CheckBox _attributeHidden = null!;
    private CheckBox _attributePermanent = null!;
    private Button _attributeSave = null!;
    private Button _attributeDelete = null!;
    private TextBlock _attributeSummary = null!;
    private TextBlock _attributeHelp = null!;
    private TextBlock _attributeNameCaption = null!;
    private bool _attributeNumericCodes;

    private DataGrid _employmentGrid = null!;
    private ComboBox _employmentCompany = null!;
    private CheckBox _employmentUnlisted = null!;
    private TextBox _employmentRecordName = null!;
    private TextBlock _employmentNameHint = null!;
    private bool _employmentNameAutomatic = true;
    private bool _settingGeneratedEmploymentName;
    private TextBox _employmentInitials = null!;
    private TextBlock _employmentCompanyMatch = null!;
    private bool _syncingEmploymentCompany;
    private TextBox _employmentFrom = null!;
    private TextBox _employmentUntil = null!;
    private CheckBox _employmentLoan = null!;
    private CheckBox _employmentTrade = null!;
    private CheckBox _employmentTour = null!;
    private CheckBox _employmentDevelopmental = null!;
    private Button _employmentSave = null!;
    private Button _employmentDelete = null!;
    private TextBlock _employmentSummary = null!;
    private List<EmploymentCompanyChoice> _employmentCompanies = new();
    private bool _settingWorkerExtraForm;

    private sealed record EmploymentDisplay(WorkerEmploymentEntry Entry, string Company, string WorkerName = "")
    {
        public string FromText => StudioDateFormat.Format(Entry.FromDate);
        public string UntilText => StudioDateFormat.IsOngoingEndDate(Entry.UntilDate)
            ? "Ongoing" : StudioDateFormat.Format(Entry.UntilDate);
        public string Type => string.Join(", ", new[] {
            Entry.Loan ? "Loan" : "", Entry.Trade ? "Trade" : "",
            Entry.Tour ? "Tour" : "", Entry.Developmental ? "Developmental" : ""
        }.Where(x => x.Length > 0)) is string result && result.Length > 0
            ? result : "Regular";
    }

    private static SolidColorBrush ExtraColor(string code) =>
        new((Color)ColorConverter.ConvertFromString(code)!);

    private TextBlock ExtraLabel(string text, bool title = false) => new()
    {
        Text = text, Foreground = ExtraColor(title ? "#EDF7FF" : "#B9D4ED"),
        FontSize = title ? 18 : 12,
        FontWeight = title ? FontWeights.Bold : FontWeights.Normal,
        Margin = new Thickness(0, 0, 0, title ? 12 : 6),
        TextWrapping = TextWrapping.Wrap
    };

    private Button ExtraButton(string text, bool primary, RoutedEventHandler action) {
        var button = new Button {
            Content = text, Style = (Style)FindResource(primary ? "BlueButton" : "ActionButton"),
            Padding = new Thickness(13, 10, 13, 10), Margin = new Thickness(0, 0, 0, 8)
        };
        button.Click += action;
        return button;
    }

    private TextBox ExtraTextBox() => new() {
        Style = (Style)FindResource("FormTextBox"),
        Margin = new Thickness(0, 0, 0, 11),
        MinHeight = 35
    };

    private ComboBox ExtraPicker(bool editable = false) => new() {
        Style = (Style)FindResource("DarkComboBox"),
        IsEditable = editable, IsTextSearchEnabled = true,
        MaxDropDownHeight = 240, MinHeight = 36,
        Margin = new Thickness(0, 0, 0, 11)
    };

    private DataGrid ExtraGrid()
    {
        // Explicit header, row and cell styles prevent the WPF default
        // white column headers / selected cells from showing through.
        var headers = new Style(typeof(DataGridColumnHeader));
        headers.Setters.Add(new Setter(Control.BackgroundProperty, ExtraColor("#1A3450")));
        headers.Setters.Add(new Setter(Control.ForegroundProperty, ExtraColor("#E8F4FF")));
        headers.Setters.Add(new Setter(Control.BorderBrushProperty, ExtraColor("#345370")));
        headers.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
        headers.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 8, 8, 8)));
        headers.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));

        var rows = new Style(typeof(DataGridRow));
        rows.Setters.Add(new Setter(Control.ForegroundProperty, ExtraColor("#E4F1FF")));
        var selected = new Trigger { Property = DataGridRow.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, ExtraColor("#185AA0")));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, ExtraColor("#FFFFFF")));
        rows.Triggers.Add(selected);

        var cells = new Style(typeof(DataGridCell));
        cells.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        cells.Setters.Add(new Setter(Control.ForegroundProperty, ExtraColor("#E4F1FF")));
        cells.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        cells.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 5, 8, 5)));
        var selectedCell = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
        selectedCell.Setters.Add(new Setter(Control.BackgroundProperty, ExtraColor("#185AA0")));
        selectedCell.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        cells.Triggers.Add(selectedCell);

        return new DataGrid {
            AutoGenerateColumns = false, IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single,
            CanUserAddRows = false, CanUserDeleteRows = false,
            Background = ExtraColor("#102238"),
            Foreground = ExtraColor("#ECF4FF"),
            RowBackground = ExtraColor("#12253A"),
            AlternatingRowBackground = ExtraColor("#1B324C"),
            BorderBrush = ExtraColor("#345370"),
            BorderThickness = new Thickness(1),
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HorizontalGridLinesBrush = ExtraColor("#314960"),
            HeadersVisibility = DataGridHeadersVisibility.Column,
            ColumnHeaderHeight = 36, RowHeight = 36,
            ColumnHeaderStyle = headers, RowStyle = rows, CellStyle = cells
        };
    }

    private static void TextColumn(DataGrid grid, string title, string binding,
        DataGridLength width)
    {
        var textStyle = new Style(typeof(TextBlock));
        textStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty,
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E6F3FF")!)));
        textStyle.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
        textStyle.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        grid.Columns.Add(new DataGridTextColumn {
            Header = title, Binding = new Binding(binding), Width = width,
            ElementStyle = textStyle
        });
    }

    private static void AddExtraField(StackPanel panel, UIElement caption, UIElement field)
    {
        panel.Children.Add(caption);
        panel.Children.Add(field);
    }

    private static Grid TwoColumnExtraGrid(Grid host)
    {
        host.ColumnDefinitions.Add(new ColumnDefinition {
            Width = new GridLength(1.08, GridUnitType.Star)
        });
        host.ColumnDefinitions.Add(new ColumnDefinition {
            Width = new GridLength(0.92, GridUnitType.Star)
        });
        return host;
    }

    private void BuildExtraSection(Grid host, string title,
        DataGrid grid, StackPanel editor, out TextBlock summary)
    {
        TwoColumnExtraGrid(host);
        var left = new DockPanel { Margin = new Thickness(0, 0, 12, 0) };
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        heading.Children.Add(ExtraLabel(title, true));
        summary = new TextBlock {
            Text = "Select an existing worker to load records.",
            Foreground = ExtraColor("#A6BDD9"), TextWrapping = TextWrapping.Wrap
        };
        heading.Children.Add(summary);
        DockPanel.SetDock(heading, Dock.Top);
        left.Children.Add(heading);
        left.Children.Add(grid);
        Grid.SetColumn(left, 0);
        host.Children.Add(left);

        var scroll = new ScrollViewer {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        scroll.Content = editor;
        Grid.SetColumn(scroll, 1);
        host.Children.Add(scroll);
    }

    private void InitializeWorkerExtrasUI()
    {
        _attributesGrid = ExtraGrid();
        TextColumn(_attributesGrid, "Attribute (stored code)", "DisplayAttribute",
            new DataGridLength(1, DataGridLengthUnitType.Star));
        _attributesGrid.Columns.Add(new DataGridCheckBoxColumn {
            Header = "Hidden", Binding = new Binding("Hidden"), Width = 74
        });
        _attributesGrid.Columns.Add(new DataGridCheckBoxColumn {
            Header = "Permanent", Binding = new Binding("Permanent"), Width = 88
        });
        _attributesGrid.SelectionChanged += AttributeSelectionChanged;
        var attrEditor = new StackPanel { Margin = new Thickness(1, 0, 8, 6) };
        attrEditor.Children.Add(ExtraLabel("Attribute details", true));
        _attributeName = ExtraPicker(editable: true);
        _attributeNameCaption = ExtraLabel("Attribute name");
        AddExtraField(attrEditor, _attributeNameCaption, _attributeName);
        _attributeHidden = new CheckBox {
            Content = "Hidden (still takes effect in TEW IX)",
            Foreground = ExtraColor("#E8F5FF"), Margin = new Thickness(0, 3, 0, 11)
        };
        _attributePermanent = new CheckBox {
            Content = "Permanent (cannot be lost during gameplay)",
            Foreground = ExtraColor("#E8F5FF"), Margin = new Thickness(0, 0, 0, 12)
        };
        attrEditor.Children.Add(_attributeHidden);
        attrEditor.Children.Add(_attributePermanent);
        _attributeHelp = new TextBlock {
            Text = "Loading the tblAttribute storage type...",
            Foreground = ExtraColor("#A6BDD9"), TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        };
        attrEditor.Children.Add(_attributeHelp);
        attrEditor.Children.Add(ExtraButton("+ Add Attribute", true, AddAttribute_Click));
        _attributeSave = ExtraButton("Save Selected Attribute", false, SaveAttribute_Click);
        _attributeDelete = ExtraButton("Remove Selected Attribute", false, RemoveAttribute_Click);
        _attributeSave.IsEnabled = _attributeDelete.IsEnabled = false;
        attrEditor.Children.Add(_attributeSave);
        attrEditor.Children.Add(_attributeDelete);
        BuildExtraSection(AttributesHost, "Worker Attributes", _attributesGrid,
            attrEditor, out _attributeSummary);

        _employmentGrid = ExtraGrid();
        TextColumn(_employmentGrid, "Record name", "Entry.RecordName",
            new DataGridLength(1.3, DataGridLengthUnitType.Star));
        TextColumn(_employmentGrid, "Company", "Company", new DataGridLength(1.15, DataGridLengthUnitType.Star));
        TextColumn(_employmentGrid, "From", "FromText", new DataGridLength(103));
        TextColumn(_employmentGrid, "Until", "UntilText", new DataGridLength(103));
        TextColumn(_employmentGrid, "Arrangement", "Type", new DataGridLength(120));
        _employmentGrid.SelectionChanged += EmploymentSelectionChanged;
        var employmentEditor = new StackPanel { Margin = new Thickness(1, 0, 8, 6) };
        employmentEditor.Children.Add(ExtraLabel("Employment period", true));
        _employmentRecordName = ExtraTextBox();
        _employmentRecordName.TextChanged += EmploymentRecordNameChanged;
        var recordHeading = new DockPanel();
        var generateName = ExtraButton("Generate name", false, GenerateEmploymentName_Click);
        generateName.Padding = new Thickness(8, 5, 8, 5);
        generateName.Margin = new Thickness(0, 0, 0, 5);
        DockPanel.SetDock(generateName, Dock.Right);
        recordHeading.Children.Add(generateName);
        recordHeading.Children.Add(ExtraLabel("Record name (editable)"));
        AddExtraField(employmentEditor, recordHeading, _employmentRecordName);
        _employmentNameHint = new TextBlock {
            Text = "Record names use the selected company's full name. " +
                   "Ongoing: Company Name (Year - ). Historical: Company Name (Year - Year). " +
                   "Unlisted companies use the historical initials.",
            Foreground = ExtraColor("#93B4D5"), FontSize = 11,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, -5, 0, 11)
        };
        employmentEditor.Children.Add(_employmentNameHint);
        _employmentCompany = ExtraPicker(editable: true);
        _employmentCompany.SelectionChanged += EmploymentCompanyChanged;
        _employmentCompany.DisplayMemberPath = nameof(EmploymentCompanyChoice.Name);
        TextSearch.SetTextPath(_employmentCompany, nameof(EmploymentCompanyChoice.Name));
        AddExtraField(employmentEditor, ExtraLabel("Company (optional for unlisted promotions)"), _employmentCompany);
        _employmentUnlisted = new CheckBox {
            Content = "Unlisted historical company (FedUID 0)",
            Foreground = ExtraColor("#E8F5FF"),
            Margin = new Thickness(0, 0, 0, 11),
            ToolTip = "The company is not in tblFed. Only its initials and dates are needed."
        };
        _employmentUnlisted.Checked += EmploymentUnlistedChanged;
        _employmentUnlisted.Unchecked += EmploymentUnlistedChanged;
        employmentEditor.Children.Add(_employmentUnlisted);
        _employmentInitials = ExtraTextBox();
        _employmentInitials.TextChanged += EmploymentInitialsChanged;
        AddExtraField(employmentEditor, ExtraLabel("Historical company initials"), _employmentInitials);
        _employmentCompanyMatch = new TextBlock {
            Text = "Select a company, or enter initials and dates without one to store FedUID 0.",
            Foreground = ExtraColor("#93B4D5"), FontSize = 11,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, -6, 0, 11)
        };
        employmentEditor.Children.Add(_employmentCompanyMatch);
        _employmentFrom = ExtraTextBox();
        _employmentFrom.TextChanged += EmploymentNameSourceChanged;
        AddExtraField(employmentEditor, ExtraLabel("From date (e.g. 5/12/1998 or May 12th 1998)"), _employmentFrom);
        _employmentUntil = ExtraTextBox();
        _employmentUntil.TextChanged += EmploymentNameSourceChanged;
        AddExtraField(employmentEditor,
            ExtraLabel("Until date (or 01/01/1666 if still active)"), _employmentUntil);
        var ongoingButton = ExtraButton("Still active (01/01/1666)", false, (_, _) => {
            _employmentUntil.Text = StudioDateFormat.Format(StudioDateFormat.OngoingEndDate);
        });
        ongoingButton.ToolTip = "Set the TEW IX ongoing employment code in Until. This is not a real end date.";
        employmentEditor.Children.Add(ongoingButton);
        var flags = new WrapPanel { Margin = new Thickness(0, 0, 0, 13) };
        _employmentLoan = ExtraFlag("Loan", flags);
        _employmentTrade = ExtraFlag("Trade", flags);
        _employmentTour = ExtraFlag("Tour", flags);
        _employmentDevelopmental = ExtraFlag("Developmental", flags);
        employmentEditor.Children.Add(flags);
        employmentEditor.Children.Add(new TextBlock {
            Text = "These are historical tblCV periods, not active contracts. " +
                "A new period does not sign the worker to a company.",
            Foreground = ExtraColor("#A6BDD9"), TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 15)
        });
        employmentEditor.Children.Add(ExtraLabel("Existing record-name repair", true));
        employmentEditor.Children.Add(new TextBlock {
            Text = "Preview old → new RecordName values before changing the MDB. " +
                "Only record names are updated; employment periods, companies and arrangement flags stay unchanged.",
            Foreground = ExtraColor("#93B4D5"), FontSize = 11,
            Margin = new Thickness(0, 0, 0, 12), TextWrapping = TextWrapping.Wrap
        });
        employmentEditor.Children.Add(ExtraButton(
            "Review / Repair This Worker's Existing Names", false,
            ReviewSelectedEmploymentRecordNames_Click));
        employmentEditor.Children.Add(ExtraButton(
            "Review / Repair All Employment Record Names", false,
            ReviewAllEmploymentRecordNames_Click));
        employmentEditor.Children.Add(ExtraButton("+ Add Employment Period", true, AddEmployment_Click));
        _employmentSave = ExtraButton("Save Selected Period", false, SaveEmployment_Click);
        _employmentDelete = ExtraButton("Remove Selected Period", false, RemoveEmployment_Click);
        _employmentSave.IsEnabled = _employmentDelete.IsEnabled = false;
        employmentEditor.Children.Add(_employmentSave);
        employmentEditor.Children.Add(_employmentDelete);
        BuildExtraSection(EmploymentHost, "Employment History", _employmentGrid,
            employmentEditor, out _employmentSummary);
        InitializeWorkerContractsUI();
    }

    private CheckBox ExtraFlag(string name, Panel panel)
    {
        var flag = new CheckBox {
            Content = name, Foreground = ExtraColor("#E8F5FF"),
            Margin = new Thickness(0, 0, 15, 8)
        };
        panel.Children.Add(flag);
        return flag;
    }

    private void EmploymentRecordNameChanged(object sender, TextChangedEventArgs e)
    {
        if (!_settingWorkerExtraForm && !_settingGeneratedEmploymentName)
            _employmentNameAutomatic = false;
    }

    private void EmploymentNameSourceChanged(object sender, TextChangedEventArgs e) =>
        RefreshAutomaticEmploymentName();

    private string EmploymentAutoNameCompany() =>
        _employmentUnlisted.IsChecked != true &&
        _employmentCompany.SelectedItem is EmploymentCompanyChoice company &&
        !string.IsNullOrWhiteSpace(company.Name)
            ? company.Name.Trim() : _employmentInitials.Text.Trim();

    private void RefreshAutomaticEmploymentName()
    {
        if (_settingWorkerExtraForm || !_employmentNameAutomatic ||
            _employmentRecordName == null || _employmentFrom == null ||
            _employmentUntil == null || _employmentInitials == null) return;

        var result = EmploymentNameGeneration.Validate(
            EmploymentAutoNameCompany(), _employmentFrom.Text, _employmentUntil.Text);
        if (!result.IsValid) return;

        try
        {
            _settingGeneratedEmploymentName = true;
            _employmentRecordName.Text = result.RecordName!;
            _employmentNameHint.Text = "Generated from company name and employment dates: " +
                result.RecordName;
            _employmentNameHint.Foreground = ExtraColor("#93B4D5");
        }
        finally { _settingGeneratedEmploymentName = false; }
    }

    private void GenerateEmploymentName_Click(object sender, RoutedEventArgs e)
    {
        if (_employmentInitials == null || _employmentFrom == null ||
            _employmentUntil == null) return;

        // Company names come directly from tblFed; historical initials
        // remain required and are the fallback for unlisted companies.
        if (string.IsNullOrWhiteSpace(_employmentInitials.Text) &&
            _employmentCompany.SelectedItem is EmploymentCompanyChoice company &&
            !string.IsNullOrWhiteSpace(company.Initials))
            _employmentInitials.Text = company.Initials;

        var result = EmploymentNameGeneration.Validate(
            _employmentInitials.Text, _employmentFrom.Text, _employmentUntil.Text);
        if (!result.IsValid)
        {
            // No generic modal: identify which input needs attention while
            // leaving the user's partially completed form untouched.
            _employmentNameHint.Text = result.Error!;
            _employmentNameHint.Foreground = ExtraColor("#E5BF78");
            switch (result.Field)
            {
                case EmploymentNameField.Initials: _employmentInitials.Focus(); break;
                case EmploymentNameField.From: _employmentFrom.Focus(); break;
                case EmploymentNameField.Until: _employmentUntil.Focus(); break;
            }
            return;
        }
        _employmentNameAutomatic = true;
        RefreshAutomaticEmploymentName();
        RefreshDirty();
    }

    private void EmploymentUnlistedChanged(object sender, RoutedEventArgs e)
    {
        if (_settingWorkerExtraForm || _employmentCompany == null ||
            _employmentInitials == null) return;
        bool unlisted = _employmentUnlisted.IsChecked == true;
        _syncingEmploymentCompany = true;
        try
        {
            if (unlisted) _employmentCompany.SelectedItem = null;
            _employmentCompany.IsEnabled = !unlisted;
        }
        finally { _syncingEmploymentCompany = false; }
        _employmentCompanyMatch.Text = unlisted
            ? "Unlisted company (FedUID 0). The initials and dates are saved " +
              "without a tblFed company record."
            : "Select a company, or enter unmatched initials for an unlisted company.";
        RefreshAutomaticEmploymentName();
        RefreshDirty();
    }

    private void EmploymentCompanyChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settingWorkerExtraForm || _syncingEmploymentCompany ||
            _employmentInitials == null ||
            _employmentUnlisted.IsChecked == true) return;
        if (_employmentCompany.SelectedItem is not EmploymentCompanyChoice company)
            return;

        // A deliberate company selection wins even if another historical
        // abbreviation was previously entered. Never invent initials.
        _syncingEmploymentCompany = true;
        try { _employmentInitials.Text = company.Initials; }
        finally { _syncingEmploymentCompany = false; }
        _employmentCompanyMatch.Text = string.IsNullOrWhiteSpace(company.Initials)
            ? "This company has no initials stored in tblFed. Enter valid initials before saving."
            : company.Name + " is selected. Company initials: " + company.Initials + ".";
        RefreshAutomaticEmploymentName();
    }

    private void EmploymentInitialsChanged(object sender, TextChangedEventArgs e)
    {
        if (_settingWorkerExtraForm || _syncingEmploymentCompany) return;
        string typed = _employmentInitials.Text.Trim();
        if (_employmentUnlisted.IsChecked == true)
        {
            _employmentCompanyMatch.Text =
                "Unlisted company (FedUID 0). These initials do not require a tblFed record.";
            RefreshAutomaticEmploymentName();
            RefreshDirty();
            return;
        }
        var unique = EmploymentCompanyLookup.FindUniqueByInitials(
            typed, _employmentCompanies);
        int matches = EmploymentCompanyLookup.CountMatches(
            typed, _employmentCompanies);

        _syncingEmploymentCompany = true;
        try
        {
            if (unique != null)
            {
                if ((_employmentCompany.SelectedItem as EmploymentCompanyChoice)?.Id != unique.Id)
                    _employmentCompany.SelectedItem = unique;
            }
            else if (_employmentCompany.SelectedItem is EmploymentCompanyChoice selected &&
                !(matches > 1 &&
                  string.Equals(selected.Initials.Trim(), typed,
                      StringComparison.OrdinalIgnoreCase)))
            {
                // A partial, unknown, or ambiguous abbreviation cannot leave
                // a different company silently selected for the MDB write.
                _employmentCompany.SelectedItem = null;
            }
        }
        finally { _syncingEmploymentCompany = false; }

        _employmentCompanyMatch.Text = typed.Length == 0
            ? "Enter company initials to find the matching company."
            : unique != null ? "Matched company: " + unique.Name + "."
            : matches > 1
                ? "Multiple companies use these initials. Select the intended company by name."
                : "No matching company. This will save as an unlisted company (FedUID 0).";
        RefreshAutomaticEmploymentName();
        RefreshDirty();
    }

    private void ResetWorkerExtras()
    {
        if (_attributesGrid == null) return;
        _settingWorkerExtraForm = true;
        try
        {
            _attributesGrid.ItemsSource = null;
            _employmentGrid.ItemsSource = null;
            ResetWorkerContractsUI();
            _attributeName.ItemsSource = null;
            _attributeName.Text = "";
            _attributeHidden.IsChecked = _attributePermanent.IsChecked = false;
            _employmentCompanies.Clear();
            _employmentCompany.ItemsSource = null;
            _employmentUnlisted.IsChecked = false;
            _employmentCompany.IsEnabled = true;
            _employmentNameAutomatic = true;
            _employmentRecordName.Text = "";
            _employmentNameHint.Text = "Ongoing: Company Name (Year - ). " +
                "Historical: Company Name (Year - Year).";
            _employmentNameHint.Foreground = ExtraColor("#93B4D5");
            _employmentInitials.Text = _employmentFrom.Text = _employmentUntil.Text = "";
            _employmentCompanyMatch.Text = "Select a company or use initials only for FedUID 0.";
            _employmentLoan.IsChecked = _employmentTrade.IsChecked =
                _employmentTour.IsChecked = _employmentDevelopmental.IsChecked = false;
            _attributeSave.IsEnabled = _attributeDelete.IsEnabled = false;
            _employmentSave.IsEnabled = _employmentDelete.IsEnabled = false;
            _attributeSummary.Text = "Select a worker to load attributes.";
            _employmentSummary.Text = "Select a worker to load employment history.";
        }
        finally { _settingWorkerExtraForm = false; }
    }

    private void RefreshWorkerExtraTab(string tab, int? attributeUid = null,
        string? employmentKey = null)
    {
        if (_session == null) return;
        if (tab == "Employment" && _activeModule == "Employment History")
        {
            ReloadEmploymentHistory(EmploymentWorker?.Id, employmentKey);
            return;
        }
        if (_worker == null) return;
        try
        {
            if (tab == "Contracts")
            {
                RefreshWorkerContracts();
                return;
            }
            if (tab == "Attributes")
            {
                var records = _session.ReadWorkerAttributes(_worker.Id);
                bool numeric = _session.WorkerAttributesUseNumericCodes();
                var known = _session.ListKnownWorkerAttributes();
                _settingWorkerExtraForm = true;
                try
                {
                    _attributeNumericCodes = numeric;
                    _attributeName.IsEditable = !numeric;
                    _attributeNameCaption.Text = numeric
                        ? "Attribute (verified name + stored numeric code)"
                        : "Attribute name";
                    _attributeHelp.Text = numeric
                        ? "52 known, assignable attributes across Personality, Alignment, " +
                          "Gimmick and Other Performance. Each shows its stored code. " +
                          "Five Personality codes are reserved as Unassigned and cannot be added. " +
                          "Other codes remain unmapped, readable, and preserved if unchanged."
                        : "This MDB stores attributes as text names. Choose a known " +
                          "attribute or type its exact game name.";
                    _attributeName.ItemsSource = numeric
                        ? (System.Collections.IEnumerable)WorkerAttributeValue.Options(known)
                        : known;
                    _attributesGrid.ItemsSource = records;
                    _attributesGrid.SelectedItem = attributeUid.HasValue
                        ? records.FirstOrDefault(r => r.Uid == attributeUid.Value) : null;
                    _attributeSummary.Text = $"{records.Count} attribute(s) linked to {_worker.Name}. " +
                        (numeric ? "Verified codes display their attribute names; " +
                                   "unknown codes remain unmapped. " :
                                   "Attribute values are stored as text. ") +
                        "Hidden and Permanent reflect the actual MDB flags.";
                }
                finally { _settingWorkerExtraForm = false; }
                AttributeSelectionChanged(_attributesGrid, null!);
            }
            else if (tab == "Employment")
            {
                var records = _session.ReadWorkerEmployment(_worker.Id);
                _employmentCompanies = _session.ReadEmploymentCompanies();
                var names = _employmentCompanies.ToDictionary(c => c.Id, c => c.Name);
                var rows = records.Select(r => new EmploymentDisplay(r,
                    names.TryGetValue(r.FedUid, out var company) ? company :
                    $"{r.Initials} (company UID {r.FedUid})")).ToList();
                _settingWorkerExtraForm = true;
                try
                {
                    _employmentCompany.ItemsSource = _employmentCompanies;
                    _employmentGrid.ItemsSource = rows;
                    _employmentGrid.SelectedItem = employmentKey == null ? null :
                        rows.FirstOrDefault(r => r.Entry.RecordName == employmentKey);
                    _employmentSummary.Text = $"{rows.Count} historical period(s) linked to " +
                        $"{_worker.Name}. This does not show current tblContract deals.";
                }
                finally { _settingWorkerExtraForm = false; }
                EmploymentSelectionChanged(_employmentGrid, null!);
            }
        }
        catch (Exception ex)
        {
            if (tab == "Attributes")
            {
                _attributesGrid.ItemsSource = null;
                _attributeSummary.Text = "Cannot read worker attributes: " + ex.Message;
            }
            else
            {
                _employmentGrid.ItemsSource = null;
                _employmentSummary.Text = "Cannot read employment history: " + ex.Message;
            }
        }
    }

    private void AttributeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settingWorkerExtraForm) return;
        _settingWorkerExtraForm = true;
        try
        {
            var item = _attributesGrid.SelectedItem as WorkerAttributeEntry;
            _attributeSave.IsEnabled = _attributeDelete.IsEnabled = item != null;
            if (_attributeNumericCodes)
                _attributeName.SelectedItem = item == null ? null :
                    _attributeName.Items.Cast<WorkerAttributeOption>()
                        .FirstOrDefault(x => x.StoredCode == item.Attribute);
            else
                _attributeName.Text = item?.Attribute ?? "";
            _attributeHidden.IsChecked = item?.Hidden ?? false;
            _attributePermanent.IsChecked = item?.Permanent ?? false;
        }
        finally { _settingWorkerExtraForm = false; }
    }

    private void EmploymentSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settingWorkerExtraForm) return;
        _settingWorkerExtraForm = true;
        try
        {
            var item = (_employmentGrid.SelectedItem as EmploymentDisplay)?.Entry;
            if (_activeModule == "Employment History" && _employmentWorkerPicker != null)
            {
                if (item != null)
                    _employmentWorkerPicker.SelectedItem =
                        _allWorkers.FirstOrDefault(x => x.Id == item.WorkerUid);
                _employmentWorkerPicker.IsEnabled = item == null;
            }
            _employmentSave.IsEnabled = _employmentDelete.IsEnabled = item != null;
            _employmentNameAutomatic = item == null;
            _employmentRecordName.Text = item?.RecordName ?? "";
            _employmentNameHint.Text = item == null
                ? "Ongoing: Company Name (Year - ). Historical: Company Name (Year - Year)."
                : "Existing record name is preserved until you choose Generate name or edit it.";
            _employmentNameHint.Foreground = ExtraColor("#93B4D5");
            _employmentUnlisted.IsChecked = item?.FedUid == 0;
            _employmentCompany.IsEnabled = item?.FedUid != 0;
            _employmentCompany.SelectedItem = item == null || item.FedUid == 0 ? null :
                _employmentCompanies.FirstOrDefault(c => c.Id == item.FedUid);
            _employmentInitials.Text = item?.Initials ?? "";
            _employmentCompanyMatch.Text = item == null
                ? "Select a company or enter initials for FedUID 0."
                : item.FedUid == 0
                    ? "Unlisted company (FedUID 0). Historical initials: " + item.Initials + "."
                    : "Company: " + (_employmentCompany.SelectedItem as EmploymentCompanyChoice)?.Name +
                      ". Historical initials: " + item.Initials + ".";
            _employmentFrom.Text = item is null ? "" : StudioDateFormat.Format(item.FromDate);
            _employmentUntil.Text = item is null ? "" : StudioDateFormat.Format(item.UntilDate);
            _employmentLoan.IsChecked = item?.Loan ?? false;
            _employmentTrade.IsChecked = item?.Trade ?? false;
            _employmentTour.IsChecked = item?.Tour ?? false;
            _employmentDevelopmental.IsChecked = item?.Developmental ?? false;
        }
        finally { _settingWorkerExtraForm = false; }
    }

    private string CurrentAttributeValue() => _attributeNumericCodes
        ? (_attributeName.SelectedItem as WorkerAttributeOption)?.StoredCode ?? ""
        : _attributeName.Text.Trim();

    /// <summary>
    /// Unlike normal worker profile fields, these linked rows use dedicated
    /// Add/Save Selected actions. Protect typed but unapplied values when users
    /// switch workers or navigate away.
    /// </summary>
    private bool HasUnappliedWorkerExtraForm()
    {
        if (_settingWorkerExtraForm || _attributesGrid == null ||
            (_worker == null && _activeModule != "Employment History"))
            return false;
        if (_currentTab == "Contracts") return HasUnappliedContractEdits();
        if (_currentTab == "Accomplishments") return HasPendingAccomplishmentEdits();
        if (_currentTab == "Attributes")
        {
            var entry = _attributesGrid.SelectedItem as WorkerAttributeEntry;
            string name = CurrentAttributeValue();
            if (entry == null)
                return name.Length != 0 || _attributeHidden.IsChecked == true ||
                    _attributePermanent.IsChecked == true;
            return !name.Equals(entry.Attribute, StringComparison.Ordinal) ||
                (_attributeHidden.IsChecked == true) != entry.Hidden ||
                (_attributePermanent.IsChecked == true) != entry.Permanent;
        }
        if (_activeModule == "Employment History" ||
            (_activeModule == "Workers" && _currentTab == "Employment"))
        {
            var item = (_employmentGrid.SelectedItem as EmploymentDisplay)?.Entry;
            var company = _employmentCompany.SelectedItem as EmploymentCompanyChoice;
            if (item == null)
                return company != null || _employmentUnlisted.IsChecked == true ||
                    _employmentRecordName.Text.Trim().Length > 0 ||
                    _employmentInitials.Text.Trim().Length > 0 ||
                    _employmentFrom.Text.Trim().Length > 0 ||
                    _employmentUntil.Text.Trim().Length > 0 ||
                    _employmentLoan.IsChecked == true || _employmentTrade.IsChecked == true ||
                    _employmentTour.IsChecked == true ||
                    _employmentDevelopmental.IsChecked == true;
            return (_employmentUnlisted.IsChecked == true ? 0 : company?.Id ?? 0)
                != item.FedUid ||
                !_employmentRecordName.Text.Trim().Equals(item.RecordName, StringComparison.Ordinal) ||
                !_employmentInitials.Text.Trim().Equals(item.Initials, StringComparison.Ordinal) ||
                !_employmentFrom.Text.Trim().Equals(StudioDateFormat.Format(item.FromDate), StringComparison.Ordinal) ||
                !_employmentUntil.Text.Trim().Equals(StudioDateFormat.Format(item.UntilDate), StringComparison.Ordinal) ||
                (_employmentLoan.IsChecked == true) != item.Loan ||
                (_employmentTrade.IsChecked == true) != item.Trade ||
                (_employmentTour.IsChecked == true) != item.Tour ||
                (_employmentDevelopmental.IsChecked == true) != item.Developmental;
        }
        return false;
    }

    private bool ConfirmExtraWrite(string message, string title) =>
        MessageBox.Show(this, message + "\n\nThis changes the active MDB directly. " +
            "Creating a backup first is recommended but optional.",
            title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    private void SaveAttributeCore(bool createNew)
    {
        if (_session == null || _worker == null) return;
        var selected = _attributesGrid.SelectedItem as WorkerAttributeEntry;
        if (!createNew && selected == null)
        {
            MessageBox.Show(this, "Select an existing attribute to save.");
            return;
        }
        string name = CurrentAttributeValue();
        bool hidden = _attributeHidden.IsChecked == true;
        bool permanent = _attributePermanent.IsChecked == true;
        if (name.Length == 0)
        {
            MessageBox.Show(this, "Choose an attribute name first.");
            return;
        }

        // New codes must be positively verified. Existing unknown and
        // Unassigned codes can be retained while flags are edited.
        if (_attributeNumericCodes &&
            !WorkerAttributeValue.MaySaveCode(name, createNew ? null : selected?.Attribute))
        {
            MessageBox.Show(this,
                "This is an Unassigned/reserved or still-unmapped code. " +
                "It may be preserved on an existing record but cannot be newly assigned. " +
                "Choose one of the verified named attributes instead.",
                "Unverified Attribute Code", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        bool observed = _session.ListKnownWorkerAttributes().Any(x =>
            x.Equals(name, StringComparison.OrdinalIgnoreCase));
        string warning = _attributeNumericCodes || observed ? "" :
            "\n\nThis attribute name is not used elsewhere in this database. " +
            "Verify its exact spelling in the TEW IX editor.";
        string readable = _attributeNumericCodes
            ? WorkerAttributeValue.Label(name, typeof(int)) : name;
        if (!ConfirmExtraWrite(
                (createNew ? $"Add '{readable}' to {_worker.Name}?" :
                $"Update attribute UID {selected!.Uid} for {_worker.Name} to '{readable}'?") + warning,
                createNew ? "Add Worker Attribute" : "Save Worker Attribute")) return;

        try
        {
            int uid = _session.SaveWorkerAttribute(_worker.Id,
                createNew ? null : selected!.Uid, name, hidden, permanent);
            _lastSaved = DateTime.Now;
            RefreshWorkerExtraTab("Attributes", uid);
            SetStatus($"{(createNew ? "Added" : "Updated")} '{name}' for {_worker.Name}.");
            RefreshDirty();
        }
        catch (Exception ex) { Error(ex); }
    }

    private void AddAttribute_Click(object sender, RoutedEventArgs e) => SaveAttributeCore(true);
    private void SaveAttribute_Click(object sender, RoutedEventArgs e) => SaveAttributeCore(false);

    private void RemoveAttribute_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null || _worker == null ||
            _attributesGrid.SelectedItem is not WorkerAttributeEntry selected) return;
        if (!ConfirmExtraWrite(
            $"Remove '{selected.Attribute}' from {_worker.Name}? " +
            "This may change their in-game personality or other attribute effects.",
            "Remove Worker Attribute")) return;
        try
        {
            _session.DeleteWorkerAttribute(_worker.Id, selected.Uid);
            _lastSaved = DateTime.Now;
            RefreshWorkerExtraTab("Attributes");
            SetStatus($"Removed '{selected.Attribute}' from {_worker.Name}.");
            RefreshDirty();
        }
        catch (Exception ex) { Error(ex); }
    }

    private WorkerEmploymentDraft EmploymentDraft()
    {
        int companyId = _employmentUnlisted.IsChecked == true
            ? 0 : (_employmentCompany.SelectedItem as EmploymentCompanyChoice)?.Id ?? 0;
        // A blank selection is a legitimate unlisted company, with historical
        // initials as its identity. Selecting a company continues to use tblFed.
        // Ambiguous initials should not silently be stored as an unlisted company.
        if (companyId == 0 && _employmentUnlisted.IsChecked != true &&
            EmploymentCompanyLookup.CountMatches(
                _employmentInitials.Text, _employmentCompanies) > 0)
            throw new InvalidOperationException(
                "These initials match an existing company. Select the intended " +
                "company, or check Unlisted historical company to store FedUID 0.");
        if (!StudioDateFormat.TryParse(_employmentFrom.Text, out DateTime from) ||
            !StudioDateFormat.TryParse(_employmentUntil.Text, out DateTime until))
            throw new InvalidOperationException(
                "Enter valid employment From / Until dates, such as 5/12/1998 or May 12th 1998.");
        return new WorkerEmploymentDraft(
            companyId, _employmentInitials.Text.Trim(), from, until,
            _employmentLoan.IsChecked == true, _employmentTrade.IsChecked == true,
            _employmentTour.IsChecked == true, _employmentDevelopmental.IsChecked == true,
            _employmentNameAutomatic
                ? EmploymentNameGeneration.Validate(EmploymentAutoNameCompany(),
                    _employmentFrom.Text, _employmentUntil.Text).RecordName
                    ?? throw new InvalidOperationException(
                        "Cannot generate employment name: check the company and dates.")
                : _employmentRecordName.Text.Trim(),
            _employmentNameAutomatic);
    }

    private void SaveEmploymentCore(bool createNew)
    {
        if (_session == null) return;
        try
        {
            var selected = (_employmentGrid.SelectedItem as EmploymentDisplay)?.Entry;
            var owner = !createNew && selected != null
                ? _allWorkers.FirstOrDefault(w => w.Id == selected.WorkerUid)
                : EmploymentWorker;
            if (owner == null)
                throw new InvalidOperationException("Select a worker for this employment period.");
            if (!createNew && selected == null)
                throw new InvalidOperationException("Select an existing employment period first.");
            var draft = EmploymentDraft();
            if (!createNew && selected != null && selected.FedUid > 0 &&
                draft.FedUid == 0 && _employmentUnlisted.IsChecked != true)
                throw new InvalidOperationException(
                    "The original linked company is not selected. Choose the " +
                    "company or explicitly check Unlisted company to change it to UID 0.");
            string company = draft.FedUid == 0
                ? draft.Initials + " (unlisted company, UID 0)"
                : _employmentCompanies.First(x => x.Id == draft.FedUid).Name;
            if (!ConfirmExtraWrite(
                (createNew ? "Add a new" : "Update this") +
                $" historical employment period for {owner.Name} at {company}?\n" +
                $"Record Name: {(string.IsNullOrEmpty(draft.RequestedRecordName) ? "(generate)" : draft.RequestedRecordName)}\n" +
                $"{StudioDateFormat.Format(draft.FromDate)} through " +
                (StudioDateFormat.IsOngoingEndDate(draft.UntilDate)
                    ? "Ongoing (01/01/1666)" : StudioDateFormat.Format(draft.UntilDate)) +
                ".\n\n" +
                "This does not create or change an active worker contract.",
                createNew ? "Add Employment History" : "Save Employment History")) return;

            string key = _session.SaveWorkerEmployment(owner.Id,
                createNew ? null : selected, draft);
            _lastSaved = DateTime.Now;
            if (_activeModule == "Employment History")
                ReloadEmploymentHistory(owner.Id, key, draft.FedUid, draft.FromDate);
            else RefreshWorkerExtraTab("Employment", employmentKey: key);
            SetStatus($"{(createNew ? "Added" : "Updated")} employment period for {owner.Name} at {company}.");
            RefreshDirty();
        }
        catch (Exception ex) { Error(ex); }
    }

    private void AddEmployment_Click(object sender, RoutedEventArgs e) => SaveEmploymentCore(true);
    private void SaveEmployment_Click(object sender, RoutedEventArgs e) => SaveEmploymentCore(false);

    private void RemoveEmployment_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null ||
            (_employmentGrid.SelectedItem as EmploymentDisplay)?.Entry is not
                WorkerEmploymentEntry selected) return;
        var owner = _allWorkers.FirstOrDefault(w => w.Id == selected.WorkerUid);
        if (owner == null) return;
        if (!ConfirmExtraWrite(
            $"Remove {owner.Name}'s historical employment period " +
            $"{StudioDateFormat.Format(selected.FromDate)} through {StudioDateFormat.Format(selected.UntilDate)}?\n" +
            "This is permanent and cannot be restored without a backup.",
            "Remove Employment History")) return;
        try
        {
            _session.DeleteWorkerEmployment(owner.Id, selected);
            _lastSaved = DateTime.Now;
            if (_activeModule == "Employment History") ReloadEmploymentHistory();
            else RefreshWorkerExtraTab("Employment");
            SetStatus($"Removed an employment-history period for {owner.Name}.");
            RefreshDirty();
        }
        catch (Exception ex) { Error(ex); }
    }
}
