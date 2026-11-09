using System.Security.Cryptography;
using System.Text;

namespace TEWUniverse.Studio;

/// <summary>
/// Optional, Windows-user-scoped credential storage. The plaintext password is
/// never written to settings.json, source control or application diagnostics.
/// </summary>
internal static class CredentialStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes(
        "TEW Universe Studio | locally remembered Access database password | v1");

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TEW Universe Studio", "credential.dpapi");

    public static void Save(string password)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Remember password requires Windows.");
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Cannot remember an empty password.");

        var bytes = Encoding.UTF8.GetBytes(password);
        try
        {
            var encrypted = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temp = FilePath + ".tmp";
            File.WriteAllBytes(temp, encrypted);
            File.Move(temp, FilePath, overwrite: true);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public static string? TryRead()
    {
        if (!File.Exists(FilePath)) return null;
        try
        {
            var encrypted = File.ReadAllBytes(FilePath);
            var plaintext = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            try { return Encoding.UTF8.GetString(plaintext); }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            // Account migration, deleted Windows key or damaged credential.
            // Let the user enter it normally rather than loading incorrect data.
            return null;
        }
    }

    public static void Delete()
    {
        if (File.Exists(FilePath)) File.Delete(FilePath);
    }

    // Save passwords are independent of scenario credentials. Each has a
    // different DPAPI entropy and file, so opening one cannot overwrite the other.
    private static readonly byte[] SaveEntropy = Encoding.UTF8.GetBytes(
        "TEW Universe Studio | saved TEW IX game password | v1");

    private static string SaveFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TEW Universe Studio", "save-credential.dpapi");

    public static void SaveGamePassword(string password)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Remember password requires Windows.");
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Cannot remember an empty password.");

        var bytes = Encoding.UTF8.GetBytes(password);
        try
        {
            var encrypted = ProtectedData.Protect(
                bytes, SaveEntropy, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(SaveFilePath)!);
            var temp = SaveFilePath + ".tmp";
            File.WriteAllBytes(temp, encrypted);
            File.Move(temp, SaveFilePath, overwrite: true);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public static string? TryReadSaveGamePassword()
    {
        if (!File.Exists(SaveFilePath)) return null;
        try
        {
            var encrypted = File.ReadAllBytes(SaveFilePath);
            var plaintext = ProtectedData.Unprotect(
                encrypted, SaveEntropy, DataProtectionScope.CurrentUser);
            try { return Encoding.UTF8.GetString(plaintext); }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void DeleteSaveGamePassword()
    {
        if (File.Exists(SaveFilePath)) File.Delete(SaveFilePath);
    }
}
