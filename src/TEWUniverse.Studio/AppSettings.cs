using System.Text.Json;

namespace TEWUniverse.Studio;

/// <summary>Only file locations are persisted. Never store database credentials.</summary>
internal sealed class AppSettings
{
    public string DefaultDatabasePath { get; set; } = "";
    public string CurrentCopyPath { get; set; } = "";
    public string BackupFolderPath { get; set; } = "";

    public static string SettingsFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TEW Universe Studio", "settings.json");

    public static AppSettings Load()
    {
        if (!File.Exists(SettingsFile)) return new AppSettings();
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile))
                ?? new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Cannot read the saved application settings at {SettingsFile}. " +
                "Please repair or rename the file before starting the editor.", ex);
        }
    }

    public AppSettings Copy() => new()
    {
        DefaultDatabasePath = DefaultDatabasePath,
        CurrentCopyPath = CurrentCopyPath,
        BackupFolderPath = BackupFolderPath
    };

    public static bool SamePath(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) &&
        string.Equals(Path.GetFullPath(a.Trim()).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(b.Trim()).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    public void NormalizeAndValidate()
    {
        DefaultDatabasePath = NormalizeFilePath(DefaultDatabasePath, "Default Database");
        CurrentCopyPath = NormalizeFilePath(CurrentCopyPath, "Current Copy");
        BackupFolderPath = string.IsNullOrWhiteSpace(BackupFolderPath)
            ? ""
            : Path.GetFullPath(BackupFolderPath.Trim());

        if (SamePath(DefaultDatabasePath, CurrentCopyPath))
            throw new InvalidOperationException(
                "Default Database and Current Copy cannot be the same file. " +
                "Keep the original scenario untouched and edit a separate copy.");
        if (BackupFolderPath.Length > 0 && File.Exists(BackupFolderPath))
            throw new InvalidOperationException("Backup Folder must be a directory, not a file.");
    }

    private static string NormalizeFilePath(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var path = Path.GetFullPath(value.Trim());
        if (!path.EndsWith(".mdb", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{label} must point to a .mdb file.");
        return path;
    }

    public void Save()
    {
        NormalizeAndValidate();
        var directory = Path.GetDirectoryName(SettingsFile)!;
        Directory.CreateDirectory(directory);
        string temp = SettingsFile + ".tmp";
        File.WriteAllText(temp,
            JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, SettingsFile, overwrite: true);
    }
}
