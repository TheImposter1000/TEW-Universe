using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace TEWUniverse.Studio;

internal sealed record AvailableUpdate(Version Version, string InstallerUrl, string ChecksumUrl, string Filename);

internal static class UpdateService
{
    private static readonly Uri LatestUrl = new("https://api.github.com/repos/TheImposter1000/TEW-Universe/releases/latest");

    private static HttpClient Client()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(40) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("TEW-Universe-Studio", "0.1"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public static async Task<AvailableUpdate?> FindUpdateAsync()
    {
        using var client = Client();
        using var response = await client.GetAsync(LatestUrl);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null; // No release published yet.
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version)) return null;
        var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 1, 0);
        if (version <= current) return null;
        var filename = $"TEW-Universe-Setup-{version.Major}.{version.Minor}.{version.Build}.exe";
        var assets = root.GetProperty("assets").EnumerateArray().ToArray();
        string? Find(string name) => assets.FirstOrDefault(a =>
            string.Equals(a.GetProperty("name").GetString(), name, StringComparison.OrdinalIgnoreCase))
            .TryGetProperty("browser_download_url", out var url) ? url.GetString() : null;
        var installerUrl = Find(filename);
        var checksumUrl = Find(filename + ".sha256");
        if (installerUrl == null || checksumUrl == null) return null;
        return new AvailableUpdate(version, installerUrl, checksumUrl, filename);
    }

    public static async Task<string> DownloadVerifiedAsync(AvailableUpdate release)
    {
        // The checksum and installer originate from the same GitHub Release; unsigned
        // binaries are still less secure than properly code-signed releases.
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TEW Universe Studio", "Updates", release.Version.ToString());
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, release.Filename);
        using var client = Client();
        string sumText = await client.GetStringAsync(release.ChecksumUrl);
        var expected = sumText.Split(new[] { ' ', '\r', '\n', '\t' },
            StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (expected.Length != 64 || !expected.All(Uri.IsHexDigit))
            throw new InvalidOperationException("Release checksum is missing or malformed.");
        using (var response = await client.GetAsync(release.InstallerUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            await using var remote = await response.Content.ReadAsStreamAsync();
            await using var file = File.Create(target);
            await remote.CopyToAsync(file);
        }
        await using var verify = File.OpenRead(target);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(verify));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(target);
            throw new InvalidOperationException("Installer checksum failed. No update will be run.");
        }
        return target;
    }
}
