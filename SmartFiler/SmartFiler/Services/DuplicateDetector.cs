using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace SmartFiler.Services
{
    /// <summary>
    /// A single file within a duplicate group.
    /// </summary>
    public record DuplicateFile
    {
        public string FullPath { get; init; } = "";
        public string FileName { get; init; } = "";
        public DateTime LastModified { get; init; }
    }

    /// <summary>
    /// A group of files that share the same content hash and file size.
    /// </summary>
    public record DuplicateGroup
    {
        public string Hash { get; init; } = "";
        public long FileSize { get; init; }
        public bool IsPartialHash { get; init; }
        public List<DuplicateFile> Files { get; init; } = [];
    }

    /// <summary>
    /// Detects duplicate files across source directories by grouping files by size
    /// and then comparing SHA-256 hashes. Files larger than 1 GB use a partial hash
    /// (first 64 KB + last 64 KB + file size) for performance.
    /// </summary>
    public sealed class DuplicateDetector
    {
        private readonly IReadOnlyList<string> _sourceDirectories;

        /// <summary>
        /// Size threshold above which partial hashing is used instead of full-file hashing.
        /// </summary>
        private const long PartialHashThreshold = 1L * 1024 * 1024 * 1024; // 1 GB

        /// <summary>
        /// Number of bytes read from the start and end of large files for partial hashing.
        /// </summary>
        private const int PartialHashChunkSize = 64 * 1024; // 64 KB

        /// <summary>
        /// Creates a duplicate detector that scans the given directories.
        /// If no directories are supplied, defaults to the current user's Downloads, Desktop, and Documents folders.
        /// </summary>
        public DuplicateDetector(IEnumerable<string>? sourceDirectories = null)
        {
            if (sourceDirectories is not null)
            {
                _sourceDirectories = new List<string>(sourceDirectories).AsReadOnly();
            }
            else
            {
                _sourceDirectories = new List<string>
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                }.AsReadOnly();
            }
        }

        /// <summary>
        /// Scans source directories for duplicate files. Groups files by size first,
        /// then computes hashes only for size-matched groups. Returns a list of
        /// <see cref="DuplicateGroup"/> containing two or more identical files.
        /// </summary>
        public async Task<List<DuplicateGroup>> FindDuplicatesAsync()
        {
            // Step 1: Enumerate all files from source directories (top-level only).
            var allFiles = await Task.Run(() => EnumerateSourceFiles());

            // Step 2: Group by size — unique sizes cannot be duplicates.
            var sizeGroups = allFiles
                .GroupBy(f => f.SizeBytes)
                .Where(g => g.Count() >= 2);

            // Step 3: For each size group, compute hashes and find duplicates.
            var duplicateGroups = new List<DuplicateGroup>();

            foreach (var sizeGroup in sizeGroups)
            {
                long fileSize = sizeGroup.Key;
                bool usePartialHash = fileSize > PartialHashThreshold;

                var hashGroups = new Dictionary<string, List<DuplicateFile>>();

                foreach (var file in sizeGroup)
                {
                    try
                    {
                        string hash = usePartialHash
                            ? await ComputePartialHashAsync(file.FullPath, fileSize)
                            : await ComputeFullHashAsync(file.FullPath);

                        if (!hashGroups.TryGetValue(hash, out var list))
                        {
                            list = new List<DuplicateFile>();
                            hashGroups[hash] = list;
                        }

                        list.Add(new DuplicateFile
                        {
                            FullPath = file.FullPath,
                            FileName = file.FileName,
                            LastModified = file.LastModified
                        });
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }

                foreach (var kvp in hashGroups)
                {
                    if (kvp.Value.Count >= 2)
                    {
                        duplicateGroups.Add(new DuplicateGroup
                        {
                            Hash = kvp.Key,
                            FileSize = fileSize,
                            IsPartialHash = usePartialHash,
                            Files = kvp.Value
                        });
                    }
                }
            }

            return duplicateGroups;
        }

        private List<FileEntry> EnumerateSourceFiles()
        {
            var files = new List<FileEntry>();

            foreach (var directory in _sourceDirectories)
            {
                if (!Directory.Exists(directory))
                    continue;

                IEnumerable<string> entries;
                try
                {
                    entries = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly);
                }
                catch (UnauthorizedAccessException) { continue; }
                catch (IOException) { continue; }

                foreach (var filePath in entries)
                {
                    try
                    {
                        var info = new FileInfo(filePath);
                        files.Add(new FileEntry
                        {
                            FullPath = info.FullName,
                            FileName = info.Name,
                            SizeBytes = info.Length,
                            LastModified = info.LastWriteTime
                        });
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }

            return files;
        }

        private static async Task<string> ComputeFullHashAsync(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);

            byte[] hash = await sha256.ComputeHashAsync(stream);
            return Convert.ToHexString(hash);
        }

        private static async Task<string> ComputePartialHashAsync(string filePath, long fileSize)
        {
            using var sha256 = SHA256.Create();

            byte[] headBuffer = new byte[PartialHashChunkSize];
            byte[] tailBuffer = new byte[PartialHashChunkSize];

            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
            {
                // Read first 64 KB.
                int headBytesRead = await ReadExactAsync(stream, headBuffer);

                // Seek to last 64 KB.
                stream.Seek(-PartialHashChunkSize, SeekOrigin.End);
                int tailBytesRead = await ReadExactAsync(stream, tailBuffer);

                // Combine: head + tail + file size as string.
                byte[] sizeBytes = System.Text.Encoding.UTF8.GetBytes(fileSize.ToString());

                sha256.TransformBlock(headBuffer, 0, headBytesRead, null, 0);
                sha256.TransformBlock(tailBuffer, 0, tailBytesRead, null, 0);
                sha256.TransformFinalBlock(sizeBytes, 0, sizeBytes.Length);
            }

            return Convert.ToHexString(sha256.Hash!);
        }

        /// <summary>
        /// Reads as many bytes as possible into the buffer, handling partial reads.
        /// </summary>
        private static async Task<int> ReadExactAsync(FileStream stream, byte[] buffer)
        {
            int totalRead = 0;
            while (totalRead < buffer.Length)
            {
                int bytesRead = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead));
                if (bytesRead == 0)
                    break;
                totalRead += bytesRead;
            }
            return totalRead;
        }

        /// <summary>
        /// Lightweight internal type for file enumeration before hashing.
        /// </summary>
        private record FileEntry
        {
            public string FullPath { get; init; } = "";
            public string FileName { get; init; } = "";
            public long SizeBytes { get; init; }
            public DateTime LastModified { get; init; }
        }
    }
}
