using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TEWUniverse.Studio;

/// <summary>
/// A worker's manually curated career achievements belong to Studio, not TEW.
/// They are stored as JSON under LocalApplicationData, keyed by the absolute
/// database path and worker UID. No OleDb connection, MDB write, or game
/// database schema is involved in this service.
/// </summary>
internal sealed record WorkerAccomplishment(
    Guid Id, int WorkerUid, string Category, string Title, string Year,
    string Notes, DateTimeOffset CreatedUtc)
{
    public string Summary => (Year.Length == 0 ? "" : Year + " · ") + Category;
}

internal sealed class WorkerAccomplishmentStore
{
    private sealed record Document(int Version, List<WorkerAccomplishment> Items);
    internal static readonly string[] Categories = {
        "Championship", "Award", "Tournament", "Hall of Fame", "Milestone", "Other"
    };
    private static readonly JsonSerializerOptions JsonOptions =
        new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    private readonly string _directory;
    public WorkerAccomplishmentStore(string? directory = null)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TEWUniverse", "Accomplishments");
    }

    internal string FilePath(string database)
    {
        string normalized = Path.GetFullPath(database);
        if (OperatingSystem.IsWindows()) normalized = normalized.ToUpperInvariant();
        string hash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
        return Path.Combine(_directory, hash + ".json");
    }

    private Document ReadDocument(string database)
    {
        string path = FilePath(database);
        if (!File.Exists(path)) return new Document(1, new List<WorkerAccomplishment>());
        try
        {
            var text = File.ReadAllText(path, Encoding.UTF8);
            var document = JsonSerializer.Deserialize<Document>(text, JsonOptions);
            if (document == null || document.Version != 1 || document.Items == null)
                throw new InvalidOperationException("Unrecognized Studio accomplishments file.");
            return document;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw new InvalidOperationException(
                "Studio accomplishments could not be read. The existing JSON file was NOT changed. " +
                "Review it before attempting to save: " + path, ex);
        }
    }

    public IReadOnlyList<WorkerAccomplishment> List(string database, int workerId) =>
        ReadDocument(database).Items.Where(x => x.WorkerUid == workerId)
            .OrderByDescending(x => x.Year).ThenBy(x => x.Category)
            .ThenBy(x => x.Title).ToArray();

    private static (string Category, string Title, string Year, string Notes)
        Validate(string category, string title, string year, string notes)
    {
        category = category.Trim();
        title = title.Trim();
        year = year.Trim();
        notes = notes.Trim();
        if (!Categories.Contains(category, StringComparer.Ordinal))
            throw new InvalidOperationException("Choose a known accomplishment category.");
        if (title.Length is < 1 or > 180 || title.Any(char.IsControl))
            throw new InvalidOperationException("Accomplishment title must have 1–180 characters.");
        if (year.Length != 0 &&
            (!int.TryParse(year, out int number) || number is < 1800 or > 2100))
            throw new InvalidOperationException("Year must be blank or between 1800 and 2100.");
        if (notes.Length > 4000)
            throw new InvalidOperationException("Notes cannot exceed 4,000 characters.");
        return (category, title, year, notes);
    }

    private void WriteDocument(string database, Document document)
    {
        string path = FilePath(database);
        Directory.CreateDirectory(_directory);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(document, JsonOptions),
                new UTF8Encoding(false));
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public WorkerAccomplishment Add(string database, int workerUid,
        string category, string title, string year, string notes)
    {
        if (workerUid <= 0) throw new InvalidOperationException("Choose an existing worker.");
        var values = Validate(category, title, year, notes);
        var doc = ReadDocument(database);
        var entry = new WorkerAccomplishment(Guid.NewGuid(), workerUid,
            values.Category, values.Title, values.Year, values.Notes,
            DateTimeOffset.UtcNow);
        doc.Items.Add(entry);
        WriteDocument(database, doc);
        return entry;
    }

    public WorkerAccomplishment Update(string database, int workerUid, Guid uid,
        string category, string title, string year, string notes)
    {
        var values = Validate(category, title, year, notes);
        var doc = ReadDocument(database);
        int index = doc.Items.FindIndex(x => x.Id == uid && x.WorkerUid == workerUid);
        if (index < 0)
            throw new InvalidOperationException(
                "This accomplishment is missing or belongs to another worker.");
        var updated = doc.Items[index] with {
            Category = values.Category, Title = values.Title,
            Year = values.Year, Notes = values.Notes
        };
        doc.Items[index] = updated;
        WriteDocument(database, doc);
        return updated;
    }

    public void Remove(string database, int workerUid, Guid uid)
    {
        var doc = ReadDocument(database);
        int removed = doc.Items.RemoveAll(x => x.Id == uid && x.WorkerUid == workerUid);
        if (removed != 1)
            throw new InvalidOperationException("Selected accomplishment was not found.");
        WriteDocument(database, doc);
    }
}
