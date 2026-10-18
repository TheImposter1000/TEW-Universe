using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TEWUniverse.Studio;

namespace TEWUniverse.Desktop;

/// <summary>
/// Uses TEW IX tblWorker.Picture (filename), not the worker's ID/name.
/// Only reads images beneath the selected folder; never writes to the game.
/// </summary>
public sealed class WorkerPortraitConverter : IValueConverter
{
    private string _folder = "";
    private readonly Dictionary<string, ImageBrush?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private const int MaxCachedPortraits = 250;

    public void SetFolder(string? folder)
    {
        _folder = string.IsNullOrWhiteSpace(folder) ? "" : Path.GetFullPath(folder.Trim());
        _cache.Clear();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is WorkerItem worker
            ? ((Brush?)LoadBrush(worker.PictureName, 110)) ?? Brushes.Transparent
            : Brushes.Transparent;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;

    public ImageBrush? LoadBrush(string? storedPictureName, int decodeWidth)
    {
        var file = ResolveFile(storedPictureName);
        if (file == null) return null;
        var key = file + "|" + decodeWidth.ToString(CultureInfo.InvariantCulture);
        if (_cache.TryGetValue(key, out var cached)) return cached;

        ImageBrush? brush = null;
        try
        {
            // CacheOption.OnLoad releases the file lock immediately. Downsample
            // list thumbnails so a large TEW image pack remains responsive.
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = decodeWidth;
            bitmap.UriSource = new Uri(file, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            brush = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
            brush.Freeze();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            // Damaged and unsupported image files retain the silhouette.
        }
        // Keep memory bounded when scrolling thousands of workers.
        if (_cache.Count >= MaxCachedPortraits) _cache.Clear();
        _cache[key] = brush;
        return brush;
    }

    private string? ResolveFile(string? storedPictureName)
    {
        if (_folder.Length == 0 || !Directory.Exists(_folder) ||
            string.IsNullOrWhiteSpace(storedPictureName)) return null;

        var name = storedPictureName.Trim().Trim('"');
        if (name is "" or "." or ".." || name.Equals("None", StringComparison.OrdinalIgnoreCase)
            || name.Equals("None.jpg", StringComparison.OrdinalIgnoreCase)
            || name.Equals("No Picture", StringComparison.OrdinalIgnoreCase)
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name.Contains('/') || name.Contains('\\'))
            return null;

        // The user may choose the People folder itself or its parent picture-pack folder.
        var folders = new[] { _folder, Path.Combine(_folder, "People") };
        var extensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };
        bool hasExtension = Path.HasExtension(name);
        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder)) continue;
            if (hasExtension)
            {
                var exact = Path.Combine(folder, name);
                if (File.Exists(exact)) return exact;
            }
            else
            {
                foreach (var extension in extensions)
                {
                    var candidate = Path.Combine(folder, name + extension);
                    if (File.Exists(candidate)) return candidate;
                }
            }
        }
        return null;
    }
}
