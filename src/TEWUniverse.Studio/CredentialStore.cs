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
}
