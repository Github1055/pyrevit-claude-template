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
            { FileCategory.RevitProject,      @"D:\D CPL OFFICE\05 REVIT\000_Reference Projects" },
            { FileCategory.RevitFamily,       @"D:\D CPL OFFICE\05 REVIT\000_Revit Families" },
            { FileCategory.RevitTemplate,     @"D:\D CPL OFFICE\05 REVIT\000_ Revit Template" },
            { FileCategory.Blender,           @"D:\D Blender3D" },
            { FileCategory.AutoCad,           @"D:\D Autocad" },
            { FileCategory.AutoCadBackup,     @"D:\D Autocad\Backups" },
            { FileCategory.Rhino,             @"D:\D Rhino3D" },
            { FileCategory.Plasticity,        @"D:\D Plasticity3D" },
            { FileCategory.ThreeDInterchange, @"D:\D Blender3D\Imports" },
            { FileCategory.MsWord,            @"D:\Downloads Utils\Docs" },
            { FileCategory.MsExcel,           @"D:\Downloads Utils\Docs" },
            { FileCategory.MsPowerPoint,      @"D:\Downloads Utils\Docs" },
            { FileCategory.Pdf,               @"D:\Downloads Utils\Docs" },
            { FileCategory.TextFile,          @"D:\Downloads Utils\Docs" },
            { FileCategory.Image,             @"D:\Downloads Utils\Images" },
            { FileCategory.WebLink,           @"D:\Downloads Utils\Links" },
            { FileCategory.Installer,         @"D:\Downloads Apps" },
            { FileCategory.Driver,            @"D:\Downloads Drivers" },
            { FileCategory.Archive,           @"D:\Downloads Utils\Archives" },
            { FileCategory.Shortcut,          @"D:\Downloads Utils\Shortcuts" },
            { FileCategory.Other,             @"D:\Downloads Utils\Other" }
        };

        private static readonly HashSet<FileCategory> BackupCategories = new()
        {
            FileCategory.RevitBackup,
            FileCategory.RfaBackup,
            FileCategory.BlenderBackup,
            FileCategory.AutoCadBackup
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
                        return file;
                    }
                }
                catch { /* Revit library matching is best-effort */ }
            }

            // Try fuzzy matching against general project folders on D:
            var fuzzyResult = await _fuzzyMatcher.FindBestMatchAsync(file.FileName, projectFolders);
            if (fuzzyResult.FolderPath is not null && fuzzyResult.Score > 0)
            {
                file.SuggestedDestination = fuzzyResult.FolderPath;
                file.MatchConfidence = fuzzyResult.Score;
                file.MatchedProjectFolder = Path.GetFileName(fuzzyResult.FolderPath);
                return file;
            }

            // Fall back to move history frequency for this extension
            if (!string.IsNullOrEmpty(file.Extension)
                && extensionFrequency.TryGetValue(file.Extension, out var historyDest)
                && historyDest is not null)
            {
                file.SuggestedDestination = historyDest;
                file.MatchConfidence = 0.5;
                return file;
            }

            // Fall back to category default destination
            if (CategoryDefaults.TryGetValue(file.Category, out var defaultDest))
            {
                file.SuggestedDestination = defaultDest;
                file.MatchConfidence = 0.3;
                return file;
            }

            return file;
        }
    }
}
