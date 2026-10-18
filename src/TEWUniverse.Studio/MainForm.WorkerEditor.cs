using System.Data;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace TEWUniverse.Studio;

/// <summary>
/// Phase 2 worker profile form. Widgets are generated from the actual MDB
/// schema and remain bound to the original column names. TEW-specific enum
/// meanings are not guessed, and the original record is never edited directly.
/// </summary>
internal sealed partial class MainForm
{
    private sealed record ProfileBinding(
        string ColumnName, Type ColumnType, string OriginalValue, Control Editor,
        Func<string> CurrentValue);

    private readonly FlowLayoutPanel _profileFieldsHost = new();
    private readonly List<ProfileBinding> _profileBindings = new();
    private readonly ToolTip _fieldHelp = new();
    private readonly List<Control> _profileGroups = new();
    private bool _loadingProfile;

    private Control BuildProfileForm()
    {
        _profileFieldsHost.Dock = DockStyle.Fill;
        _profileFieldsHost.FlowDirection = FlowDirection.TopDown;
        _profileFieldsHost.WrapContents = false;
        _profileFieldsHost.AutoScroll = true;
        _profileFieldsHost.BackColor = _surface;
        _profileFieldsHost.Padding = new Padding(6, 6, 6, 6);
        _profileFieldsHost.SizeChanged += (_, _) => SizeProfileGroups();
        return _profileFieldsHost;
    }

    private void ClearProfileForm()
    {
        _loadingProfile = true;
        try
        {
            _profileBindings.Clear();
            _profileGroups.Clear();
            _profileFieldsHost.Controls.Clear();
        }
        finally { _loadingProfile = false; }
    }

    private void LoadProfileForm(RecordSection record)
    {
        _loadingProfile = true;
        _profileFieldsHost.SuspendLayout();
        try
        {
            ClearProfileForm();
            if (!record.Exists)
            {
                var missing = new Label
                {
                    Text = "No existing profile record was found. The editor will not create a new worker.",
                    Height = 76, ForeColor = _muted, AutoSize = false
                };
                _profileFieldsHost.Controls.Add(missing);
                return;
            }

            var columns = record.Data.Columns.Cast<DataColumn>()
                .Where(column =>
                    !column.ColumnName.Equals(record.Key, StringComparison.OrdinalIgnoreCase) &&
                    DatabaseSession.IsEditable(column.DataType)).ToList();

            var identity = columns.Where(c => Category(c) == 0)
                .OrderBy(c => WorkerFieldCatalog.ProfileOrder(c.ColumnName)).ToList();
            var general = columns.Where(c => Category(c) == 1)
                .OrderBy(c => WorkerFieldCatalog.ProfileOrder(c.ColumnName)).ToList();
            var codes = columns.Where(c => Category(c) == 2)
                .OrderBy(c => WorkerFieldCatalog.ProfileOrder(c.ColumnName)).ToList();
            var remaining = columns.Where(c => Category(c) == 3)
                .OrderBy(c => c.Ordinal).ToList();

            AddProfileGroup("IDENTITY", "Worker identity and display names", identity, record.Data.Rows[0]);
            AddProfileGroup("STATUS & DATES", "Activity, history, and date fields", general, record.Data.Rows[0]);
            AddProfileGroup("GAME CODES", "Stored numeric TEW IX codes. Meanings are not verified.", codes, record.Data.Rows[0]);
            AddProfileGroup("ADDITIONAL DATABASE FIELDS",
                "Other existing worker fields. Hover over a label to see its MDB column.",
                remaining, record.Data.Rows[0]);

            SizeProfileGroups();
        }
        finally
        {
            _loadingProfile = false;
            _profileFieldsHost.ResumeLayout(true);
            RefreshShellStatus();
        }
    }

    // Fields with known plain-language semantics are promoted; unknown stored
    // enum codes are kept separate instead of being translated speculatively.
    private static int Category(DataColumn c)
    {
        var name = c.ColumnName;
        if (name.Equals("Name", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Shortname", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (WorkerFieldCatalog.IsUnknownCode(name, c.DataType)) return 2;
        if (name.Equals("Active", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("User", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Regen", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Birthday", StringComparison.OrdinalIgnoreCase) ||
            c.DataType == typeof(DateTime))
            return 1;
        return 3;
    }

    private void AddProfileGroup(string title, string description,
        IReadOnlyList<DataColumn> columns, DataRow row)
    {
        if (columns.Count == 0) return;

        int rowCount = (columns.Count + 1) / 2;
        var group = new Panel
        {
            BackColor = Color.FromArgb(23, 41, 58),
            Margin = new Padding(0, 0, 4, 13),
            Padding = new Padding(10, 8, 10, 10),
            Height = 70 + rowCount * 92
        };
        var heading = new Label
        {
            Dock = DockStyle.Top, Height = 28, ForeColor = _cyan,
            Text = title, Font = new Font("Segoe UI", 10, FontStyle.Bold)
        };
        var note = new Label
        {
            Dock = DockStyle.Top, Height = 32, ForeColor = _muted,
            Text = description, Font = new Font("Segoe UI", 8),
            AutoEllipsis = true
        };
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = rowCount,
            BackColor = Color.Transparent, Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (int i = 0; i < rowCount; i++)
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));

        for (int i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            var original = DatabaseSession.Display(row[column]);
            var (widget, binding) = MakeProfileWidget(column.ColumnName, column.DataType, original);
            body.Controls.Add(widget, i % 2, i / 2);
            _profileBindings.Add(binding);
        }

        group.Controls.Add(body);
        group.Controls.Add(note);
        group.Controls.Add(heading);
        _profileGroups.Add(group);
        _profileFieldsHost.Controls.Add(group);
    }

    private void SizeProfileGroups()
    {
        int width = Math.Max(310, _profileFieldsHost.ClientSize.Width -
            _profileFieldsHost.Padding.Horizontal - 23);
        foreach (var group in _profileGroups) group.Width = width;
    }

    private (Control, ProfileBinding) MakeProfileWidget(string field, Type type, string original)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill, Margin = new Padding(4, 3, 8, 5),
            Padding = new Padding(5, 3, 5, 4),
            BackColor = Color.FromArgb(29, 48, 65)
        };
        var name = WorkerFieldCatalog.FriendlyName(field, type);
        var caption = new Label
        {
            Dock = DockStyle.Top, Height = 30, Text = name,
            ForeColor = _light, Font = new Font("Segoe UI", 9, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true
        };

        Control editor;
        Func<string> read;

        if (type == typeof(bool))
        {
            var state = original.ToLowerInvariant() switch
            {
                "true" or "1" => CheckState.Checked,
                "false" or "0" => CheckState.Unchecked,
                _ => CheckState.Indeterminate
            };
            var input = new CheckBox
            {
                Dock = DockStyle.Fill, ThreeState = true,
                CheckState = state, ForeColor = _light,
                BackColor = _fieldBg,
                Font = new Font("Segoe UI", 10)
            };
            void UpdateLabel() => input.Text = input.CheckState switch
            {
                CheckState.Checked => "Yes",
                CheckState.Unchecked => "No",
                _ => "Not set"
            };
            UpdateLabel();
            input.CheckStateChanged += (_, _) =>
            {
                UpdateLabel();
                MarkProfileEdited();
            };
            read = () => input.CheckState switch
            {
                CheckState.Checked => state == CheckState.Checked ? original : "True",
                CheckState.Unchecked => state == CheckState.Unchecked ? original : "False",
                _ => ""
            };
            editor = input;
        }
        else if (type == typeof(DateTime) &&
                 (original.Length == 0 ||
                  (StudioDateFormat.TryParse(original, out var date) &&
                   date >= DateTimePicker.MinDateTime && date <= DateTimePicker.MaxDateTime)))
        {
            var originalDate = StudioDateFormat.TryParse(original, out var parsed)
                ? parsed : DateTime.Today;
            var input = new DateTimePicker
            {
                Dock = DockStyle.Fill,
                Format = DateTimePickerFormat.Custom,
                CustomFormat = StudioDateFormat.Pattern,
                ShowCheckBox = true,
                CalendarForeColor = Color.Black,
                Value = originalDate,
                Checked = original.Length > 0,
                Font = new Font("Segoe UI", 10)
            };
            input.ValueChanged += (_, _) => MarkProfileEdited();
            read = () =>
            {
                if (!input.Checked) return "";
                if (original.Length > 0 && input.Value.Date == originalDate.Date)
                    return original;
                return StudioDateFormat.Format(input.Value);
            };
            editor = input;
        }
        else if (WorkerFieldCatalog.IsNumeric(type) &&
                 !WorkerFieldCatalog.IsUnknownCode(field, type) &&
                 IsWholeNumber(type) &&
                 decimal.TryParse(original, NumberStyles.Integer,
                     CultureInfo.InvariantCulture, out var first))
        {
            // Numeric controls enforce storage type limits. A NULL or unverified
            // code instead receives a plain input, so we do not silently coerce
            // NULL to zero or attach an unverified meaning to a database code.
            var (min, max) = NumericBounds(type);
            if (first >= min && first <= max)
            {
                var input = new NumericUpDown
                {
                    Dock = DockStyle.Fill, Minimum = min, Maximum = max,
                    DecimalPlaces = 0, Value = first, ThousandsSeparator = false,
                    BackColor = _fieldBg, ForeColor = _light,
                    Font = new Font("Segoe UI", 10)
                };
                input.ValueChanged += (_, _) => MarkProfileEdited();
                read = () => input.Value == first ? original :
                    input.Value.ToString(CultureInfo.InvariantCulture);
                editor = input;
            }
            else
                (editor, read) = TextValueEditor(original);
        }
        else
            (editor, read) = TextValueEditor(original);

        var tooltip = WorkerFieldCatalog.FieldDescription(field, type);
        _fieldHelp.SetToolTip(caption, tooltip);
        _fieldHelp.SetToolTip(editor, tooltip);
        panel.Controls.Add(editor);
        panel.Controls.Add(caption);
        return (panel, new ProfileBinding(field, type, original, editor, read));
    }

    private (Control Editor, Func<string> Read) TextValueEditor(string original)
    {
        var input = new TextBox
        {
            Dock = DockStyle.Fill, Text = original,
            BackColor = _fieldBg, ForeColor = _light,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 10)
        };
        input.TextChanged += (_, _) => MarkProfileEdited();
        return (input, () => input.Text);
    }

    private static bool IsWholeNumber(Type type) =>
        type == typeof(byte) || type == typeof(short) ||
        type == typeof(int) || type == typeof(long);

    private static (decimal Min, decimal Max) NumericBounds(Type type)
    {
        if (type == typeof(byte)) return (byte.MinValue, byte.MaxValue);
        if (type == typeof(short)) return (short.MinValue, short.MaxValue);
        if (type == typeof(int)) return (int.MinValue, int.MaxValue);
        if (type == typeof(long)) return (long.MinValue, long.MaxValue);
        throw new ArgumentException("Not a supported integer database type.");
    }

    private void MarkProfileEdited()
    {
        if (!_loadingProfile) RefreshShellStatus();
    }

    private List<FieldEdit> GetProfileEdits()
    {
        var edits = new List<FieldEdit>();
        if (_session == null || _currentWorker == null) return edits;
        foreach (var field in _profileBindings)
        {
            string value = field.CurrentValue();
            if (!string.Equals(value, field.OriginalValue, StringComparison.Ordinal))
                edits.Add(new FieldEdit("tblWorker", "UID", field.ColumnName, field.ColumnType, value));
        }
        return edits;
    }

    private int PendingProfileEditCount() => _profileBindings.Count(x =>
        !string.Equals(x.CurrentValue(), x.OriginalValue, StringComparison.Ordinal));

    private void FocusProfileField(string name)
    {
        _tabs.SelectedIndex = 0;
        var binding = _profileBindings.FirstOrDefault(x =>
            x.ColumnName.Equals(name, StringComparison.OrdinalIgnoreCase));
        binding?.Editor.Focus();
    }

    private void ShowEditValidation(string message)
    {
        MessageBox.Show(this, message, "Check worker edit",
            MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
