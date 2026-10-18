using System.Text.Json;

namespace TEWUniverse.Studio;

/// <summary>Paths and the opt-in preference only. The password is saved separately with Windows DPAPI.</summary>
internal sealed class AppSettings
{
    // Explicit direct-edit target. We do not auto-activate a legacy working copy.
    public string ActiveDatabasePath { get; set; } = "";
    // Old settings retained for backward-compatible JSON reading only.
    public string DefaultDatabasePath { get; set; } = "";
    public string CurrentCopyPath { get; set; } = "";
    public string BackupFolderPath { get; set; } = "";
    // The TEW IX People picture folder. Files are read in place, never imported into MDB.
    public string WorkerPicturesFolderPath { get; set; } = "";
    public bool RememberDatabasePassword { get; set; } = false;

    // App-only presentation categories. Not TEW IX title-type values and
    // never inserted into tblBelt. Keys combine database path with title UID.
    public Dictionary<string, string> TitleDisplayKinds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static string TitleDisplayKey(string databasePath, int titleUid) =>
        Path.GetFullPath(databasePath.Trim()).ToUpperInvariant() + "|" +
        titleUid.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public string GetTitleDisplayKind(string databasePath, int titleUid) =>
        TitleDisplayKinds != null &&
        TitleDisplayKinds.TryGetValue(TitleDisplayKey(databasePath, titleUid), out var kind) &&
        kind == "AnnualTournament" ? "AnnualTournament" : "Championship";

    public void SetTitleDisplayKind(string databasePath, int titleUid, string kind)
    {
        TitleDisplayKinds ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string key = TitleDisplayKey(databasePath, titleUid);
        if (kind == "AnnualTournament")
            TitleDisplayKinds[key] = kind;
        else
            TitleDisplayKinds.Remove(key);
    }

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
        ActiveDatabasePath = ActiveDatabasePath,
        DefaultDatabasePath = DefaultDatabasePath,
        CurrentCopyPath = CurrentCopyPath,
        BackupFolderPath = BackupFolderPath,
        WorkerPicturesFolderPath = WorkerPicturesFolderPath,
        RememberDatabasePassword = RememberDatabasePassword,
        TitleDisplayKinds = new Dictionary<string, string>(TitleDisplayKinds ?? new(), StringComparer.OrdinalIgnoreCase)
    };

    public static bool SamePath(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) &&
        string.Equals(Path.GetFullPath(a.Trim()).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(b.Trim()).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    public void NormalizeAndValidate()
    {
        ActiveDatabasePath = NormalizeFilePath(ActiveDatabasePath, "Active Database");
        DefaultDatabasePath = NormalizeFilePath(DefaultDatabasePath, "Legacy Default Database");
        CurrentCopyPath = NormalizeFilePath(CurrentCopyPath, "Current Copy");
        BackupFolderPath = string.IsNullOrWhiteSpace(BackupFolderPath)
            ? ""
            : Path.GetFullPath(BackupFolderPath.Trim());

        WorkerPicturesFolderPath = string.IsNullOrWhiteSpace(WorkerPicturesFolderPath)
            ? ""
            : Path.GetFullPath(WorkerPicturesFolderPath.Trim());
        if (WorkerPicturesFolderPath.Length > 0 && File.Exists(WorkerPicturesFolderPath))
            throw new InvalidOperationException("Worker Picture Folder must be a directory.");

        TitleDisplayKinds ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        TitleDisplayKinds = new Dictionary<string, string>(TitleDisplayKinds, StringComparer.OrdinalIgnoreCase);
        // Direct editing deliberately permits the previously protected source path.
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
