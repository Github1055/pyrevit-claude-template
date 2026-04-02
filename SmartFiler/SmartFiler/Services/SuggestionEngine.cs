using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SmartFiler.Data;

namespace SmartFiler.Services
{
    /// <summary>
    /// Assigns a suggested destination and action to each scanned file
    /// using fuzzy project matching, move history, and category defaults.
    /// </summary>
    public sealed class SuggestionEngine
    {
        private static readonly Dictionary<FileCategory, string> CategoryDefaults = new()
        {
            // --- Revit (CPL office resources) ---
            { FileCategory.RevitProject,      @"D:\D CPL OFFICE\05 REVIT\000_Reference Projects" },
            { FileCategory.RevitFamily,       @"D:\D CPL OFFICE\05 REVIT\000_Revit Families" },
            { FileCategory.RevitTemplate,     @"D:\D CPL OFFICE\05 REVIT\000_ Revit Template" },

            // --- 3D / CAD applications ---
            { FileCategory.Blender,           @"D:\D Blender3D\_My Projects" },
            { FileCategory.AutoCad,           @"D:\D Autocad" },
            { FileCategory.AutoCadBackup,     @"D:\D Autocad\Backups" },
            { FileCategory.Rhino,             @"D:\D Rhino3D" },
            { FileCategory.Plasticity,        @"D:\D Plasticity3D" },
            { FileCategory.FreeCad,           @"D:\D Freecad" },
            { FileCategory.ThreeDInterchange, @"D:\D Blender3D\Downloads" },

            // --- Documents & media ---
            { FileCategory.MsWord,            @"D:\Downloads Utils\Docs" },
            { FileCategory.MsExcel,           @"D:\Downloads Utils\Docs" },
            { FileCategory.MsPowerPoint,      @"D:\Power Point" },
            { FileCategory.Pdf,               @"D:\Downloads Utils\Docs" },
            { FileCategory.TextFile,          @"D:\Downloads Utils\Docs" },
            { FileCategory.Image,             @"D:\Downloads Utils\Images" },
            { FileCategory.WebLink,           @"D:\Downloads Utils\Links" },

            // --- Installers & system ---
            { FileCategory.Installer,         @"D:\Downloads Apps" },
            { FileCategory.Driver,            @"D:\Downloads Drivers" },
            { FileCategory.Archive,           @"D:\Downloads Utils\Archives" },
            { FileCategory.Shortcut,          @"D:\Downloads Utils\Shortcuts" },
            { FileCategory.Folder,            @"D:\" },
            { FileCategory.Other,             @"D:\Downloads Utils\Other" }
        };

        private static readonly HashSet<FileCategory> BackupCategories = new()
        {
            FileCategory.RevitBackup,
            FileCategory.RfaBackup,
            FileCategory.BlenderBackup,
            FileCategory.AutoCadBackup,
            FileCategory.FreeCadBackup
        };

        private static readonly HashSet<FileCategory> RevitCategories = new()
        {
            FileCategory.RevitProject,
            FileCategory.RevitFamily,
            FileCategory.RevitTemplate,
            FileCategory.RfaBackup
        };

        private readonly FuzzyMatcher _fuzzyMatcher;
        private readonly ProjectIndexer _projectIndexer;
        private readonly MoveHistoryRepo _moveHistoryRepo;
        private readonly DeferredFileRepo _deferredFileRepo;
        private readonly RevitLibraryMatcher _revitMatcher;

        public SuggestionEngine(
            FuzzyMatcher fuzzyMatcher,
            ProjectIndexer projectIndexer,
            MoveHistoryRepo moveHistoryRepo,
            DeferredFileRepo deferredFileRepo,
            RevitLibraryMatcher revitMatcher)
        {
            _fuzzyMatcher = fuzzyMatcher;
            _projectIndexer = projectIndexer;
            _moveHistoryRepo = moveHistoryRepo;
            _deferredFileRepo = deferredFileRepo;
            _revitMatcher = revitMatcher;
        }

        /// <summary>
        /// Populates <see cref="ScannedFile.SuggestedDestination"/>,
        /// <see cref="ScannedFile.Action"/>, <see cref="ScannedFile.MatchConfidence"/>,
        /// and <see cref="ScannedFile.MatchedProjectFolder"/> on each file in the list.
        /// </summary>
        /// <param name="files">The files to suggest destinations for. Modified in place.</param>
        public async Task SuggestDestinationsAsync(List<ScannedFile> files)
        {
            var projectFolders = await _projectIndexer.GetProjectFoldersAsync();
            var deferredPaths = await _deferredFileRepo.GetAllPathsAsync();
            var deferredSet = new HashSet<string>(deferredPaths, StringComparer.OrdinalIgnoreCase);

            // Pre-load extension frequency maps for all unique extensions in the batch
            var extensions = files.Select(f => f.Extension).Where(e => !string.IsNullOrEmpty(e)).Distinct().ToList();
            var extensionFrequency = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var ext in extensions)
            {
                var freqMap = await _moveHistoryRepo.GetDestinationFrequencyAsync(ext);
                extensionFrequency[ext] = freqMap.Count > 0
                    ? freqMap.OrderByDescending(kv => kv.Value).First().Key
                    : null;
            }

            for (int i = 0; i < files.Count; i++)
            {
                files[i] = await SuggestForFileAsync(files[i], projectFolders, deferredSet, extensionFrequency);
            }
        }

        /// <summary>
        /// Returns up to <paramref name="count"/> alternative destination paths for a file,
        /// excluding the file's current <see cref="ScannedFile.SuggestedDestination"/>.
        /// Draws from fuzzy project folder matches ranked by score, then category-default paths.
        /// </summary>
        public async Task<List<string>> GetAlternativeSuggestionsAsync(ScannedFile file, int count = 3)
        {
            var projectFolders = await _projectIndexer.GetProjectFoldersAsync();
            var primary = file.SuggestedDestination;

            // Get top fuzzy matches (use a low threshold to surface alternatives)
            var topMatches = await _fuzzyMatcher.FindTopMatchesAsync(file.FileName, projectFolders, count + 1, threshold: 0.1);

            var alternatives = topMatches
                .Select(m => m.FolderPath)
                .Where(p => !string.Equals(p, primary, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Pad with category-default fallback if we don't have enough
            if (alternatives.Count < count && CategoryDefaults.TryGetValue(file.Category, out var defaultDest))
            {
                if (!string.Equals(defaultDest, primary, StringComparison.OrdinalIgnoreCase)
                    && !alternatives.Any(a => string.Equals(a, defaultDest, StringComparison.OrdinalIgnoreCase)))
                {
                    alternatives.Add(defaultDest);
                }
            }

            return alternatives.Take(count).ToList();
        }

        /// <summary>
        /// If Dest2 or Dest3 are still null, fills them from move history or category defaults
        /// (skipping any paths already used by a higher-priority destination slot).
        /// </summary>
        private static void FillDestFallbacks(ScannedFile file, Dictionary<string, string?> extensionFrequency)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                file.SuggestedDestination ?? ""
            };
            if (file.SuggestedDestination2 is not null) used.Add(file.SuggestedDestination2);

            if (file.SuggestedDestination2 is null || file.SuggestedDestination3 is null)
            {
                // Try move history
                string? historyDest = null;
                if (!string.IsNullOrEmpty(file.Extension) && extensionFrequency.TryGetValue(file.Extension, out var hd))
                    historyDest = hd;

                if (historyDest is not null && !used.Contains(historyDest))
                {
                    if (file.SuggestedDestination2 is null)
                    {
                        file.SuggestedDestination2 = historyDest;
                        used.Add(historyDest);
                    }
                    else if (file.SuggestedDestination3 is null)
                    {
                        file.SuggestedDestination3 = historyDest;
                        used.Add(historyDest);
                    }
                }

                // Try category default
                if ((file.SuggestedDestination2 is null || file.SuggestedDestination3 is null)
                    && CategoryDefaults.TryGetValue(file.Category, out var catDefault)
                    && catDefault is not null && !used.Contains(catDefault))
                {
                    if (file.SuggestedDestination2 is null)
                        file.SuggestedDestination2 = catDefault;
                    else if (file.SuggestedDestination3 is null)
                        file.SuggestedDestination3 = catDefault;
                }
            }
        }

        /// <summary>
        /// Determines the suggestion for a single file.
        /// </summary>
        private async Task<ScannedFile> SuggestForFileAsync(
            ScannedFile file,
            List<ProjectFolder> projectFolders,
            HashSet<string> deferredSet,
            Dictionary<string, string?> extensionFrequency)
        {
            // Deferred files keep their deferred status
            if (deferredSet.Contains(file.FullPath))
            {
                file.Action = FileAction.Deferred;
                return file;
            }

            // Backup categories are suggested for deletion
            if (BackupCategories.Contains(file.Category))
            {
                file.SuggestedDestination = null;
                file.Action = FileAction.Delete;
                file.MatchConfidence = 1.0;
                return file;
            }

            // For Revit files: try deep matching against the Revit library tree first
            if (RevitCategories.Contains(file.Category))
            {
                try
                {
                    var revitResult = await _revitMatcher.FindBestFolderAsync(file);
                    if (revitResult.FolderPath is not null && revitResult.Score > 0)
                    {
                        var dest = Path.Combine(revitResult.FolderPath, file.FileName);
                        file.SuggestedDestination = dest;
                        file.MatchConfidence = revitResult.Score;
                        file.MatchedProjectFolder = Path.GetFileName(revitResult.FolderPath);

                        // Dest2 and Dest3: use fuzzy matching for Revit files
                        var revitFuzzy = await _fuzzyMatcher.FindTopMatchesAsync(file.FileName, projectFolders, 3, threshold: 0.1);
                        var revitAlts = revitFuzzy
                            .Select(m => m.FolderPath)
                            .Where(p => !string.Equals(p, dest, StringComparison.OrdinalIgnoreCase))
                            .ToList();
                        file.SuggestedDestination2 = revitAlts.Count > 0 ? revitAlts[0] : null;
                        file.SuggestedDestination3 = revitAlts.Count > 1 ? revitAlts[1] : null;
                        FillDestFallbacks(file, extensionFrequency);
                        return file;
                    }
                }
                catch { /* Revit library matching is best-effort */ }
            }

            // Try fuzzy matching against general project folders on D:
            // Get top 3 matches so we can populate Dest1, Dest2, Dest3
            var topMatches = await _fuzzyMatcher.FindTopMatchesAsync(file.FileName, projectFolders, 3, threshold: 0.1);

            if (topMatches.Count > 0 && topMatches[0].Score >= 0.4)
            {
                // Top match meets the default threshold — use as Dest1
                file.SuggestedDestination = topMatches[0].FolderPath;
                file.MatchConfidence = topMatches[0].Score;
                file.MatchedProjectFolder = Path.GetFileName(topMatches[0].FolderPath);
                file.SuggestedDestination2 = topMatches.Count > 1 ? topMatches[1].FolderPath : null;
                file.SuggestedDestination3 = topMatches.Count > 2 ? topMatches[2].FolderPath : null;
                FillDestFallbacks(file, extensionFrequency);
                return file;
            }

            // Fall back to move history frequency for this extension
            if (!string.IsNullOrEmpty(file.Extension)
                && extensionFrequency.TryGetValue(file.Extension, out var historyDest)
                && historyDest is not null)
            {
                file.SuggestedDestination = historyDest;
                file.MatchConfidence = 0.5;
                // Dest2/3 from fuzzy matches (low threshold)
                file.SuggestedDestination2 = topMatches.Count > 0 ? topMatches[0].FolderPath : null;
                file.SuggestedDestination3 = topMatches.Count > 1 ? topMatches[1].FolderPath : null;
                FillDestFallbacks(file, extensionFrequency);
                return file;
            }

            // Fall back to category default destination
            if (CategoryDefaults.TryGetValue(file.Category, out var defaultDest))
            {
                file.SuggestedDestination = defaultDest;
                file.MatchConfidence = 0.3;
                file.SuggestedDestination2 = topMatches.Count > 0 ? topMatches[0].FolderPath : null;
                file.SuggestedDestination3 = topMatches.Count > 1 ? topMatches[1].FolderPath : null;
                return file;
            }

            return file;
        }
    }
}
