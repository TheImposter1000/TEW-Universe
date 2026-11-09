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


var window = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml"));
XElement Named(string element, string name) =>
    window.Descendants(wpf + element).Single(x =>
        (string?)x.Attribute(xaml + "Name") == name);
var advancedPanel = Named("Border", "AdvancedLineagePanel");
var setupScroll = Named("ScrollViewer", "AdvancedSetupScrollViewer");
var previewGrid = Named("DataGrid", "AdvancedPreviewGrid");
var candidatesGrid = Named("DataGrid", "AdvancedCandidatesGrid");
var previewButton = Named("Button", "AdvancedPreviewViewButton");
var builderButton = Named("Button", "AdvancedBuilderViewButton");
var applyButton = Named("Button", "AdvancedApplyButton");
var generateButton = window.Descendants(wpf + "Button").Single(x =>
    (string?)x.Attribute("Click") == "AdvancedGenerate_Click");
if (!setupScroll.Ancestors().Contains(advancedPanel) ||
    !previewGrid.Ancestors().Contains(advancedPanel) ||
    setupScroll.Ancestors(wpf + "ScrollViewer").Any() ||
    previewGrid.Ancestors(wpf + "ScrollViewer").Any() ||
    previewGrid.Ancestors().Contains(setupScroll) ||
    !candidatesGrid.Ancestors().Contains(setupScroll) ||
    (string?)previewGrid.Attribute("Grid.Row") != "1" ||
    (string?)previewGrid.Attribute("Visibility") != "Collapsed" ||
    (string?)previewButton.Attribute("IsEnabled") != "False" ||
    (string?)setupScroll.Attribute("VerticalScrollBarVisibility") != "Auto" ||
    !int.TryParse((string?)candidatesGrid.Attribute("Height"), out int candidateHeight) ||
    candidateHeight < 200 ||
    (string?)candidatesGrid.Attribute("FrozenColumnCount") != "1" ||
    (string?)previewGrid.Attribute("FrozenColumnCount") != "2" ||
    new[] { builderButton, previewButton, generateButton, applyButton }
        .Any(x => x.Ancestors(wpf + "ScrollViewer").Any()) ||
    advancedPanel.Descendants(wpf + "ScrollViewer").Any(x =>
        (string?)x.Attribute("MaxHeight") == "437"))
    throw new Exception("Championship Advanced Generator regressed into clipped/nested scrolling or hidden actions.");

Console.WriteLine("Passed championship Builder/Preview layout, reachable actions and full-height preview checks.");

// Save Studio must stay inside the existing desktop window rather than
// opening a separate top-level editor with a filtered catalog.
var saveTablesNav = Named("Button", "SaveTablesNav");
var saveTablesPanel = Named("ContentControl", "SaveTablesPanel");
var scenarioStart = Named("Button", "ScenarioStartButton");
var openSave = Named("Button", "OpenSaveButton");
if ((string?)saveTablesNav.Attribute("Click") != "SaveTablesNav_Click" ||
    (string?)saveTablesNav.Attribute("Visibility") != "Collapsed" ||
    (string?)saveTablesPanel.Attribute("Visibility") != "Collapsed" ||
    (string?)saveTablesPanel.Attribute("Grid.ColumnSpan") != "2" ||
    (string?)openSave.Attribute("Click") != "OpenSaveStudio_Click" ||
    (string?)scenarioStart.Attribute("Click") != "EditScenarioStart_Click")
    throw new Exception("Integrated Save Studio workspace/nav has regressed.");

Console.WriteLine("Passed integrated Save Studio main-window navigation checks.");


var settingsWindow = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "SettingsWindow.xaml"));
var settingNames = settingsWindow.Descendants()
    .Select(node => (string?)node.Attribute(xaml + "Name"))
    .Where(name => !string.IsNullOrWhiteSpace(name)).ToHashSet();
if (!settingNames.Contains("SaveGamePath") ||
    !settingNames.Contains("RememberSavePassword") ||
    !settingsWindow.Descendants(wpf + "Button")
        .Any(button => (string?)button.Attribute("Click") == "SaveGameBrowse_Click"))
    throw new Exception("Save game path and separate credential controls are missing in Settings.");

Console.WriteLine("Passed Save Studio path and credential settings UI checks.");


// The scrollbars shared by the main editor and Save Studio must have
// horizontal and vertical tracks. Previously the global Width=12 style
// collapsed every horizontal track to a 12-pixel control.
var darkScroll = document.Descendants(wpf + "Style").Single(style =>
    (string?)style.Attribute("TargetType") == "ScrollBar" &&
    style.Attribute(xaml + "Key") == null);
var scrollTemplate = darkScroll.Descendants(wpf + "ControlTemplate").Single();
var scrollTrack = scrollTemplate.Descendants(wpf + "Track").Single();
var horizontalScrollTemplateTrigger = scrollTemplate.Descendants(wpf + "Trigger").Single(t =>
    (string?)t.Attribute("Property") == "Orientation" &&
    (string?)t.Attribute("Value") == "Horizontal");
var horizontalBarSizeTrigger = darkScroll.Element(wpf + "Style.Triggers")?
    .Elements(wpf + "Trigger").SingleOrDefault(t =>
        (string?)t.Attribute("Property") == "Orientation" &&
        (string?)t.Attribute("Value") == "Horizontal");
if ((string?)scrollTrack.Attribute("Orientation") != "Vertical" ||
    horizontalBarSizeTrigger == null ||
    !HasSetter(horizontalScrollTemplateTrigger, wpf, "PART_Track", "Orientation", "Horizontal") ||
    !HasSetter(horizontalScrollTemplateTrigger, wpf, "DecreasePage", "Command",
        "{x:Static ScrollBar.PageLeftCommand}") ||
    !HasSetter(horizontalScrollTemplateTrigger, wpf, "IncreasePage", "Command",
        "{x:Static ScrollBar.PageRightCommand}") ||
    !horizontalBarSizeTrigger.Elements(wpf + "Setter").Any(setter =>
        (string?)setter.Attribute("Property") == "Width" &&
        (string?)setter.Attribute("Value") == "Auto") ||
    !horizontalBarSizeTrigger.Elements(wpf + "Setter").Any(setter =>
        (string?)setter.Attribute("Property") == "Height" &&
        (string?)setter.Attribute("Value") == "14"))
    throw new Exception("WPF dark scrollbar orientation or horizontal track sizing has regressed.");

Console.WriteLine("Passed two-axis scrollbar orientation and horizontal width regression checks.");
