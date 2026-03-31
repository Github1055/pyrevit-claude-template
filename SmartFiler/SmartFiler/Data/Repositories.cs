using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace SmartFiler.Data
{
    // ─────────────────────────────────────────────
    //  MoveHistoryRepo
    // ─────────────────────────────────────────────

    /// <summary>
    /// Persists and queries file-move history for undo and analytics.
    /// </summary>
    public sealed class MoveHistoryRepo
    {
        private readonly SmartFilerDb _db;
        public MoveHistoryRepo(SmartFilerDb db) => _db = db;

        public async Task AddAsync(MoveRecord record)
        {
            await using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO move_history (source_path, dest_path, file_ext, file_category, project_folder, batch_id, moved_at, undone)
                VALUES ($src, $dst, $ext, $cat, $proj, $batch, $at, $undone)";
            cmd.Parameters.AddWithValue("$src", record.SourcePath);
            cmd.Parameters.AddWithValue("$dst", record.DestPath);
            cmd.Parameters.AddWithValue("$ext", (object?)record.FileExt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$cat", (object?)record.FileCategory ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$proj", (object?)record.ProjectFolder ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$batch", record.BatchId);
            cmd.Parameters.AddWithValue("$at", record.MovedAt.ToString("o"));
            cmd.Parameters.AddWithValue("$undone", record.Undone ? 1 : 0);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<MoveRecord>> GetByBatchAsync(string batchId)
        {
            await using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM move_history WHERE batch_id = $batch ORDER BY id";
            cmd.Parameters.AddWithValue("$batch", batchId);
            return await ReadRecordsAsync(cmd);
        }

        public async Task<List<MoveRecord>> GetRecentAsync(int limit = 100)
        {
            await using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM move_history ORDER BY id DESC LIMIT $limit";
            cmd.Parameters.AddWithValue("$limit", limit);
            return await ReadRecordsAsync(cmd);
        }

        public async Task MarkUndoneAsync(string batchId)
        {
            await using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "UPDATE move_history SET undone = 1 WHERE batch_id = $batch";
            cmd.Parameters.AddWithValue("$batch", batchId);
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Returns a map of destination folder path to move count for a given extension,
        /// useful for suggesting the most common destination.
        /// </summary>
        public async Task<Dictionary<string, int>> GetDestinationFrequencyAsync(string fileExt)
        {
            await using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = @"
                SELECT dest_path, COUNT(*) as cnt
                FROM move_history
                WHERE file_ext = $ext AND undone = 0
                GROUP BY dest_path
                ORDER BY cnt DESC";
            cmd.Parameters.AddWithValue("$ext", fileExt);

            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result[reader.GetString(0)] = reader.GetInt32(1);
            }
            return result;
        }

        private static async Task<List<MoveRecord>> ReadRecordsAsync(SqliteCommand cmd)
        {
            var list = new List<MoveRecord>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new MoveRecord
                {
                    Id = reader.GetInt32(reader.GetOrdinal("id")),
                    SourcePath = reader.GetString(reader.GetOrdinal("source_path")),
                    DestPath = reader.GetString(reader.GetOrdinal("dest_path")),
                    FileExt = reader.IsDBNull(reader.GetOrdinal("file_ext")) ? null : reader.GetString(reader.GetOrdinal("file_ext")),
                    FileCategory = reader.IsDBNull(reader.GetOrdinal("file_category")) ? null : reader.GetString(reader.GetOrdinal("file_category")),
                    ProjectFolder = reader.IsDBNull(reader.GetOrdinal("project_folder")) ? null : reader.GetString(reader.GetOrdinal("project_folder")),
                    BatchId = reader.GetString(reader.GetOrdinal("batch_id")),
                    MovedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("moved_at"))),
                    Undone = reader.GetInt32(reader.GetOrdinal("undone")) == 1
                });
            }
            return list;
        }
    }

    // ─────────────────────────────────────────────
    //  ProjectIndexRepo
    // ─────────────────────────────────────────────

    /// <summary>
    /// Manages the indexed project folders used for fuzzy destination matching.
    /// </summary>
    public sealed class ProjectIndexRepo
    {
        private readonly SmartFilerDb _db;
        public ProjectIndexRepo(SmartFilerDb db) => _db = db;

        public async Task UpsertAsync(ProjectFolder folder)
        {
            await using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO project_index (folder_path, folder_name, last_modified, scanned_at)
                VALUES ($path, $name, $mod, $scan)
                ON CONFLICT(id) DO UPDATE SET
                    folder_path   = excluded.folder_path,
                    folder_name   = excluded.folder_name,
                    last_modified = excluded.last_modified,
                    scanned_at    = excluded.scanned_at";
            cmd.Parameters.AddWithValue("$path", folder.FolderPath);
            cmd.Parameters.AddWithValue("$name", folder.FolderName);
            cmd.Parameters.AddWithValue("$mod", folder.LastModified.HasValue ? folder.LastModified.Value.ToString("o") : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$scan", folder.ScannedAt.ToString("o"));
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<ProjectFolder>> GetAllAsync()
        {
            await using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM project_index ORDER BY folder_name";

            var list = new List<ProjectFolder>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new ProjectFolder
                {
                    Id = reader.GetInt32(reader.GetOrdinal("id")),
                    FolderPath = reader.GetString(reader.GetOrdinal("folder_path")),
                    FolderName = reader.GetString(reader.GetOrdinal("folder_name")),
                    LastModified = reader.IsDBNull(reader.GetOrdinal("last_modified"))
                        ? null
                        : DateTime.Parse(reader.GetString(reader.GetOrdinal("last_modified"))),
                    ScannedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("scanned_at")))
                });
            }
            return list;
        }

        public async Task<DateTime?> GetLastScanTimeAsync()
        {
            await using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "SELECT MAX(scanned_at) FROM project_index";
            var result = await cmd.ExecuteScalarAsync();
            if (result is string s)
                return DateTime.Parse(s);
            return null;
        }

        public async Task ClearAsync()
        {
            await using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "DELETE FROM project_index";
            await cmd.ExecuteNonQueryAsync();
        }
    }

    // ─────────────────────────────────────────────
    //  DeferredFileRepo
    // ─────────────────────────────────────────────

    /// <summary>
    /// Tracks files the user has deferred (skipped) so they are not re-suggested.
    /// </summary>
    public sealed class DeferredFileRepo
    {
        private readonly SmartFilerDb _db;
        public DeferredFileRepo(SmartFilerDb db) => _db = db;

        public async Task AddAsync(string filePath)
        {
            await using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "INSERT INTO deferred_files (file_path, deferred_at) VALUES ($path, $at)";
            cmd.Parameters.AddWithValue("$path", filePath);
            cmd.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("o"));
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<HashSet<string>> GetAllPathsAsync()
        {
            await using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "SELECT file_path FROM deferred_files";

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                set.Add(reader.GetString(0));
            }
            return set;
        }

        public async Task RemoveAsync(string filePath)
        {
            await using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "DELETE FROM deferred_files WHERE file_path = $path";
            cmd.Parameters.AddWithValue("$path", filePath);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task ClearAsync()
        {
            await using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "DELETE FROM deferred_files";
            await cmd.ExecuteNonQueryAsync();
        }
    }

    // ─────────────────────────────────────────────
    //  ScanStatsRepo
    // ─────────────────────────────────────────────

    /// <summary>
    /// Records per-scan statistics for the dashboard trend chart.
    /// </summary>
    public sealed class ScanStatsRepo
    {
        private readonly SmartFilerDb _db;
        public ScanStatsRepo(SmartFilerDb db) => _db = db;

        public async Task AddAsync(int filesScanned, int filesMoved, int filesDeferred,
            int filesDeleted, long bytesMoved, long bytesDeleted)
        {
            await using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO scan_stats (scan_date, files_scanned, files_moved, files_deferred, files_deleted, bytes_moved, bytes_deleted)
                VALUES ($date, $scanned, $moved, $deferred, $deleted, $bm, $bd)";
            cmd.Parameters.AddWithValue("$date", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("$scanned", filesScanned);
            cmd.Parameters.AddWithValue("$moved", filesMoved);
            cmd.Parameters.AddWithValue("$deferred", filesDeferred);
            cmd.Parameters.AddWithValue("$deleted", filesDeleted);
            cmd.Parameters.AddWithValue("$bm", bytesMoved);
            cmd.Parameters.AddWithValue("$bd", bytesDeleted);
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Returns weekly totals of files moved, going back the specified number of weeks.
        /// </summary>
        public async Task<List<(DateTime Date, int FilesMoved)>> GetWeeklyTrendAsync(int weeks = 7)
        {
            var cutoff = DateTime.UtcNow.AddDays(-7 * weeks).ToString("o");
            await using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = @"
                SELECT DATE(scan_date, 'weekday 0', '-6 days') AS week_start,
                       SUM(files_moved) AS total_moved
                FROM scan_stats
                WHERE scan_date >= $cutoff
                GROUP BY week_start
                ORDER BY week_start";
            cmd.Parameters.AddWithValue("$cutoff", cutoff);

            var list = new List<(DateTime, int)>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add((DateTime.Parse(reader.GetString(0)), reader.GetInt32(1)));
            }
            return list;
        }

        /// <summary>
        /// Returns totals for the current calendar month.
        /// </summary>
        public async Task<(int TotalMoved, long TotalBytes)> GetMonthTotalsAsync()
        {
            var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1).ToString("o");
            await using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = @"
                SELECT COALESCE(SUM(files_moved), 0), COALESCE(SUM(bytes_moved), 0)
                FROM scan_stats
                WHERE scan_date >= $start";
            cmd.Parameters.AddWithValue("$start", monthStart);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
                return (reader.GetInt32(0), reader.GetInt64(1));
            return (0, 0);
        }
    }

    // ─────────────────────────────────────────────
    //  AliasRepo
    // ─────────────────────────────────────────────

    /// <summary>
    /// Manages acronym-to-expansion aliases for improved project matching.
    /// </summary>
    public sealed class AliasRepo
    {
        private readonly SmartFilerDb _db;
        public AliasRepo(SmartFilerDb db) => _db = db;

        public async Task<List<Alias>> GetAllAsync()
        {
            await using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM aliases ORDER BY acronym";

            var list = new List<Alias>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new Alias
                {
                    Id = reader.GetInt32(reader.GetOrdinal("id")),
                    Acronym = reader.GetString(reader.GetOrdinal("acronym")),
                    Expansion = reader.GetString(reader.GetOrdinal("expansion"))
                });
            }
            return list;
        }

        public async Task AddAsync(string acronym, string expansion)
        {
            await using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "INSERT INTO aliases (acronym, expansion) VALUES ($acr, $exp)";
            cmd.Parameters.AddWithValue("$acr", acronym);
            cmd.Parameters.AddWithValue("$exp", expansion);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task RemoveAsync(int id)
        {
            await using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "DELETE FROM aliases WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    // ─────────────────────────────────────────────
    //  BatchJournalRepo
    // ─────────────────────────────────────────────

    /// <summary>
    /// Write-ahead journal for batch move operations. Records the plan before
    /// execution so incomplete batches can be detected and recovered.
    /// </summary>
    public sealed class BatchJournalRepo
    {
        private readonly SmartFilerDb _db;
        public BatchJournalRepo(SmartFilerDb db) => _db = db;

        public async Task AddPlanAsync(string batchId, string sourcePath, string destPath)
        {
            await using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO batch_journal (batch_id, source_path, dest_path, status, created_at)
                VALUES ($batch, $src, $dst, 'planned', $at)";
            cmd.Parameters.AddWithValue("$batch", batchId);
            cmd.Parameters.AddWithValue("$src", sourcePath);
            cmd.Parameters.AddWithValue("$dst", destPath);
            cmd.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("o"));
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task UpdateStatusAsync(string batchId, string sourcePath, string status)
        {
            await using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "UPDATE batch_journal SET status = $status WHERE batch_id = $batch AND source_path = $src";
            cmd.Parameters.AddWithValue("$status", status);
            cmd.Parameters.AddWithValue("$batch", batchId);
            cmd.Parameters.AddWithValue("$src", sourcePath);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<(string Source, string Dest, string Status)>> GetBatchAsync(string batchId)
        {
            await using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "SELECT source_path, dest_path, status FROM batch_journal WHERE batch_id = $batch ORDER BY id";
            cmd.Parameters.AddWithValue("$batch", batchId);

            var list = new List<(string, string, string)>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            }
            return list;
        }

        public async Task ClearBatchAsync(string batchId)
        {
            await using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "DELETE FROM batch_journal WHERE batch_id = $batch";
            cmd.Parameters.AddWithValue("$batch", batchId);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
