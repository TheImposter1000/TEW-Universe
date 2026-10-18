using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace TEWUniverse.Studio;

internal sealed record AvailableUpdate(Version Version, string InstallerUrl, string ChecksumUrl, string Filename);
internal sealed record UpdateProgress(long BytesReceived, long? TotalBytes);

internal static class UpdateService
{
    private static readonly Uri LatestUrl = new(
        "https://api.github.com/repos/TheImposter1000/TEW-Universe/releases/latest");
    private static readonly object LogLock = new();

    public static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TEW Universe Studio", "Updates", "update.log");

    public static void Log(string message)
    {
        try
        {
            lock (LogLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
        }
        catch { /* Diagnostics should not stop the editor. */ }
    }

    private static HttpClient Client()
    {
        // Release installers are tens of megabytes. A 40-second global timeout
        // incorrectly treated slow but healthy connections as failed updates.
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("TEW-Universe-Studio", "0.1"));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public static async Task<AvailableUpdate?> FindUpdateAsync()
    {
        using var client = Client();
        using var response = await client.GetAsync(LatestUrl);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Log("No published GitHub Release available.");
            return null;
        }
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version))
            throw new InvalidOperationException($"GitHub's latest release tag '{tag}' is not a valid version.");

        var current = Assembly.GetExecutingAssembly().GetName().Version ??
            new Version(0, 1, 0);
        Log($"Update check: installed {current}, latest GitHub release {version}.");

        if (version <= current) return null;
        var filename = $"TEW-Universe-Setup-{version.Major}.{version.Minor}.{version.Build}.exe";
        var assets = root.GetProperty("assets").EnumerateArray().ToArray();

        string? Find(string name)
        {
            foreach (var asset in assets)
            {
                if (string.Equals(asset.GetProperty("name").GetString(),
                    name, StringComparison.OrdinalIgnoreCase))
                    return asset.GetProperty("browser_download_url").GetString();
            }
            return null;
        }

        var installerUrl = Find(filename);
        var checksumUrl = Find(filename + ".sha256");
        if (installerUrl == null || checksumUrl == null)
            throw new InvalidOperationException(
                $"GitHub release {tag} is missing the required installer or checksum. " +
                "Use the Releases page once publishing has finished.");

        return new AvailableUpdate(version, installerUrl, checksumUrl, filename);
    }

    public static async Task<string> DownloadVerifiedAsync(
        AvailableUpdate release, IProgress<UpdateProgress>? progress = null)
    {
        // Game databases, backup paths and passwords are never included here.
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TEW Universe Studio", "Updates", release.Version.ToString());
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, release.Filename);
        var temporary = target + ".partial";
        using var client = Client();

        Log($"Checking release checksum for {release.Filename}.");
        var sumText = await client.GetStringAsync(release.ChecksumUrl);
        var expected = sumText.Split(new[] { ' ', '\r', '\n', '\t' },
            StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (expected.Length != 64 || !expected.All(Uri.IsHexDigit))
            throw new InvalidOperationException("Release checksum is missing or malformed.");

        if (File.Exists(target) && await ChecksumMatchesAsync(target, expected))
        {
            Log($"Reusing already verified installer {release.Filename}.");
            progress?.Report(new UpdateProgress(new FileInfo(target).Length,
                new FileInfo(target).Length));
            return target;
        }

        if (File.Exists(temporary)) File.Delete(temporary);
        Log($"Downloading {release.Filename}.");
        try
        {
            using var response = await client.GetAsync(
                release.InstallerUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using (var input = await response.Content.ReadAsStreamAsync())
            await using (var output = new FileStream(temporary, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, bufferSize: 131072, useAsync: true))
            {
                byte[] buffer = new byte[131072];
                long written = 0;
                long lastReported = 0;
                int read;
                while ((read = await input.ReadAsync(buffer)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read));
                    written += read;
                    if (written - lastReported >= 1048576)
                    {
                        progress?.Report(new UpdateProgress(written, total));
                        lastReported = written;
                    }
                }
                progress?.Report(new UpdateProgress(written, total));
            }

            if (!await ChecksumMatchesAsync(temporary, expected))
                throw new InvalidOperationException(
                    "Downloaded installer checksum does not match the release. No update was installed.");

            File.Move(temporary, target, overwrite: true);
            Log($"Installer verified successfully ({new FileInfo(target).Length} bytes).");
            return target;
        }
        catch (Exception ex)
        {
            Log($"Installer download failed: {ex.GetType().Name}: {ex.Message}");
            if (File.Exists(temporary)) File.Delete(temporary);
            throw;
        }
    }

    private static async Task<bool> ChecksumMatchesAsync(string file, string expected)
    {
        await using var stream = File.OpenRead(file);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream));
        return actual.Equals(expected, StringComparison.OrdinalIgnoreCase);
    }
}
