using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using SmartFiler.Data;

namespace SmartFiler.Services
{
    /// <summary>
    /// Enumerates files in configured source directories and produces a <see cref="ScanResult"/>.
    /// Files are assigned <see cref="FileCategory.Other"/> initially; run
    /// <see cref="FileCategorizer.CategorizeAll"/> on the result to classify them.
    /// </summary>
    public sealed class FileScanner
    {
        private readonly IReadOnlyList<string> _sourceDirectories;

        /// <summary>
        /// Creates a scanner that will enumerate files in the given directories.
        /// If no directories are supplied, defaults to the current user's Downloads, Desktop, and Documents folders.
        /// </summary>
        public FileScanner(IEnumerable<string>? sourceDirectories = null)
        {
            var dirs = sourceDirectories?.ToList() ?? new List<string>
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            // Deduplicate directories that resolve to the same physical path
            // (e.g., Desktop and OneDrive\Desktop when Desktop is redirected)
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unique = new List<string>();
            foreach (var dir in dirs)
            {
                if (!Directory.Exists(dir)) continue;
                string resolved;
                try { resolved = Path.GetFullPath(dir); } catch { resolved = dir; }
                if (seen.Add(resolved))
                    unique.Add(dir);
            }
            _sourceDirectories = unique.AsReadOnly();
        }

        /// <summary>
        /// Scans all source directories (top-level only) and returns a <see cref="ScanResult"/>
        /// containing every accessible file wrapped as a <see cref="ScannedFile"/>.
        /// Inaccessible files are counted and their reasons recorded in
        /// <see cref="ScanResult.SkippedReasons"/>.
        /// </summary>
        public Task<ScanResult> ScanAsync()
        {
            return Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                var files = new List<ScannedFile>();
                var skippedReasons = new List<string>();
                int skippedCount = 0;

                foreach (var directory in _sourceDirectories)
                {
                    if (!Directory.Exists(directory))
                        continue;

                    IEnumerable<string> entries;
                    try
                    {
                        entries = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // System folder — skip silently
                        skippedCount++;
                        continue;
                    }
                    catch (IOException ex)
                    {
                        skippedCount++;
                        skippedReasons.Add($"Cannot access directory: {directory} ({ex.Message})");
                        continue;
                    }

                    foreach (var filePath in entries)
                    {
                        try
                        {
                            var info = new FileInfo(filePath);

                            files.Add(new ScannedFile
                            {
                                FullPath = info.FullName,
                                FileName = info.Name,
                                Extension = info.Extension.ToLowerInvariant(),
                                Category = FileCategory.Other,
                                SizeBytes = info.Length,
                                LastModified = info.LastWriteTime,
                                IsLocked = false
                            });
                        }
                        catch (PathTooLongException)
                        {
                            skippedCount++;
                            skippedReasons.Add($"Path too long: {Path.GetFileName(filePath)}");
                        }
                        catch (IOException)
                        {
                            skippedCount++;
                            skippedReasons.Add($"File in use: {Path.GetFileName(filePath)}");
                        }
                        catch (UnauthorizedAccessException)
                        {
                            // System file — skip silently
                            skippedCount++;
                        }
                    }
                }

                sw.Stop();

                // Deduplicate by resolved full path (OneDrive redirects can cause the same
                // physical file to appear via multiple scan source paths)
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var dedupedFiles = new List<ScannedFile>();
                foreach (var f in files)
                {
                    // Resolve to actual path to catch symlinks and OneDrive redirects
                    var key = f.FullPath;
                    try { key = Path.GetFullPath(f.FullPath); } catch { }

                    if (seen.Add(key))
                        dedupedFiles.Add(f);
                }

                return new ScanResult
                {
                    Files = dedupedFiles,
                    SkippedCount = skippedCount,
                    SkippedReasons = skippedReasons,
                    ScanDuration = sw.Elapsed
                };
            });
        }
    }
}
