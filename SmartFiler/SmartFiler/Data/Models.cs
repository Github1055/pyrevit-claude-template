using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace SmartFiler.Data
{
    /// <summary>
    /// Broad file type categories used for classification and routing.
    /// </summary>
    public enum FileCategory
    {
        RevitProject,
        RevitBackup,
        RevitFamily,
        RfaBackup,
        RevitTemplate,
        Blender,
        BlenderBackup,
        AutoCad,
        AutoCadBackup,
        Rhino,
        Plasticity,
        ThreeDInterchange,
        MsWord,
        MsExcel,
        MsPowerPoint,
        Pdf,
        TextFile,
        Image,
        WebLink,
        Installer,
        Driver,
        Archive,
        Shortcut,
        Other
    }

    /// <summary>
    /// Disposition action for a scanned file.
    /// </summary>
    public enum FileAction
    {
        Pending,
        Approved,
        Deferred,
        Delete
    }

    /// <summary>
    /// A file discovered during a scan, with classification and suggested destination.
    /// </summary>
    public class ScannedFile : INotifyPropertyChanged
    {
        public string FullPath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string Extension { get; set; } = string.Empty;
        public FileCategory Category { get; set; }
        public long SizeBytes { get; set; }
        public DateTime LastModified { get; set; }
        public string? SuggestedDestination { get; set; }
        public string? AlternativeDestination { get; set; }
        public double MatchConfidence { get; set; }
        public string? MatchedProjectFolder { get; set; }

        private FileAction _action = FileAction.Pending;
        public FileAction Action
        {
            get => _action;
            set
            {
                if (_action != value)
                {
                    _action = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Action)));
                }
            }
        }

        public bool IsLocked { get; set; }
        public string? LockReason { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Returns the directory the file currently lives in.</summary>
        public string SourceDirectory => System.IO.Path.GetDirectoryName(FullPath) ?? "";
    }

    /// <summary>
    /// A completed file move, stored for undo support and audit history.
    /// </summary>
    public record MoveRecord
    {
        public int Id { get; init; }
        public string SourcePath { get; init; } = string.Empty;
        public string DestPath { get; init; } = string.Empty;
        public string? FileExt { get; init; }
        public string? FileCategory { get; init; }
        public string? ProjectFolder { get; init; }
        public string BatchId { get; init; } = string.Empty;
        public DateTime MovedAt { get; init; }
        public bool Undone { get; set; }
    }

    /// <summary>
    /// An indexed project folder on the D: drive used for fuzzy matching destinations.
    /// </summary>
    public record ProjectFolder
    {
        public int Id { get; init; }
        public string FolderPath { get; init; } = string.Empty;
        public string FolderName { get; init; } = string.Empty;
        public DateTime? LastModified { get; init; }
        public DateTime ScannedAt { get; init; }
    }

    /// <summary>
    /// Summary produced after a scan operation completes.
    /// </summary>
    public record ScanResult
    {
        public List<ScannedFile> Files { get; init; } = new();
        public int SkippedCount { get; init; }
        public List<string> SkippedReasons { get; init; } = new();
        public TimeSpan ScanDuration { get; init; }
    }

    /// <summary>
    /// An acronym-to-expansion mapping used to improve project folder matching.
    /// </summary>
    public record Alias
    {
        public int Id { get; init; }
        public string Acronym { get; init; } = string.Empty;
        public string Expansion { get; init; } = string.Empty;
    }
}
