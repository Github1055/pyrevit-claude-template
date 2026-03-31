using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SmartFiler.Data;

namespace SmartFiler.Services
{
    /// <summary>
    /// Result of a batch move operation.
    /// </summary>
    public record MoveResult
    {
        public string BatchId { get; init; } = "";
        public int SuccessCount { get; init; }
        public int SkippedCount { get; init; }
        public int DeletedCount { get; init; }
        public long BytesMoved { get; init; }
        public long BytesDeleted { get; init; }
        public List<string> Failures { get; init; } = [];
        public bool AbortedDueToFullDisk { get; init; }
    }

    /// <summary>
    /// Result of an undo operation on the most recent batch.
    /// </summary>
    public record UndoResult
    {
        public int RestoredCount { get; init; }
        public int FailedCount { get; init; }
        public List<string> Failures { get; init; } = [];
    }

    /// <summary>
    /// Executes batch file moves, deletions, and undo operations.
    /// Handles IO errors per-file so one failure never kills the batch.
    /// </summary>
    public sealed class MoveService
    {
        private readonly MoveHistoryRepo _moveHistory;
        private readonly BatchJournalRepo _batchJournal;
        private readonly ScanStatsRepo _scanStats;

        /// <summary>
        /// Creates a new <see cref="MoveService"/> with the required repositories.
        /// </summary>
        /// <param name="moveHistory">Repository for completed move records and undo support.</param>
        /// <param name="batchJournal">Write-ahead journal for crash recovery.</param>
        /// <param name="scanStats">Repository for per-scan statistics.</param>
        public MoveService(MoveHistoryRepo moveHistory, BatchJournalRepo batchJournal, ScanStatsRepo scanStats)
        {
            _moveHistory = moveHistory ?? throw new ArgumentNullException(nameof(moveHistory));
            _batchJournal = batchJournal ?? throw new ArgumentNullException(nameof(batchJournal));
            _scanStats = scanStats ?? throw new ArgumentNullException(nameof(scanStats));
        }

        /// <summary>
        /// Executes a batch of approved file moves and deletions.
        /// <para>
        /// Phase 1 journals all planned moves, Phase 2 executes moves for approved files,
        /// Phase 3 deletes files marked for deletion, and Phase 4 records scan statistics.
        /// </para>
        /// </summary>
        /// <param name="approvedFiles">The list of scanned files to process. Only files with
        /// <see cref="FileAction.Approved"/> or <see cref="FileAction.Delete"/> are acted upon.</param>
        /// <returns>A <see cref="MoveResult"/> summarising successes, skips, and failures.</returns>
        public async Task<MoveResult> ExecuteBatchAsync(List<ScannedFile> approvedFiles)
        {
            var batchId = Guid.NewGuid().ToString("N")[..12];
            var failures = new List<string>();
            int successCount = 0;
            int skippedCount = 0;
            int deletedCount = 0;
            long bytesMoved = 0;
            long bytesDeleted = 0;
            bool abortedDueToFullDisk = false;

            var filesToMove = approvedFiles.Where(f => f.Action == FileAction.Approved).ToList();
            var filesToDelete = approvedFiles.Where(f => f.Action == FileAction.Delete).ToList();

            // ── Phase 1: Journal all planned moves ──────────────────────
            foreach (var file in filesToMove)
            {
                // Use alternative destination if user set one, otherwise suggested
                var effectiveDest = !string.IsNullOrWhiteSpace(file.AlternativeDestination)
                    ? Path.Combine(file.AlternativeDestination, file.FileName)
                    : file.SuggestedDestination;

                if (!string.IsNullOrEmpty(effectiveDest))
                {
                    await _batchJournal.AddPlanAsync(batchId, file.FullPath, effectiveDest);
                }
            }

            // ── Phase 2: Execute moves ──────────────────────────────────
            foreach (var file in filesToMove)
            {
                // Use alternative destination if user set one, otherwise suggested
                var effectiveDest = !string.IsNullOrWhiteSpace(file.AlternativeDestination)
                    ? Path.Combine(file.AlternativeDestination, file.FileName)
                    : file.SuggestedDestination;

                if (string.IsNullOrEmpty(effectiveDest))
                {
                    skippedCount++;
                    continue;
                }

                if (!File.Exists(file.FullPath))
                {
                    skippedCount++;
                    continue;
                }

                try
                {
                    var destDir = Path.GetDirectoryName(effectiveDest)!;
                    Directory.CreateDirectory(destDir);

                    var destPath = effectiveDest;

                    // Handle name conflicts
                    if (File.Exists(destPath))
                    {
                        destPath = AppendTimestampSuffix(destPath);
                    }

                    File.Move(file.FullPath, destPath);

                    // Update journal
                    await _batchJournal.UpdateStatusAsync(batchId, file.FullPath, "MOVED");

                    // Record in move history
                    await _moveHistory.AddAsync(new MoveRecord
                    {
                        SourcePath = file.FullPath,
                        DestPath = destPath,
                        FileExt = file.Extension,
                        FileCategory = file.Category.ToString(),
                        ProjectFolder = file.MatchedProjectFolder,
                        BatchId = batchId,
                        MovedAt = DateTime.UtcNow,
                        Undone = false
                    });

                    successCount++;
                    bytesMoved += file.SizeBytes;
                }
                catch (IOException ex)
                {
                    failures.Add($"Move failed [{file.FileName}]: {ex.Message}");

                    if (IsDiskFullError(ex))
                    {
                        abortedDueToFullDisk = true;
                        break;
                    }
                    // Otherwise skip this file and continue
                }
            }

            // ── Phase 3: Delete files ───────────────────────────────────
            if (!abortedDueToFullDisk)
            {
                foreach (var file in filesToDelete)
                {
                    try
                    {
                        if (!File.Exists(file.FullPath))
                        {
                            skippedCount++;
                            continue;
                        }

                        File.Delete(file.FullPath);
                        deletedCount++;
                        bytesDeleted += file.SizeBytes;
                    }
                    catch (IOException ex)
                    {
                        failures.Add($"Delete failed [{file.FileName}]: {ex.Message}");
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        failures.Add($"Delete failed [{file.FileName}]: {ex.Message}");
                    }
                }
            }

            // ── Phase 4: Record scan statistics ─────────────────────────
            int totalScanned = approvedFiles.Count;
            int deferred = approvedFiles.Count(f => f.Action == FileAction.Deferred);
            await _scanStats.AddAsync(totalScanned, successCount, deferred, deletedCount, bytesMoved, bytesDeleted);

            return new MoveResult
            {
                BatchId = batchId,
                SuccessCount = successCount,
                SkippedCount = skippedCount,
                DeletedCount = deletedCount,
                BytesMoved = bytesMoved,
                BytesDeleted = bytesDeleted,
                Failures = failures,
                AbortedDueToFullDisk = abortedDueToFullDisk
            };
        }

        /// <summary>
        /// Undoes the most recent batch that has not already been undone.
        /// Moves each file back from its destination to its original source path.
        /// </summary>
        /// <returns>An <see cref="UndoResult"/> with restored and failed counts.</returns>
        public async Task<UndoResult> UndoLastBatchAsync()
        {
            var failures = new List<string>();
            int restoredCount = 0;
            int failedCount = 0;

            // Find the most recent batch that hasn't been undone
            var recent = await _moveHistory.GetRecentAsync(500);
            var lastBatchId = recent
                .Where(r => !r.Undone)
                .Select(r => r.BatchId)
                .FirstOrDefault();

            if (lastBatchId is null)
            {
                return new UndoResult
                {
                    RestoredCount = 0,
                    FailedCount = 0,
                    Failures = ["No batch available to undo."]
                };
            }

            var batchRecords = await _moveHistory.GetByBatchAsync(lastBatchId);

            foreach (var record in batchRecords)
            {
                try
                {
                    if (!File.Exists(record.DestPath))
                    {
                        failedCount++;
                        failures.Add($"File no longer at destination: {record.DestPath}");
                        continue;
                    }

                    var sourceDir = Path.GetDirectoryName(record.SourcePath)!;
                    if (!Directory.Exists(sourceDir))
                    {
                        Directory.CreateDirectory(sourceDir);
                    }

                    File.Move(record.DestPath, record.SourcePath);
                    restoredCount++;
                }
                catch (IOException ex)
                {
                    failedCount++;
                    failures.Add($"Undo failed [{Path.GetFileName(record.SourcePath)}]: {ex.Message}");
                }
                catch (UnauthorizedAccessException ex)
                {
                    failedCount++;
                    failures.Add($"Undo failed [{Path.GetFileName(record.SourcePath)}]: {ex.Message}");
                }
            }

            // Mark the entire batch as undone
            await _moveHistory.MarkUndoneAsync(lastBatchId);

            return new UndoResult
            {
                RestoredCount = restoredCount,
                FailedCount = failedCount,
                Failures = failures
            };
        }

        /// <summary>
        /// Appends a timestamp suffix before the file extension to resolve name conflicts.
        /// Example: report.pdf becomes report_20260331_143022.pdf
        /// </summary>
        private static string AppendTimestampSuffix(string filePath)
        {
            var dir = Path.GetDirectoryName(filePath)!;
            var name = Path.GetFileNameWithoutExtension(filePath);
            var ext = Path.GetExtension(filePath);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            return Path.Combine(dir, $"{name}_{stamp}{ext}");
        }

        /// <summary>
        /// Checks whether an IOException indicates the disk is full.
        /// </summary>
        private static bool IsDiskFullError(IOException ex)
        {
            var msg = ex.Message.ToLowerInvariant();
            return msg.Contains("not enough") || msg.Contains("disk full") || msg.Contains("no space");
        }
    }
}
