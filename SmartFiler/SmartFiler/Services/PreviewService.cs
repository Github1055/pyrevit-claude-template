using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SmartFiler.Services;

/// <summary>
/// Generates file previews: image thumbnails, URL target parsing, and file properties.
/// PDF preview is deferred to v2 (requires a third-party library).
/// </summary>
public static class PreviewService
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp"
    };

    /// <summary>
    /// Loads a thumbnail image for the given file path.
    /// Returns null if the file is not an image or cannot be read.
    /// </summary>
    public static BitmapImage? LoadThumbnail(string filePath, int maxWidth = 280, int maxHeight = 160)
    {
        try
        {
            var ext = Path.GetExtension(filePath);
            if (!ImageExtensions.Contains(ext))
                return null;

            if (!File.Exists(filePath))
                return null;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
            bitmap.DecodePixelWidth = maxWidth;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parses a .url shortcut file and returns the target URL.
    /// Returns null if the file cannot be read or has no URL= line.
    /// </summary>
    public static string? ParseUrlShortcut(string filePath)
    {
        try
        {
            var ext = Path.GetExtension(filePath);
            if (!string.Equals(ext, ".url", StringComparison.OrdinalIgnoreCase))
                return null;

            if (!File.Exists(filePath))
                return null;

            foreach (var line in File.ReadLines(filePath))
            {
                if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                    return line[4..].Trim();
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reads basic file properties: size, dates, attributes.
    /// </summary>
    public static FileProperties? GetProperties(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return null;

            var info = new FileInfo(filePath);
            return new FileProperties
            {
                FullPath = info.FullName,
                FileName = info.Name,
                Extension = info.Extension,
                SizeBytes = info.Length,
                Created = info.CreationTime,
                Modified = info.LastWriteTime,
                IsReadOnly = info.IsReadOnly,
                Directory = info.DirectoryName ?? ""
            };
        }
        catch
        {
            return null;
        }
    }
}

public record FileProperties
{
    public string FullPath { get; init; } = "";
    public string FileName { get; init; } = "";
    public string Extension { get; init; } = "";
    public long SizeBytes { get; init; }
    public DateTime Created { get; init; }
    public DateTime Modified { get; init; }
    public bool IsReadOnly { get; init; }
    public string Directory { get; init; } = "";
}
