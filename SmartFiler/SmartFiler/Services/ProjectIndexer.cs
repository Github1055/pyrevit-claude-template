using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SmartFiler.Data;

namespace SmartFiler.Services
{
    /// <summary>
    /// Scans a root drive (default D:\) up to two levels deep and maintains
    /// an indexed cache of project folders in the database.
    /// </summary>
    public sealed class ProjectIndexer
    {
        private static readonly HashSet<string> SystemFolders = new(StringComparer.OrdinalIgnoreCase)
        {
            "$RECYCLE.BIN",
            "System Volume Information",
            "msdownld.tmp",
            "Recovery",
            "Users"
        };

        /// <summary>
        /// Folders that contain non-project content (games, caches, etc.) and should
        /// be excluded from fuzzy matching to avoid false-positive suggestions.
        /// </summary>
        private static readonly HashSet<string> ExcludedFolders = new(StringComparer.OrdinalIgnoreCase)
        {
            "SteamLibrary",
            "Games",
            "Megascans Assets",
            "D5 WorkSpace"
        };

        /// <summary>
        /// Extra folder to scan one level deep for supplier/product sub-folders.
        /// These folders sit at depth 3 from D:\ so are missed by the 2-level root scan.
        /// </summary>
        private const string SupplierFolderRoot = @"D:\D CPL OFFICE\06 Details & Suppliers";

        private readonly ProjectIndexRepo _repo;
        private readonly string _rootPath;
        private readonly TimeSpan _cacheTtl;

        /// <summary>
        /// Creates a new indexer targeting the specified root path.
        /// </summary>
        /// <param name="repo">Repository for persisting and querying the project index.</param>
        /// <param name="rootPath">Drive or directory root to scan (default <c>D:\</c>).</param>
        /// <param name="cacheTtl">
        /// How long a previous scan remains valid before a re-scan is required.
        /// Defaults to one hour.
        /// </param>
        public ProjectIndexer(
            ProjectIndexRepo repo,
            string rootPath = @"D:\",
            TimeSpan? cacheTtl = null)
        {
            _repo = repo;
            _rootPath = rootPath;
            _cacheTtl = cacheTtl ?? TimeSpan.FromHours(1);
        }

        /// <summary>
        /// Returns the current list of indexed project folders.
        /// Uses the cached index if it was scanned within the TTL window,
        /// unless <paramref name="forceRefresh"/> is <c>true</c>.
        /// </summary>
        /// <param name="forceRefresh">When <c>true</c>, ignores the cache and re-scans the drive.</param>
        /// <returns>A list of <see cref="ProjectFolder"/> records.</returns>
        public async Task<List<ProjectFolder>> GetProjectFoldersAsync(bool forceRefresh = false)
        {
            if (!forceRefresh)
            {
                var lastScan = await _repo.GetLastScanTimeAsync();
                if (lastScan.HasValue && (DateTime.UtcNow - lastScan.Value) < _cacheTtl)
                {
                    return await _repo.GetAllAsync();
                }
            }

            List<ProjectFolder> folders;
            try
            {
                folders = await Task.Run(() => ScanFolders());
            }
            catch (DriveNotFoundException)
            {
                var cached = await _repo.GetAllAsync();
                return cached.Count > 0 ? cached : new List<ProjectFolder>();
            }

            await _repo.ClearAsync();
            foreach (var folder in folders)
            {
                await _repo.UpsertAsync(folder);
            }
            return folders;
        }

        /// <summary>
        /// Synchronously enumerates folders up to two levels deep under <see cref="_rootPath"/>.
        /// </summary>
        private List<ProjectFolder> ScanFolders()
        {
            var results = new List<ProjectFolder>();
            var now = DateTime.UtcNow;

            if (!Directory.Exists(_rootPath))
                throw new DriveNotFoundException($"Root path not found: {_rootPath}");

            // Level 1: immediate children of root
            IEnumerable<string> topDirs;
            try
            {
                topDirs = Directory.EnumerateDirectories(_rootPath);
            }
            catch (UnauthorizedAccessException)
            {
                return results;
            }

            foreach (var level1 in topDirs)
            {
                var name1 = Path.GetFileName(level1);
                if (SystemFolders.Contains(name1) || ExcludedFolders.Contains(name1))
                    continue;

                try
                {
                    results.Add(CreateProjectFolder(level1, name1, now));
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                // Level 2: children of each level-1 folder
                IEnumerable<string> subDirs;
                try
                {
                    subDirs = Directory.EnumerateDirectories(level1);
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (var level2 in subDirs)
                {
                    var name2 = Path.GetFileName(level2);
                    if (SystemFolders.Contains(name2))
                        continue;

                    try
                    {
                        results.Add(CreateProjectFolder(level2, name2, now));
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Skip inaccessible sub-folder
                    }
                }
            }

            // Extra scan: supplier/product folders at D:\D CPL OFFICE\06 Details & Suppliers\
            // These are at depth 3 from D:\ so the 2-level root scan above misses them.
            if (Directory.Exists(SupplierFolderRoot))
            {
                IEnumerable<string> supplierDirs;
                try
                {
                    supplierDirs = Directory.EnumerateDirectories(SupplierFolderRoot);
                }
                catch (UnauthorizedAccessException)
                {
                    supplierDirs = [];
                }

                foreach (var supplierDir in supplierDirs)
                {
                    var supplierName = Path.GetFileName(supplierDir);
                    if (SystemFolders.Contains(supplierName) || ExcludedFolders.Contains(supplierName))
                        continue;

                    // Only add if not already indexed (the 2-level scan may have picked up the parent)
                    if (!results.Any(r => string.Equals(r.FolderPath, supplierDir, StringComparison.OrdinalIgnoreCase)))
                    {
                        try
                        {
                            results.Add(CreateProjectFolder(supplierDir, supplierName, now));
                        }
                        catch (UnauthorizedAccessException)
                        {
                            // Skip inaccessible supplier folder
                        }
                    }
                }
            }

            return results;
        }

        private static ProjectFolder CreateProjectFolder(string path, string name, DateTime scannedAt)
        {
            return new ProjectFolder
            {
                FolderPath = path,
                FolderName = name,
                LastModified = Directory.GetLastWriteTime(path),
                ScannedAt = scannedAt
            };
        }
    }
}
