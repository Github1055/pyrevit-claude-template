using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SmartFiler.Services
{
    /// <summary>
    /// A file discovered during stale-file scanning, with age classification.
    /// </summary>
    public record StaleFileInfo
    {
        public string FullPath { get; init; } = "";
        public string FileName { get; init; } = "";
        public long SizeBytes { get; init; }
        public DateTime LastModified { get; init; }
        public int AgeDays { get; init; }
        public string AgeBucket { get; init; } = ""; // "90-180 days", "6 months - 1 year", "Over 1 year"
    }

    /// <summary>
    /// Recursively scans a root path for files that have not been modified within a
    /// configurable age threshold, grouping them into age buckets.
    /// </summary>
    public sealed class StaleDetector
    {
        private readonly string _rootPath;
        private readonly int _ageThresholdDays;

        private static readonly HashSet<string> SkipFolders = new(StringComparer.OrdinalIgnoreCase)
        {
            "$RECYCLE.BIN",
            "System Volume Information",
            "Recovery",
            "msdownld.tmp"
        };

        /// <summary>
        /// Creates a new stale-file detector.
        /// </summary>
        /// <param name="rootPath">Root directory to scan. Defaults to D:\.</param>
        /// <param name="ageThresholdDays">Minimum age in days for a file to be considered stale. Defaults to 90.</param>
        public StaleDetector(string rootPath = @"D:\", int ageThresholdDays = 90)
        {
            _rootPath = rootPath;
            _ageThresholdDays = ageThresholdDays;
        }

        /// <summary>
        /// Scans the root path recursively for files older than the age threshold.
        /// Returns results sorted by age (oldest first).
        /// </summary>
        public Task<List<StaleFileInfo>> FindStaleFilesAsync()
        {
            return Task.Run(() =>
            {
                var results = new List<StaleFileInfo>();
                var now = DateTime.Now;

                ScanDirectory(_rootPath, results, now);

                results.Sort((a, b) => b.AgeDays.CompareTo(a.AgeDays));
                return results;
            });
        }

        private void ScanDirectory(string directory, List<StaleFileInfo> results, DateTime now)
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly);
            }
            catch (UnauthorizedAccessException) { return; }
            catch (IOException) { return; }

            foreach (var filePath in files)
            {
                try
                {
                    var info = new FileInfo(filePath);
                    int ageDays = (int)(now - info.LastWriteTime).TotalDays;

                    if (ageDays >= _ageThresholdDays)
                    {
                        results.Add(new StaleFileInfo
                        {
                            FullPath = info.FullName,
                            FileName = info.Name,
                            SizeBytes = info.Length,
                            LastModified = info.LastWriteTime,
                            AgeDays = ageDays,
                            AgeBucket = GetAgeBucket(ageDays)
                        });
                    }
                }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }

            // Recurse into subdirectories, skipping system folders.
            IEnumerable<string> subdirectories;
            try
            {
                subdirectories = Directory.EnumerateDirectories(directory);
            }
            catch (UnauthorizedAccessException) { return; }
            catch (IOException) { return; }

            foreach (var subdir in subdirectories)
            {
                string dirName = Path.GetFileName(subdir);
                if (SkipFolders.Contains(dirName))
                    continue;

                ScanDirectory(subdir, results, now);
            }
        }

        private static string GetAgeBucket(int ageDays)
        {
            if (ageDays <= 180)
                return "90-180 days";
            if (ageDays <= 365)
                return "6 months - 1 year";
            return "Over 1 year";
        }
    }
}
