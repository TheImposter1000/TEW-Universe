using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TEWUniverse.Studio;

namespace TEWUniverse.Desktop;

/// <summary>
/// Resolves a portrait by the worker's display name first, prioritizing PNG.
/// Only if there is no usable name-based picture does it fall back to tblWorker.Picture.
/// Source files are always read-only, and missing pictures use the silhouette.
/// </summary>
public sealed class WorkerPortraitConverter : IValueConverter
{
    private string _folder = "";
    private readonly Dictionary<string, ImageBrush?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _pictureMatches =
        new(StringComparer.OrdinalIgnoreCase);
    private const int MaxCachedPortraits = 250;

    private static readonly string[] Extensions = {
        ".png", ".webp", ".jpg", ".jpeg", ".gif", ".bmp"
    };

    private static readonly Brush PlaceholderBackground = CreatePlaceholderBackground();

    private static Brush CreatePlaceholderBackground()
    {
        var brush = new SolidColorBrush(Color.FromRgb(45, 81, 122));
        brush.Freeze();
        return brush;
    }

    public void SetFolder(string? folder)
    {
        _folder = string.IsNullOrWhiteSpace(folder) ? "" : Path.GetFullPath(folder.Trim());
        _cache.Clear();
        _pictureMatches.Clear();
    }

    /// <summary>
    /// True when a matching portrait file exists in the configured folder
    /// (including People), using the same name-first/MDB-fallback rules as
    /// actual rendering. No image decoding is necessary for filtering.
    /// </summary>
    public bool HasPicture(string? workerName, string? databasePicture)
    {
        string key = (workerName ?? "") + "\u001F" + (databasePicture ?? "");
        if (_pictureMatches.TryGetValue(key, out bool cached)) return cached;
        bool result = _folder.Length > 0 && Directory.Exists(_folder) &&
            CandidateFiles(workerName, databasePicture).Any();
        _pictureMatches[key] = result;
        return result;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var photo = value is WorkerItem worker
            ? LoadBrush(worker.Name, worker.PictureName, 110)
            : null;

        return parameter as string switch
        {
            "PlaceholderVisibility" => photo == null ? Visibility.Visible : Visibility.Collapsed,
            "PlaceholderBackground" => photo == null ? PlaceholderBackground : Brushes.Transparent,
            _ => (Brush?)photo ?? Brushes.Transparent
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;

    public ImageBrush? LoadBrush(string? workerName, string? databasePicture, int decodeWidth)
    {
        if (_folder.Length == 0 || !Directory.Exists(_folder)) return null;

        foreach (var file in CandidateFiles(workerName, databasePicture))
        {
            var key = file + "|" + decodeWidth.ToString(CultureInfo.InvariantCulture);
            if (_cache.TryGetValue(key, out var cached))
            {
                if (cached != null) return cached;
                continue;
            }

            ImageBrush? brush = null;
            try
            {
                // Decode thumbnails at their display size rather than retaining full
                // 1254px source images in the worker list. OnLoad also releases locks.
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = decodeWidth;
                image.UriSource = new Uri(file, UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                brush = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
                brush.Freeze();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException
                or InvalidOperationException)
            {
                // WPF cannot decode every WebP codec. Try the next candidate rather
                // than showing an empty portrait if a supported JPEG/GIF also exists.
            }

            if (_cache.Count >= MaxCachedPortraits) _cache.Clear();
            _cache[key] = brush;
            if (brush != null) return brush;
        }
        return null;
    }

    private IEnumerable<string> CandidateFiles(string? workerName, string? databasePicture)
    {
        var stems = new List<string>();

        void AddStem(string? source, bool stripExtension)
        {
            if (string.IsNullOrWhiteSpace(source)) return;
            var stem = source.Trim().Trim('"');
            if (stripExtension) stem = Path.GetFileNameWithoutExtension(stem);
            if (stem.Equals("None", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("No Picture", StringComparison.OrdinalIgnoreCase)) return;

            // Worker names can contain quotation marks that Windows forbids in
            // filenames; removing invalid characters allows e.g. "Mad" Mongo Flash.
            // All candidates remain single filenames, never directory traversal.
            var safe = new string(stem.Where(c =>
                c != '/' && c != '\\' && !Path.GetInvalidFileNameChars().Contains(c))
                .ToArray()).Trim().TrimEnd('.', ' ');
            if (safe.Length == 0 || safe is "." or "..") return;
            if (!stems.Contains(safe, StringComparer.OrdinalIgnoreCase))
                stems.Add(safe);
        }

        // Name-based artwork ALWAYS takes precedence, regardless of MDB extension.
        AddStem(workerName, stripExtension: false);
        // The database's Picture value is a compatibility fallback, not a requirement.
        AddStem(databasePicture, stripExtension: true);

        var locations = new[] { _folder, Path.Combine(_folder, "People") };
        foreach (var stem in stems)
        {
            foreach (var extension in Extensions)
            {
                foreach (var location in locations)
                {
                    if (!Directory.Exists(location)) continue;
                    var candidate = Path.Combine(location, stem + extension);
                    if (File.Exists(candidate)) yield return candidate;
                }
            }
        }
    }
}
