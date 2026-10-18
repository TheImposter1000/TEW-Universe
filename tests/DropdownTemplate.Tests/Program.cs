using System.Xml.Linq;

// Prevent blank text in editable WPF ComboBoxes. Their control template must
// expose PART_EditableTextBox for selection display and keyboard search.
XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "App.xaml"));
var comboStyle = document.Descendants(wpf + "Style").Single(e =>
    (string?)e.Attribute(xaml + "Key") == "DarkComboBox");

var editor = comboStyle.Descendants(wpf + "TextBox").SingleOrDefault(e =>
    (string?)e.Attribute(xaml + "Name") == "PART_EditableTextBox");
if (editor is null || (string?)editor.Attribute("Visibility") != "Collapsed" ||
    (string?)editor.Attribute("IsReadOnly") != "{TemplateBinding IsReadOnly}")
    throw new Exception("Editable ComboBox text part is missing or incorrectly configured.");

var selection = comboStyle.Descendants(wpf + "ContentPresenter").SingleOrDefault(e =>
    (string?)e.Attribute(xaml + "Name") == "SelectedContent");
if (selection is null ||
    (string?)selection.Attribute("Content") != "{TemplateBinding SelectionBoxItem}")
    throw new Exception("Non-editable ComboBox selection presenter is missing.");

var arrow = comboStyle.Descendants(wpf + "ToggleButton").SingleOrDefault(e =>
    (string?)e.Attribute(xaml + "Name") == "DropDownToggle");
if (arrow is null)
    throw new Exception("ComboBox dropdown toggle is missing.");

var editableTrigger = comboStyle.Descendants(wpf + "Trigger").SingleOrDefault(e =>
    (string?)e.Attribute("Property") == "IsEditable" &&
    (string?)e.Attribute("Value") == "True");
if (editableTrigger is null)
    throw new Exception("Editable ComboBox state trigger is missing.");

static bool HasSetter(XElement trigger, XNamespace wpf, string target, string property, string value) =>
    trigger.Elements(wpf + "Setter").Any(e =>
        (string?)e.Attribute("TargetName") == target &&
        (string?)e.Attribute("Property") == property &&
        (string?)e.Attribute("Value") == value);

if (!HasSetter(editableTrigger, wpf, "SelectedContent", "Visibility", "Collapsed") ||
    !HasSetter(editableTrigger, wpf, "PART_EditableTextBox", "Visibility", "Visible") ||
    !HasSetter(editableTrigger, wpf, "DropDownToggle", "HorizontalAlignment", "Right"))
    throw new Exception("Editable ComboBox state is not exposing the text field and arrow correctly.");

Console.WriteLine("Passed editable and non-editable ComboBox template regression checks.");
