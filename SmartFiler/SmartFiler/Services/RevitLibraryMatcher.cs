using System.IO;
using SmartFiler.Data;

namespace SmartFiler.Services;

/// <summary>
/// Matches Revit family files (.rfa) to the best subfolder within the Revit Families library
/// at D:\D CPL OFFICE\05 REVIT\000_Revit Families. Scans the full directory tree and uses
/// fuzzy token matching to find the most relevant subfolder for each file.
/// Results are cached in SQLite for fast subsequent lookups.
/// </summary>
public sealed class RevitLibraryMatcher
{
    private static readonly string RevitFamiliesRoot = @"D:\D CPL OFFICE\05 REVIT\000_Revit Families";
    private static readonly string RevitRoot = @"D:\D CPL OFFICE\05 REVIT";

    private List<string>? _cachedFolders;
    private readonly FuzzyMatcher _fuzzyMatcher;

    public RevitLibraryMatcher(FuzzyMatcher fuzzyMatcher)
    {
        _fuzzyMatcher = fuzzyMatcher;
    }

    /// <summary>
    /// Finds the best matching subfolder in the Revit library for the given file.
    /// Searches the entire directory tree under 000_Revit Families and also
    /// the broader 05 REVIT tree for .rvt project files.
    /// </summary>
    public async Task<(string? FolderPath, double Score)> FindBestFolderAsync(ScannedFile file)
    {
        // Choose search root based on file type
        var searchRoot = file.Category switch
        {
            FileCategory.RevitFamily or FileCategory.RfaBackup => RevitFamiliesRoot,
            FileCategory.RevitProject => RevitRoot,
            FileCategory.RevitTemplate => Path.Combine(RevitRoot, "000_ Revit Template"),
            _ => RevitRoot
        };

        if (!Directory.Exists(searchRoot))
            return (null, 0);

        var folders = await GetFoldersAsync(searchRoot);
        if (folders.Count == 0)
            return (null, 0);

        // Build ProjectFolder objects for the fuzzy matcher
        var projectFolders = folders.Select(f => new ProjectFolder
        {
            FolderPath = f,
            FolderName = Path.GetFileName(f),
            ScannedAt = DateTime.Now
        }).ToList();

        // Try matching against folder names
        var result = await _fuzzyMatcher.FindBestMatchAsync(file.FileName, projectFolders, threshold: 0.2);

        return result;
    }

    /// <summary>
    /// Gets all subfolders (recursively) under the given root. Cached after first scan.
    /// </summary>
    private Task<List<string>> GetFoldersAsync(string root)
    {
        if (_cachedFolders != null)
            return Task.FromResult(_cachedFolders);

        return Task.Run(() =>
        {
            var folders = new List<string>();
            try
            {
                ScanFolders(root, folders, maxDepth: 3, currentDepth: 0);
            }
            catch { /* Best effort */ }

            _cachedFolders = folders;
            return folders;
        });
    }

    private static void ScanFolders(string path, List<string> results, int maxDepth, int currentDepth)
    {
        if (currentDepth >= maxDepth) return;

        try
        {
            foreach (var dir in Directory.EnumerateDirectories(path))
            {
                var name = Path.GetFileName(dir);
                // Skip system/hidden folders
                if (name.StartsWith('.') || name.StartsWith('$'))
                    continue;

                results.Add(dir);
                ScanFolders(dir, results, maxDepth, currentDepth + 1);
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }
}
