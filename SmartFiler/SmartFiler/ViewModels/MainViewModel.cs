using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartFiler.Data;
using SmartFiler.Services;
using WinForms = System.Windows.Forms;

namespace SmartFiler.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly FileScanner _scanner;
    private readonly SuggestionEngine _suggestionEngine;
    private readonly MoveService _moveService;
    private readonly ScanStatsRepo _statsRepo;
    private readonly DeferredFileRepo _deferredRepo;
    private readonly StaleDetector _staleDetector;
    private readonly DuplicateDetector _duplicateDetector;
    private readonly SettingsService _settings;

    public MainViewModel(
        FileScanner scanner,
        SuggestionEngine suggestionEngine,
        MoveService moveService,
        ScanStatsRepo statsRepo,
        DeferredFileRepo deferredRepo,
        StaleDetector staleDetector,
        DuplicateDetector duplicateDetector,
        SettingsService settings)
    {
        _scanner = scanner;
        _suggestionEngine = suggestionEngine;
        _moveService = moveService;
        _statsRepo = statsRepo;
        _deferredRepo = deferredRepo;
        _staleDetector = staleDetector;
        _duplicateDetector = duplicateDetector;
        _settings = settings;

        // Populate scan sources from settings
        foreach (var source in _settings.Settings.ScanSources)
            ScanSources.Add(source);

        // Load saved custom colors (or defaults from current theme)
        _customAccent = _settings.Settings.AccentColor ?? "#0EA5A9";
        _customBg = _settings.Settings.BgColor ?? "#0F0F14";
        _customSurface = _settings.Settings.SurfaceColor ?? "#18181F";
        _customText = _settings.Settings.TextColor ?? "#EAEAEF";

        // Load saved presets
        foreach (var preset in _settings.Settings.ColorPresets)
            ColorPresets.Add(preset);

        // Load saved filter states
        _showRevit = _settings.Settings.FilterRevit;
        _showBlender = _settings.Settings.FilterBlender;
        _showCad = _settings.Settings.FilterCad;
        _showWord = _settings.Settings.FilterWord;
        _showExcel = _settings.Settings.FilterExcel;
        _showPowerPoint = _settings.Settings.FilterPowerPoint;
        _showPdf = _settings.Settings.FilterPdf;
        _showText = _settings.Settings.FilterText;
        _showImages = _settings.Settings.FilterImages;
        _showShortcuts = _settings.Settings.FilterShortcuts;
        _showWebLinks = _settings.Settings.FilterWebLinks;
        _showInstallers = _settings.Settings.FilterInstallers;
        _showArchives = _settings.Settings.FilterArchives;
        _showFolders = _settings.Settings.FilterFolders;
        _showOther = _settings.Settings.FilterOther;
    }

    // ─── Observable Properties ───

    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _hasResults;
    [ObservableProperty] private string _statusText = "Ready. Click Scan Now to start.";
    [ObservableProperty] private int _selectedTabIndex;

    // Dashboard
    [ObservableProperty] private int _pendingFileCount;
    [ObservableProperty] private int _filesMovedThisMonth;
    [ObservableProperty] private long _totalBytesThisMonth;
    [ObservableProperty] private int _skippedCount;
    [ObservableProperty] private string _skippedSummary = "";

    // ─── Category Filters (all default to visible) ───
    [ObservableProperty] private bool _showRevit = true;
    [ObservableProperty] private bool _showBlender = true;
    [ObservableProperty] private bool _showCad = true;
    [ObservableProperty] private bool _showWord = true;
    [ObservableProperty] private bool _showExcel = true;
    [ObservableProperty] private bool _showPowerPoint = true;
    [ObservableProperty] private bool _showPdf = true;
    [ObservableProperty] private bool _showText = true;
    [ObservableProperty] private bool _showImages = true;
    [ObservableProperty] private bool _showShortcuts = true;
    [ObservableProperty] private bool _showWebLinks = true;
    [ObservableProperty] private bool _showInstallers = true;
    [ObservableProperty] private bool _showArchives = true;
    [ObservableProperty] private bool _showFolders = true;
    [ObservableProperty] private bool _showOther = true;

    // Triage
    public ObservableCollection<ScannedFile> ProjectMatchFiles { get; } = [];
    public ObservableCollection<ScannedFile> DownloadFiles { get; } = [];
    public ObservableCollection<ScannedFile> DeleteFiles { get; } = [];

    /// <summary>Combined filtered view of all triage files, respecting category filters.</summary>
    public ObservableCollection<ScannedFile> FilteredFiles { get; } = [];

    [ObservableProperty] private ScannedFile? _selectedFile;
    [ObservableProperty] private int _approvedCount;
    [ObservableProperty] private int _deferredCount;
    [ObservableProperty] private int _deleteCount;
    [ObservableProperty] private bool _isSelectedDeferred;

    // Last move result
    [ObservableProperty] private string _lastBatchId = "";
    [ObservableProperty] private bool _canUndo;

    // Stale files
    public ObservableCollection<StaleFileInfo> StaleFiles { get; } = [];
    [ObservableProperty] private int _staleFileCount;
    [ObservableProperty] private long _staleTotalBytes;

    // Duplicates
    public ObservableCollection<DuplicateGroup> DuplicateGroups { get; } = [];
    [ObservableProperty] private int _duplicateGroupCount;

    // Stale file age buckets
    [ObservableProperty] private int _staleCount90to180;
    [ObservableProperty] private int _staleCount6moTo1yr;
    [ObservableProperty] private int _staleCountOver1yr;
    [ObservableProperty] private long _staleBytes90to180;
    [ObservableProperty] private long _staleBytes6moTo1yr;
    [ObservableProperty] private long _staleBytesOver1yr;

    // Formatted stale sizes for display
    public string StaleSize90to180 => FormatBytes(StaleBytes90to180);
    public string StaleSize6moTo1yr => FormatBytes(StaleBytes6moTo1yr);
    public string StaleSizeOver1yr => FormatBytes(StaleBytesOver1yr);

    // Duplicate space savings
    public string DuplicateSpaceSavings => FormatBytes(DuplicateGroups.Sum(g => g.FileSize * (g.Files.Count - 1)));

    // Alternative destination (user override for selected file)
    [ObservableProperty] private string _alternativeDestination = "";

    // Alternative suggestions shown in the right-click menu on the suggested destination
    public ObservableCollection<string> AlternativeSuggestions { get; } = [];

    // ─── Location filters (one per scan source, populated after scan) ───
    public ObservableCollection<LocationFilterItem> LocationFilters { get; } = [];

    // Preview
    [ObservableProperty] private BitmapImage? _previewImage;
    [ObservableProperty] private string _previewUrl = "";
    [ObservableProperty] private string _previewDirectory = "";
    [ObservableProperty] private string _previewCreated = "";
    [ObservableProperty] private string _previewModified = "";

    // ─── Settings: Scan Sources ───

    public ObservableCollection<string> ScanSources { get; } = [];

    // ─── Settings: Custom Colors ───

    [ObservableProperty] private string _customAccent;
    [ObservableProperty] private string _customBg;
    [ObservableProperty] private string _customSurface;
    [ObservableProperty] private string _customText;

    public ObservableCollection<ColorPreset> ColorPresets { get; } = [];

    // ─── Commands ───

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        IsScanning = true;
        StatusText = "Scanning...";
        HasResults = false;

        ProjectMatchFiles.Clear();
        DownloadFiles.Clear();
        DeleteFiles.Clear();

        try
        {
            var sw = Stopwatch.StartNew();

            // Phase 1: Scan files (ScanAsync already uses Task.Run internally)
            var result = await _scanner.ScanAsync();
            StatusText = $"Classifying {result.Files.Count} files...";

            // Phase 2: Categorize
            FileCategorizer.CategorizeAll(result.Files);

            // Phase 3: Suggest destinations
            StatusText = "Matching to project folders...";
            await _suggestionEngine.SuggestDestinationsAsync(result.Files);

            sw.Stop();

            // Populate grouped collections
            foreach (var file in result.Files)
            {
                if (file.Action == FileAction.Delete)
                    DeleteFiles.Add(file);
                else if (file.MatchedProjectFolder != null)
                    ProjectMatchFiles.Add(file);
                else
                    DownloadFiles.Add(file);
            }

            PendingFileCount = result.Files.Count;

            // Rebuild location filters from distinct source directories in this scan
            foreach (var item in LocationFilters)
                item.SelectionChanged -= OnLocationFilterChanged;
            LocationFilters.Clear();

            var distinctDirs = result.Files
                .Select(f => f.SourceDirectory)
                .Where(d => !string.IsNullOrEmpty(d))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(d => d)
                .ToList();

            // Build display names — disambiguate any folders that share the same leaf name
            var leafNames = distinctDirs
                .Select(d => Path.GetFileName(d) is { Length: > 0 } n ? n : d)
                .ToList();

            for (int i = 0; i < distinctDirs.Count; i++)
            {
                var dir = distinctDirs[i];
                var leaf = leafNames[i];
                string displayName;

                if (leafNames.Count(n => n.Equals(leaf, StringComparison.OrdinalIgnoreCase)) > 1)
                {
                    // Ambiguous name — append the parent folder for context
                    var parent = Path.GetFileName(Path.GetDirectoryName(dir) ?? "");
                    displayName = string.IsNullOrEmpty(parent) ? dir : $"{leaf} ({parent})";
                }
                else
                {
                    displayName = leaf;
                }

                var item = new LocationFilterItem
                {
                    DisplayName = displayName,
                    DirectoryPath = dir
                };
                item.SelectionChanged += OnLocationFilterChanged;
                LocationFilters.Add(item);
            }

            // Build the filtered view
            RefreshFilteredFiles();
            SkippedCount = result.SkippedCount;
            SkippedSummary = result.SkippedCount > 0
                ? $"Scanned {result.Files.Count + result.SkippedCount} files. {result.SkippedCount} skipped (in use or access denied)."
                : "";

            HasResults = true;
            StatusText = $"Found {result.Files.Count} files in {sw.ElapsedMilliseconds}ms. Scanning for stale files and duplicates...";

            // Stale file scan (background, non-blocking for triage)
            StaleFiles.Clear();
            try
            {
                var staleFiles = await _staleDetector.FindStaleFilesAsync();
                foreach (var sf in staleFiles)
                    StaleFiles.Add(sf);
                StaleFileCount = staleFiles.Count;
                StaleTotalBytes = staleFiles.Sum(f => f.SizeBytes);

                // Compute age bucket counts and sizes
                var buckets = staleFiles.GroupBy(f => f.AgeBucket).ToDictionary(g => g.Key, g => g.ToList());
                StaleCount90to180 = buckets.GetValueOrDefault("90-180 days")?.Count ?? 0;
                StaleCount6moTo1yr = buckets.GetValueOrDefault("6 months - 1 year")?.Count ?? 0;
                StaleCountOver1yr = buckets.GetValueOrDefault("Over 1 year")?.Count ?? 0;
                StaleBytes90to180 = buckets.GetValueOrDefault("90-180 days")?.Sum(f => f.SizeBytes) ?? 0;
                StaleBytes6moTo1yr = buckets.GetValueOrDefault("6 months - 1 year")?.Sum(f => f.SizeBytes) ?? 0;
                StaleBytesOver1yr = buckets.GetValueOrDefault("Over 1 year")?.Sum(f => f.SizeBytes) ?? 0;
            }
            catch { /* Stale detection is best-effort */ }

            // Duplicate scan
            DuplicateGroups.Clear();
            try
            {
                var dupes = await _duplicateDetector.FindDuplicatesAsync();
                foreach (var g in dupes)
                    DuplicateGroups.Add(g);
                DuplicateGroupCount = dupes.Count;
            }
            catch { /* Duplicate detection is best-effort */ }

            StatusText = $"Found {result.Files.Count} files. {StaleFileCount} stale, {DuplicateGroupCount} duplicate groups. Review and approve.";

            // Switch to triage tab
            SelectedTabIndex = 1;

            // Refresh dashboard stats
            await RefreshDashboardAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Scan failed: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"SCAN ERROR: {ex}");
            // Show error in a message box so user can see what happened
            System.Windows.MessageBox.Show(
                $"Scan encountered an error:\n\n{ex.Message}\n\nThe app will continue working. Check the status bar for details.",
                "Smart Filer — Scan Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
        finally
        {
            IsScanning = false;
        }
    }

    private bool CanScan() => !IsScanning;

    [RelayCommand]
    private async Task ExecuteMoveAsync()
    {
        // Gather all actioned files from all three source lists
        var toProcess = ProjectMatchFiles.Concat(DownloadFiles).Concat(DeleteFiles)
            .Where(f => f.Action == FileAction.Approved || f.Action == FileAction.Delete)
            .ToList();

        if (toProcess.Count == 0)
        {
            StatusText = "No files approved or marked for deletion.";
            return;
        }

        var moveCount = toProcess.Count(f => f.Action == FileAction.Approved);
        var deleteCount = toProcess.Count(f => f.Action == FileAction.Delete);

        // Confirmation
        var confirm = System.Windows.MessageBox.Show(
            $"Ready to process {toProcess.Count} files:\n" +
            $"  Move: {moveCount} files\n" +
            $"  Delete: {deleteCount} files\n\n" +
            $"Continue?",
            "Smart Filer — Confirm",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (confirm != System.Windows.MessageBoxResult.Yes)
            return;

        IsScanning = true;
        StatusText = $"Processing {toProcess.Count} files...";

        try
        {
            var result = await _moveService.ExecuteBatchAsync(toProcess);

            LastBatchId = result.BatchId;
            CanUndo = true;

            // Remove successfully processed files from the lists
            var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in toProcess)
            {
                bool stillExists = f.IsDirectory
                    ? System.IO.Directory.Exists(f.FullPath)
                    : System.IO.File.Exists(f.FullPath);
                if (stillExists)
                    continue; // Item still exists — it wasn't moved/deleted (failed)
                processed.Add(f.FullPath);
            }

            // Remove from source collections
            foreach (var path in processed)
            {
                var pm = ProjectMatchFiles.FirstOrDefault(f => string.Equals(f.FullPath, path, StringComparison.OrdinalIgnoreCase));
                if (pm != null) ProjectMatchFiles.Remove(pm);

                var dl = DownloadFiles.FirstOrDefault(f => string.Equals(f.FullPath, path, StringComparison.OrdinalIgnoreCase));
                if (dl != null) DownloadFiles.Remove(dl);

                var del = DeleteFiles.FirstOrDefault(f => string.Equals(f.FullPath, path, StringComparison.OrdinalIgnoreCase));
                if (del != null) DeleteFiles.Remove(del);
            }

            // Refresh filtered view
            RefreshFilteredFiles();
            PendingFileCount = ProjectMatchFiles.Count + DownloadFiles.Count + DeleteFiles.Count;
            UpdateCounts();

            // Build summary
            var parts = new List<string>();
            if (result.SuccessCount > 0) parts.Add($"{result.SuccessCount} moved");
            if (result.DeletedCount > 0) parts.Add($"{result.DeletedCount} deleted");
            if (result.SkippedCount > 0) parts.Add($"{result.SkippedCount} skipped");
            if (result.Failures.Count > 0) parts.Add($"{result.Failures.Count} failed");

            var summary = string.Join(", ", parts) + ".";

            if (result.AbortedDueToFullDisk)
                summary += " ABORTED: Disk full.";

            StatusText = summary;

            // Show completion dialog
            var icon = result.Failures.Count > 0
                ? System.Windows.MessageBoxImage.Warning
                : System.Windows.MessageBoxImage.Information;

            var details = summary;
            if (result.Failures.Count > 0)
                details += "\n\nFailed files:\n" + string.Join("\n", result.Failures.Take(10));

            System.Windows.MessageBox.Show(details, "Smart Filer — Complete", System.Windows.MessageBoxButton.OK, icon);

            // Refresh dashboard
            await RefreshDashboardAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Move failed: {ex.Message}";
            System.Windows.MessageBox.Show($"Error: {ex.Message}", "Smart Filer — Error",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private async Task UndoAsync()
    {
        if (string.IsNullOrEmpty(LastBatchId)) return;

        IsScanning = true;
        StatusText = "Undoing last batch...";

        try
        {
            var result = await _moveService.UndoLastBatchAsync();
            StatusText = $"Restored {result.RestoredCount} files. {result.FailedCount} failed.";
            CanUndo = false;
            await RefreshDashboardAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Undo failed: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    /// <summary>Marks all files in the provided list as Approved (works from any action state).</summary>
    public void ApproveFiles(IList<ScannedFile> files)
    {
        foreach (var f in files)
            if (f.Action != FileAction.Approved) f.Action = FileAction.Approved;
        UpdateCounts();
        RefreshFilteredFiles();
    }

    [RelayCommand]
    private void ApproveSelected()
    {
        if (SelectedFile != null && SelectedFile.Action != FileAction.Approved)
        {
            SelectedFile.Action = FileAction.Approved;
            UpdateCounts();
            RefreshFilteredFiles();
        }
    }

    /// <summary>Toggles defer on all files in the provided list.
    /// Files already Deferred are returned to Pending; all others are set to Deferred.
    /// UI is updated synchronously; DB persistence is fire-and-forget.</summary>
    public void DeferFiles(IList<ScannedFile> files)
    {
        // 1. Change all actions synchronously
        var toRemoveFromDb = new List<string>();
        var toAddToDb = new List<string>();

        foreach (var f in files)
        {
            if (f.Action == FileAction.Deferred)
            {
                f.Action = FileAction.Pending;
                toRemoveFromDb.Add(f.FullPath);
            }
            else
            {
                f.Action = FileAction.Deferred;
                toAddToDb.Add(f.FullPath);
            }
        }

        // 2. Force full UI rebuild so icons update
        UpdateCounts();
        RefreshFilteredFiles();

        // 3. Persist to DB in background (best-effort)
        _ = Task.Run(async () =>
        {
            foreach (var path in toRemoveFromDb)
                try { await _deferredRepo.RemoveAsync(path); } catch { }
            foreach (var path in toAddToDb)
                try { await _deferredRepo.AddAsync(path); } catch { }
        });
    }

    [RelayCommand]
    private void DeferSelected()
    {
        if (SelectedFile != null)
            DeferFiles([SelectedFile]);
    }

    /// <summary>Marks all files in the provided list (or just SelectedFile) as Delete.</summary>
    public void MarkFilesForDeletion(IList<ScannedFile> files)
    {
        foreach (var f in files)
            f.Action = FileAction.Delete;
        UpdateCounts();
        RefreshFilteredFiles();
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (SelectedFile != null)
        {
            SelectedFile.Action = FileAction.Delete;
            UpdateCounts();
            RefreshFilteredFiles();
        }
    }

    /// <summary>Resets all files in the provided list back to Pending (clears Approve/Defer/Delete).</summary>
    public void ResetFiles(IList<ScannedFile> files)
    {
        var toRemoveFromDb = new List<string>();
        foreach (var f in files)
        {
            if (f.Action == FileAction.Deferred)
                toRemoveFromDb.Add(f.FullPath);
            f.Action = FileAction.Pending;
        }

        UpdateCounts();
        RefreshFilteredFiles();

        _ = Task.Run(async () =>
        {
            foreach (var path in toRemoveFromDb)
                try { await _deferredRepo.RemoveAsync(path); } catch { }
        });
    }

    [RelayCommand]
    private void ApproveAll()
    {
        foreach (var f in ProjectMatchFiles.Concat(DownloadFiles))
            if (f.Action == FileAction.Pending) f.Action = FileAction.Approved;
        foreach (var f in DeleteFiles)
            if (f.Action == FileAction.Pending) f.Action = FileAction.Delete;
        UpdateCounts();
    }

    [RelayCommand]
    private void SelectAllFilters()
    {
        ShowRevit = ShowBlender = ShowCad = ShowWord = ShowExcel = ShowPowerPoint =
        ShowPdf = ShowText = ShowImages = ShowShortcuts = ShowWebLinks =
        ShowInstallers = ShowArchives = ShowFolders = ShowOther = true;
    }

    [RelayCommand]
    private void UnselectAllFilters()
    {
        ShowRevit = ShowBlender = ShowCad = ShowWord = ShowExcel = ShowPowerPoint =
        ShowPdf = ShowText = ShowImages = ShowShortcuts = ShowWebLinks =
        ShowInstallers = ShowArchives = ShowFolders = ShowOther = false;
    }

    // ─── Settings Commands ───

    [RelayCommand]
    private void BrowseAlternative()
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "Select alternative destination folder",
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() == WinForms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            AlternativeDestination = dialog.SelectedPath;
            if (SelectedFile != null)
            {
                SelectedFile.AlternativeDestination = dialog.SelectedPath;
                SelectedFile.Action = FileAction.Approved;
                UpdateCounts();
                RefreshView();
            }
        }
    }

    [RelayCommand]
    private void AddSource()
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "Select a folder to scan",
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() == WinForms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            if (!ScanSources.Contains(dialog.SelectedPath))
            {
                ScanSources.Add(dialog.SelectedPath);
                _settings.Settings.ScanSources = [.. ScanSources];
                _settings.Save();
            }
        }
    }

    [RelayCommand]
    private void RemoveSource(string path)
    {
        ScanSources.Remove(path);
        _settings.Settings.ScanSources = [.. ScanSources];
        _settings.Save();
    }

    [RelayCommand]
    private void ApplyColors()
    {
        try
        {
            var accent = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(CustomAccent);
            var bg = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(CustomBg);
            var surface = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(CustomSurface);
            var text = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(CustomText);

            var dict = new ResourceDictionary();

            // Colors
            dict["AccentColor"] = accent;
            dict["BgColor"] = bg;
            dict["SurfaceColor"] = surface;
            dict["TextColor"] = text;

            // Brushes
            dict["AccentBrush"] = new SolidColorBrush(accent);
            dict["BgBrush"] = new SolidColorBrush(bg);
            dict["SurfaceBrush"] = new SolidColorBrush(surface);
            dict["TextBrush"] = new SolidColorBrush(text);

            // Replace the first merged dictionary (Colors.xaml)
            var merged = System.Windows.Application.Current.Resources.MergedDictionaries;
            if (merged.Count > 0)
                merged.RemoveAt(0);
            merged.Insert(0, dict);

            // Save to settings
            _settings.Settings.AccentColor = CustomAccent;
            _settings.Settings.BgColor = CustomBg;
            _settings.Settings.SurfaceColor = CustomSurface;
            _settings.Settings.TextColor = CustomText;
            _settings.Save();

            StatusText = "Custom colors applied.";
        }
        catch (Exception ex)
        {
            StatusText = $"Invalid color value: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ResetColors()
    {
        // Clear custom colors from settings
        _settings.Settings.AccentColor = null;
        _settings.Settings.BgColor = null;
        _settings.Settings.SurfaceColor = null;
        _settings.Settings.TextColor = null;
        _settings.Save();

        // Reload the theme file (restores defaults)
        ThemeManager.LoadSavedTheme();

        // Update UI fields to match defaults
        CustomAccent = "#0EA5A9";
        CustomBg = ThemeManager.CurrentTheme == ThemeManager.Theme.Dark ? "#0F0F14" : "#F8F8FC";
        CustomSurface = ThemeManager.CurrentTheme == ThemeManager.Theme.Dark ? "#18181F" : "#FFFFFF";
        CustomText = ThemeManager.CurrentTheme == ThemeManager.Theme.Dark ? "#EAEAEF" : "#1A1A2E";

        StatusText = "Colors reset to theme defaults.";
    }

    [RelayCommand]
    private void SavePreset()
    {
        var name = Microsoft.VisualBasic.Interaction.InputBox(
            "Enter a name for this color preset:", "Save Preset", "My Preset");

        if (string.IsNullOrWhiteSpace(name)) return;

        var preset = new ColorPreset
        {
            Name = name,
            AccentColor = CustomAccent,
            BgColor = CustomBg,
            SurfaceColor = CustomSurface,
            TextColor = CustomText
        };

        ColorPresets.Add(preset);
        _settings.Settings.ColorPresets = [.. ColorPresets];
        _settings.Save();

        StatusText = $"Preset '{name}' saved.";
    }

    [RelayCommand]
    private void LoadPreset(ColorPreset preset)
    {
        CustomAccent = preset.AccentColor;
        CustomBg = preset.BgColor;
        CustomSurface = preset.SurfaceColor;
        CustomText = preset.TextColor;
        ApplyColors();

        StatusText = $"Loaded preset '{preset.Name}'.";
    }

    [RelayCommand]
    private void DeletePreset(ColorPreset preset)
    {
        ColorPresets.Remove(preset);
        _settings.Settings.ColorPresets = [.. ColorPresets];
        _settings.Save();
    }

    partial void OnAlternativeDestinationChanged(string value)
    {
        if (SelectedFile != null && !string.IsNullOrWhiteSpace(value))
        {
            SelectedFile.AlternativeDestination = value;
            // Auto-approve: if you set a destination, you intend to move the file
            if (SelectedFile.Action == FileAction.Pending)
            {
                SelectedFile.Action = FileAction.Approved;
                UpdateCounts();
                RefreshView();
            }
        }
    }

    /// <summary>
    /// Applies an alternative suggestion as the destination for the selected file.
    /// </summary>
    [RelayCommand]
    private void SelectAlternativeSuggestion(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        AlternativeDestination = path;
    }

    partial void OnSelectedFileChanged(ScannedFile? value)
    {
        PreviewImage = null;
        PreviewUrl = "";
        PreviewDirectory = "";
        PreviewCreated = "";
        PreviewModified = "";
        AlternativeDestination = "";
        AlternativeSuggestions.Clear();
        IsSelectedDeferred = value?.Action == FileAction.Deferred;

        if (value == null) return;

        // Load existing alternative destination if set
        AlternativeDestination = value.AlternativeDestination ?? "";

        if (!value.IsDirectory)
        {
            // Load thumbnail for images
            PreviewImage = PreviewService.LoadThumbnail(value.FullPath);

            // Parse URL for .url files
            PreviewUrl = PreviewService.ParseUrlShortcut(value.FullPath) ?? "";

            // Load file properties
            var props = PreviewService.GetProperties(value.FullPath);
            if (props != null)
            {
                PreviewDirectory = props.Directory;
                PreviewCreated = props.Created.ToString("yyyy-MM-dd HH:mm");
                PreviewModified = props.Modified.ToString("yyyy-MM-dd HH:mm");
            }
        }
        else
        {
            // For folders, show the parent directory and modification time
            PreviewDirectory = System.IO.Path.GetDirectoryName(value.FullPath) ?? "";
            PreviewModified = value.LastModified.ToString("yyyy-MM-dd HH:mm");
        }

        // Load alternative suggestions in the background
        _ = LoadAlternativeSuggestionsAsync(value);
    }

    private async Task LoadAlternativeSuggestionsAsync(ScannedFile file)
    {
        try
        {
            var alternatives = await _suggestionEngine.GetAlternativeSuggestionsAsync(file);
            AlternativeSuggestions.Clear();
            foreach (var alt in alternatives)
                AlternativeSuggestions.Add(alt);
        }
        catch { /* Best-effort */ }
    }

    // ─── Filter change handlers ───

    private void OnLocationFilterChanged(object? sender, EventArgs e) => RefreshFilteredFiles();

    [RelayCommand]
    private void SelectAllLocations()
    {
        foreach (var item in LocationFilters)
            item.IsSelected = true;
    }

    [RelayCommand]
    private void UnselectAllLocations()
    {
        foreach (var item in LocationFilters)
            item.IsSelected = false;
    }

    partial void OnShowRevitChanged(bool value) { _settings.Settings.FilterRevit = value; SaveFilters(); RefreshFilteredFiles(); }
    partial void OnShowBlenderChanged(bool value) { _settings.Settings.FilterBlender = value; SaveFilters(); RefreshFilteredFiles(); }
    partial void OnShowCadChanged(bool value) { _settings.Settings.FilterCad = value; SaveFilters(); RefreshFilteredFiles(); }
    partial void OnShowWordChanged(bool value) { _settings.Settings.FilterWord = value; SaveFilters(); RefreshFilteredFiles(); }
    partial void OnShowExcelChanged(bool value) { _settings.Settings.FilterExcel = value; SaveFilters(); RefreshFilteredFiles(); }
    partial void OnShowPowerPointChanged(bool value) { _settings.Settings.FilterPowerPoint = value; SaveFilters(); RefreshFilteredFiles(); }
    partial void OnShowPdfChanged(bool value) { _settings.Settings.FilterPdf = value; SaveFilters(); RefreshFilteredFiles(); }
    partial void OnShowTextChanged(bool value) { _settings.Settings.FilterText = value; SaveFilters(); RefreshFilteredFiles(); }
    partial void OnShowImagesChanged(bool value) { _settings.Settings.FilterImages = value; SaveFilters(); RefreshFilteredFiles(); }
    partial void OnShowShortcutsChanged(bool value) { _settings.Settings.FilterShortcuts = value; SaveFilters(); RefreshFilteredFiles(); }
    partial void OnShowWebLinksChanged(bool value) { _settings.Settings.FilterWebLinks = value; SaveFilters(); RefreshFilteredFiles(); }
    partial void OnShowInstallersChanged(bool value) { _settings.Settings.FilterInstallers = value; SaveFilters(); RefreshFilteredFiles(); }
    partial void OnShowArchivesChanged(bool value) { _settings.Settings.FilterArchives = value; SaveFilters(); RefreshFilteredFiles(); }
    partial void OnShowFoldersChanged(bool value) { _settings.Settings.FilterFolders = value; SaveFilters(); RefreshFilteredFiles(); }
    partial void OnShowOtherChanged(bool value) { _settings.Settings.FilterOther = value; SaveFilters(); RefreshFilteredFiles(); }

    private void SaveFilters() => _settings.Save();

    // ─── Helpers ───

    /// <summary>Forces the ListView to re-render a specific item by removing and re-inserting it.
    /// This guarantees the converter re-evaluates (works around WPF binding not seeing property changes).</summary>
    private void NotifyFileChanged(ScannedFile file)
    {
        var idx = FilteredFiles.IndexOf(file);
        if (idx >= 0)
        {
            FilteredFiles.RemoveAt(idx);
            FilteredFiles.Insert(idx, file);
        }
    }

    /// <summary>Lightweight refresh — just tells the ListView to re-render without rebuilding the list.
    /// Preserves multi-selection. Use after action changes (approve/delete/defer).</summary>
    private void RefreshView()
    {
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(FilteredFiles);
        view?.Refresh();
    }

    /// <summary>Full rebuild — clears and repopulates FilteredFiles from source collections.
    /// Destroys selection. Use only when filter checkboxes change or after scan/move.</summary>
    private void RefreshFilteredFiles()
    {
        var savedSelection = SelectedFile;

        FilteredFiles.Clear();
        var all = ProjectMatchFiles.Concat(DownloadFiles).Concat(DeleteFiles);
        foreach (var f in all)
        {
            if (PassesFilter(f))
                FilteredFiles.Add(f);
        }

        if (savedSelection != null && FilteredFiles.Contains(savedSelection))
            SelectedFile = savedSelection;

        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(FilteredFiles);
        view?.Refresh();
    }

    private bool PassesFilter(ScannedFile f)
    {
        // Location filter — if any items exist, the file's directory must be selected
        if (LocationFilters.Count > 0)
        {
            var match = LocationFilters.FirstOrDefault(l =>
                string.Equals(l.DirectoryPath, f.SourceDirectory, StringComparison.OrdinalIgnoreCase));
            if (match != null && !match.IsSelected)
                return false;
        }

        return f.Category switch
        {
            FileCategory.RevitProject or FileCategory.RevitBackup or
            FileCategory.RevitFamily or FileCategory.RfaBackup or
            FileCategory.RevitTemplate => ShowRevit,

            FileCategory.Blender or FileCategory.BlenderBackup => ShowBlender,

            FileCategory.AutoCad or FileCategory.AutoCadBackup or
            FileCategory.FreeCad or FileCategory.FreeCadBackup => ShowCad,

            FileCategory.MsWord => ShowWord,
            FileCategory.MsExcel => ShowExcel,
            FileCategory.MsPowerPoint => ShowPowerPoint,
            FileCategory.Pdf => ShowPdf,
            FileCategory.TextFile => ShowText,

            FileCategory.Image => ShowImages,
            FileCategory.Shortcut => ShowShortcuts,
            FileCategory.WebLink => ShowWebLinks,

            FileCategory.Installer or FileCategory.Driver => ShowInstallers,
            FileCategory.Archive => ShowArchives,
            FileCategory.Folder => ShowFolders,

            FileCategory.Rhino or FileCategory.Plasticity or
            FileCategory.ThreeDInterchange or FileCategory.Other => ShowOther,

            _ => true
        };
    }

    public void UpdateCounts()
    {
        var all = ProjectMatchFiles.Concat(DownloadFiles).Concat(DeleteFiles).ToList();
        ApprovedCount = all.Count(f => f.Action == FileAction.Approved);
        DeferredCount = all.Count(f => f.Action == FileAction.Deferred);
        DeleteCount = all.Count(f => f.Action == FileAction.Delete);
        IsSelectedDeferred = SelectedFile?.Action == FileAction.Deferred;
        RefreshView();
    }

    partial void OnDuplicateGroupCountChanged(int value) => OnPropertyChanged(nameof(DuplicateSpaceSavings));
    partial void OnStaleBytes90to180Changed(long value) => OnPropertyChanged(nameof(StaleSize90to180));
    partial void OnStaleBytes6moTo1yrChanged(long value) => OnPropertyChanged(nameof(StaleSize6moTo1yr));
    partial void OnStaleBytesOver1yrChanged(long value) => OnPropertyChanged(nameof(StaleSizeOver1yr));

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
    }

    private async Task RefreshDashboardAsync()
    {
        try
        {
            var (totalMoved, totalBytes) = await _statsRepo.GetMonthTotalsAsync();
            FilesMovedThisMonth = totalMoved;
            TotalBytesThisMonth = totalBytes;
        }
        catch
        {
            // Dashboard stats are best-effort
        }
    }
}
