using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using TEWUniverse.Studio;

namespace TEWUniverse.Desktop;

/// <summary>
/// Worker roles are independent TEW IX flags, not a single exclusive enum.
/// The add-type dropdown sets only the requested existing column; removal
/// is handled by an explicit checked role toggle without clearing other roles.
/// </summary>
public partial class MainWindow
{
    private static readonly (string Column, string Label)[] EditableWorkerRoles = {
        ("Wrestler", "Wrestler"),
        ("OccasionalWrestler", "Occasional Wrestler"),
        ("Manager", "Manager"),
        ("Personality", "Personality"),
        ("Referee", "Referee"),
        ("RoadAgent", "Road Agent"),
        ("Announcer", "Announcer"),
        ("Colour", "Colour Commentator"),
        ("Color", "Colour Commentator"),
        ("ColourCommentator", "Colour Commentator"),
        ("ColorCommentator", "Colour Commentator"),
        ("Authority", "Authority Figure"),
        ("AuthorityFigure", "Authority Figure"),
        ("Interviewer", "Interviewer")
    };

    private static bool RoleOn(FieldDraft role) =>
        role.Value.Equals("True", StringComparison.OrdinalIgnoreCase) ||
        role.Value == "1" || role.Value == "-1";

    private static void SetRole(FieldDraft field, bool selected)
    {
        field.Value = field.ValueType == typeof(bool)
            ? selected ? "True" : "False"
            : selected ? "1" : "0";
    }

    private HashSet<FieldDraft> AddWorkerTypeEditor(
        StackPanel destination, List<FieldDraft> profile)
    {
        var roles = EditableWorkerRoles
            .Select(pair => (pair.Label, Field: profile.FirstOrDefault(f =>
                f.Column.Equals(pair.Column, StringComparison.OrdinalIgnoreCase) &&
                f.Table == "tblWorker" &&
                (f.ValueType == typeof(bool) ||
                 WorkerFieldCatalog.IsNumeric(f.ValueType)))))
            .Where(x => x.Field != null)
            .Select(x => (x.Label, Field: x.Field!))
            .DistinctBy(x => x.Field).ToList();

        var codeFields = profile.Where(f => f.Table == "tblWorker" &&
            new[] { "WorkerType", "Worker_Type", "Role", "Type" }
                .Contains(f.Column, StringComparer.OrdinalIgnoreCase) &&
            (f.ValueType == typeof(byte) || f.ValueType == typeof(short) ||
             f.ValueType == typeof(int) || f.ValueType == typeof(long))).ToList();
        var covered = roles.Select(x => x.Field).Concat(codeFields).ToHashSet();
        if (covered.Count == 0) return covered;

        AddSectionHeader(destination, "Worker Types");
        if (roles.Count > 0)
        {
            var summary = new TextBlock {
                Foreground = Muted, Margin = new Thickness(0, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap
            };
            var selectedPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 9) };
            var add = new ComboBox {
                Style = (Style)FindResource("DarkComboBox"), Height = 35,
                MaxDropDownHeight = 300, Margin = new Thickness(0, 0, 0, 8),
                ItemsSource = roles.Select(x => x.Label).Distinct().ToList(),
                ToolTip = "Select a worker type to enable it; existing selected types remain enabled."
            };
            destination.Children.Add(new TextBlock {
                Text = "Add worker type (multiple roles allowed)",
                Foreground = Muted, Margin = new Thickness(0, 0, 0, 6)
            });
            destination.Children.Add(add);
            destination.Children.Add(summary);
            destination.Children.Add(selectedPanel);
            bool handling = false;
            void Redraw()
            {
                selectedPanel.Children.Clear();
                var active = roles.Where(x => RoleOn(x.Field)).ToList();
                summary.Text = active.Count == 0
                    ? "No available role flags are enabled."
                    : "Selected: " + string.Join(", ", active.Select(x => x.Label).Distinct());
                foreach (var pair in active)
                {
                    var checkbox = new CheckBox {
                        Content = pair.Label + " (remove)",
                        IsChecked = true,
                        Foreground = Muted,
                        Margin = new Thickness(0, 0, 13, 8),
                        ToolTip = "Uncheck to disable this role only."
                    };
                    checkbox.Unchecked += (_, _) => {
                        SetRole(pair.Field, false);
                        Redraw();
                        RefreshDirty();
                    };
                    selectedPanel.Children.Add(checkbox);
                }
            }
            add.SelectionChanged += (_, _) => {
                if (handling || add.SelectedItem is not string label) return;
                foreach (var pair in roles.Where(x => x.Label == label))
                    SetRole(pair.Field, true);
                handling = true;
                add.SelectedItem = null;
                handling = false;
                Redraw();
                RefreshDirty();
            };
            Redraw();
        }

        // A separate numeric WorkerType/Role column is not the same thing as
        // boolean role flags. Offer existing stored codes only, not guessed
        // meanings, when this schema includes such a column.
        foreach (var field in codeFields)
        {
            var observed = _session?.ReadObservedWorkerTypeCodes(field.Column)
                ?? new List<int>();
            if (int.TryParse(field.Value, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int current) &&
                !observed.Contains(current))
                observed.Add(current);
            var choices = observed.Distinct().OrderBy(x => x)
                .Select(x => new TewCodeChoice(x, "Code " + x))
                .ToList();
            destination.Children.Add(new TextBlock {
                Text = field.Column + " (stored codes)",
                Foreground = Muted, Margin = new Thickness(0, 6, 0, 7)
            });
            var picker = new ComboBox {
                Style = (Style)FindResource("DarkComboBox"),
                ItemsSource = choices,
                SelectedItem = choices.FirstOrDefault(x =>
                    x.Code.ToString(CultureInfo.InvariantCulture) == field.Value),
                DisplayMemberPath = nameof(TewCodeChoice.Label),
                MaxDropDownHeight = 280, Height = 35,
                ToolTip = "Codes observed in this scenario. TEW IX meanings have not been verified."
            };
            picker.SelectionChanged += (_, _) => {
                if (picker.SelectedItem is not TewCodeChoice code) return;
                field.Value = code.Code.ToString(CultureInfo.InvariantCulture);
                if (!_loading) RefreshDirty();
            };
            destination.Children.Add(picker);
        }
        return covered;
    }
}
