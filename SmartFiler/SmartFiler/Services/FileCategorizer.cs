using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using SmartFiler.Data;

namespace SmartFiler.Services
{
    /// <summary>
    /// Classifies files into <see cref="FileCategory"/> values based on extension
    /// and filename keyword rules. Rules are evaluated in priority order so that
    /// more specific matches (e.g. Revit backups) take precedence over general ones.
    /// </summary>
    public static class FileCategorizer
    {
        // Pre-compiled regexes for backup detection
        private static readonly Regex RevitBackupRegex =
            new(@"\.\d{4}\.rvt$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex RfaBackupRegex =
            new(@"\.\d{4}\.rfa$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex BlenderBackupRegex =
            new(@"\.blend\d+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Extension lookup sets
        private static readonly HashSet<string> AutoCadExtensions =
            new(StringComparer.OrdinalIgnoreCase) { ".dwg", ".dxf" };

        private static readonly Regex AutoCadBackupRegex =
            new(@"\.bak$|\.sv\$$|\.ac\$$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex FreeCadBackupRegex =
            new(@"\.FCBak$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly HashSet<string> ThreeDInterchangeExtensions =
            new(StringComparer.OrdinalIgnoreCase) { ".fbx", ".obj", ".glb", ".gltf", ".stl", ".3mf" };

        private static readonly HashSet<string> MsWordExtensions =
            new(StringComparer.OrdinalIgnoreCase) { ".docx", ".doc", ".docm", ".dotx", ".dotm", ".rtf" };

        private static readonly HashSet<string> MsExcelExtensions =
            new(StringComparer.OrdinalIgnoreCase) { ".xlsx", ".xls", ".xlsm", ".xlsb", ".xltx", ".csv" };

        private static readonly HashSet<string> MsPowerPointExtensions =
            new(StringComparer.OrdinalIgnoreCase) { ".pptx", ".ppt", ".pptm", ".potx" };

        private static readonly HashSet<string> PdfExtensions =
            new(StringComparer.OrdinalIgnoreCase) { ".pdf" };

        private static readonly HashSet<string> TextExtensions =
            new(StringComparer.OrdinalIgnoreCase) { ".txt", ".md", ".log", ".json", ".xml", ".yaml", ".yml" };

        private static readonly HashSet<string> ImageExtensions =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff",
                ".psd", ".svg", ".gif", ".webp"
            };

        private static readonly HashSet<string> WebLinkExtensions =
            new(StringComparer.OrdinalIgnoreCase) { ".url", ".webloc" };

        private static readonly HashSet<string> InstallerExtensions =
            new(StringComparer.OrdinalIgnoreCase) { ".exe", ".msi" };

        private static readonly HashSet<string> ArchiveExtensions =
            new(StringComparer.OrdinalIgnoreCase) { ".zip", ".rar", ".7z", ".tar", ".gz" };

        // Keyword sets for installer vs driver detection
        private static readonly string[] DriverKeywords =
            { "driver", "chipset", "whql", "firmware" };

        private static readonly string[] InstallerKeywords =
            { "setup", "install", "installer" };

        /// <summary>
        /// Classifies a single file by its name and extension.
        /// Rules are evaluated in priority order; the first match wins.
        /// </summary>
        /// <param name="fileName">The file name (with extension), e.g. "Project.0001.rvt".</param>
        /// <returns>The <see cref="FileCategory"/> that best describes the file.</returns>
        public static FileCategory Categorize(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return FileCategory.Other;

            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            var nameLower = fileName.ToLowerInvariant();

            // --- Revit ---
            if (ext == ".rvt")
                return RevitBackupRegex.IsMatch(fileName) ? FileCategory.RevitBackup : FileCategory.RevitProject;

            if (ext == ".rfa")
                return RfaBackupRegex.IsMatch(fileName) ? FileCategory.RfaBackup : FileCategory.RevitFamily;

            if (ext == ".rte")
                return FileCategory.RevitTemplate;

            // --- Blender ---
            if (BlenderBackupRegex.IsMatch(fileName))
                return FileCategory.BlenderBackup;

            if (ext == ".blend")
                return FileCategory.Blender;

            // --- CAD / 3D ---
            if (AutoCadBackupRegex.IsMatch(fileName))
                return FileCategory.AutoCadBackup;

            if (AutoCadExtensions.Contains(ext))
                return FileCategory.AutoCad;

            if (ext == ".3dm")
                return FileCategory.Rhino;

            if (ext == ".plasticity")
                return FileCategory.Plasticity;

            // --- FreeCad ---
            if (FreeCadBackupRegex.IsMatch(fileName))
                return FileCategory.FreeCadBackup;

            if (ext == ".fcstd")
                return FileCategory.FreeCad;

            if (ThreeDInterchangeExtensions.Contains(ext))
                return FileCategory.ThreeDInterchange;

            // --- Microsoft Office ---
            if (MsWordExtensions.Contains(ext))
                return FileCategory.MsWord;

            if (MsExcelExtensions.Contains(ext))
                return FileCategory.MsExcel;

            if (MsPowerPointExtensions.Contains(ext))
                return FileCategory.MsPowerPoint;

            // --- PDF ---
            if (PdfExtensions.Contains(ext))
                return FileCategory.Pdf;

            // --- Text files ---
            if (TextExtensions.Contains(ext))
                return FileCategory.TextFile;

            if (ImageExtensions.Contains(ext))
                return FileCategory.Image;

            // --- Web Links ---
            if (WebLinkExtensions.Contains(ext))
                return FileCategory.WebLink;

            // --- Desktop Shortcuts ---
            if (ext == ".lnk")
                return FileCategory.Shortcut;

            // --- Executables: Driver vs Installer ---
            if (InstallerExtensions.Contains(ext))
            {
                foreach (var kw in DriverKeywords)
                {
                    if (nameLower.Contains(kw))
                        return FileCategory.Driver;
                }

                foreach (var kw in InstallerKeywords)
                {
                    if (nameLower.Contains(kw))
                        return FileCategory.Installer;
                }
            }

            // --- Archives ---
            if (ArchiveExtensions.Contains(ext))
                return FileCategory.Archive;

            return FileCategory.Other;
        }

        /// <summary>
        /// Sets the <see cref="ScannedFile.Category"/> property on every file in the list
        /// by running it through the categorization rules.
        /// </summary>
        /// <param name="files">The scanned files to classify in place.</param>
        public static void CategorizeAll(List<ScannedFile> files)
        {
            if (files is null) return;

            for (int i = 0; i < files.Count; i++)
            {
                var f = files[i];
                if (!f.IsDirectory)
                    f.Category = Categorize(f.FileName);
                files[i] = f;
            }
        }
    }
}
