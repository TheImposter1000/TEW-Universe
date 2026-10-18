using System.Data;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace TEWUniverse.Studio;

/// <summary>
/// Phase 2: type-aware controls for existing worker profile columns. These
/// are derived from the live schema, never from invented TEW IX field values.
/// </summary>
internal sealed partial class MainForm
{
    private sealed record ProfileBinding(
        string ColumnName, Type ColumnType, string OriginalValue, Control Editor,
        Func<string> CurrentValue);

    private readonly Panel _profileFieldsHost = new();
    private readonly List<ProfileBinding> _profileBindings = new();
    private readonly ToolTip _fieldHelp = new();

    private Control BuildProfileForm()
    {
        _profileFieldsHost.Dock = DockStyle.Fill;
        _profileFieldsHost.AutoScroll = true;
        _profileFieldsHost.BackColor = _surface;
        _profileFieldsHost.Padding = new Padding(9, 5, 9, 5);
        return _profileFieldsHost;
    }

    private void ClearProfileForm()
    {
        _profileBindings.Clear();
        _profileFieldsHost.Controls.Clear();
    }

    private void LoadProfileForm(RecordSection record)
    {
        _profileFieldsHost.SuspendLayout();
        try
        {
            ClearProfileForm();
            if (!record.Exists)
            {
                _profileFieldsHost.Controls.Add(new Label
                {
                    Text = "No existing profile record was found for this worker.",
                    Dock = DockStyle.Top, Height = 65, ForeColor = _muted
                });
                return;
            }

            var fields = record.Data.Columns.Cast<DataColumn>()
                .Where(column =>
                    !column.ColumnName.Equals(record.Key, StringComparison.OrdinalIgnoreCase) &&
                    DatabaseSession.IsEditable(column.DataType))
                .OrderBy(column => WorkerFieldCatalog.ProfileOrder(column.ColumnName))
                .ThenBy(column => column.Ordinal).ToList();

            var grid = new TableLayoutPanel
            {
                ColumnCount = 2, RowCount = (fields.Count + 1) / 2,
                Dock = DockStyle.Top,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = _surface, Padding = new Padding(1, 4, 2, 5)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            for (int i = 0; i < grid.RowCount; i++)
                grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));

            foreach (var (column, index) in fields.Select((item, index) => (item, index)))
            {
                string original = DatabaseSession.Display(record.Data.Rows[0][column]);
                var widget = MakeProfileWidget(column.ColumnName, column.DataType, original);
                grid.Controls.Add(widget.Item1, index % 2, index / 2);
                _profileBindings.Add(widget.Item2);
            }

            _profileFieldsHost.Controls.Add(grid);
        }
        finally { _profileFieldsHost.ResumeLayout(); }
    }

    private (Control, ProfileBinding) MakeProfileWidget(string field, Type type, string original)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(5, 3, 9, 5),
            Padding = new Padding(5, 2, 5, 3),
            BackColor = Color.FromArgb(25, 44, 60)
        };
        var caption = new Label
        {
            Dock = DockStyle.Top, Height = 28,
            Text = WorkerFieldCatalog.FriendlyName(field, type),
            ForeColor = _light,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        Control editor;
        Func<string> readValue;

        if (type == typeof(bool))
        {
            var checkbox = new CheckBox
            {
                Dock = DockStyle.Fill, ThreeState = true, AutoSize = false,
                Text = "Yes / No",
                ForeColor = _light, Font = new Font("Segoe UI", 10),
                CheckState = original switch
                {
                    "True" or "true" or "1" => CheckState.Checked,
                    "False" or "false" or "0" => CheckState.Unchecked,
                    _ => CheckState.Indeterminate
                }
            };
            readValue = () => checkbox.CheckState switch
            {
                CheckState.Checked => "True",
                CheckState.Unchecked => "False",
                _ => ""
            };
            checkbox.CheckStateChanged += (_, _) => RefreshShellStatus();
            editor = checkbox;
        }
        else if (type == typeof(DateTime) &&
                 (string.IsNullOrEmpty(original) ||
                  (DateTime.TryParse(original, CultureInfo.InvariantCulture,
                      DateTimeStyles.None, out var date) &&
                   date >= DateTimePicker.MinDateTime && date <= DateTimePicker.MaxDateTime)))
        {
            // A nullable DateTimePicker uses Checked=false for a database NULL.
            var picker = new DateTimePicker
            {
                Dock = DockStyle.Fill,
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "yyyy-MM-dd",
                ShowCheckBox = true,
                CalendarForeColor = Color.Black,
                Value = DateTime.TryParse(original, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsed) &&
                    parsed >= DateTimePicker.MinDateTime &&
                    parsed <= DateTimePicker.MaxDateTime ? parsed : DateTime.Today,
                Checked = original.Length != 0,
                Font = new Font("Segoe UI", 10)
            };
            readValue = () => picker.Checked ?
                picker.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "";
            picker.ValueChanged += (_, _) => RefreshShellStatus();
            editor = picker;
        }
        else
        {
            // Unknown numeric enum codes remain plain, typed values instead of
            // misleading dropdowns (e.g., Gender=5 is not assumed to be a label).
            var input = new TextBox
            {
                Dock = DockStyle.Fill,
                Text = original,
                BackColor = _fieldBg, ForeColor = _light,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 10)
            };
            readValue = () => input.Text;
            input.TextChanged += (_, _) => RefreshShellStatus();
            editor = input;
        }

        var tooltip = WorkerFieldCatalog.FieldDescription(field, type);
        _fieldHelp.SetToolTip(caption, tooltip);
        _fieldHelp.SetToolTip(editor, tooltip);
        panel.Controls.Add(editor);
        panel.Controls.Add(caption);
        return (panel, new ProfileBinding(field, type, original, editor, readValue));
    }

    private List<FieldEdit> GetProfileEdits()
    {
        var edits = new List<FieldEdit>();
        if (_session == null || _currentWorker == null) return edits;
        foreach (var field in _profileBindings)
        {
            string value = field.CurrentValue();
            if (value != field.OriginalValue)
                edits.Add(new FieldEdit("tblWorker", "UID", field.ColumnName,
                    field.ColumnType, value));
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
        MessageBox.Show(this, message, "Check worker edit", MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }
}
