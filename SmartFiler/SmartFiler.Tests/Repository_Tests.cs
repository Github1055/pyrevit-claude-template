using SmartFiler.Data;

namespace SmartFiler.Tests;

public class Repository_Tests : IDisposable
{
    private readonly SmartFilerDb _db;
    private readonly string _dbPath;

    public Repository_Tests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"smartfiler_test_{Guid.NewGuid():N}.db");
        _db = new SmartFilerDb(_dbPath);
        _db.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        try { File.Delete(_dbPath); } catch { }
    }

    // ── MoveHistoryRepo ────────────────────────────────────

    [Fact]
    public async Task MoveHistory_AddAndGetByBatch_RoundTrips()
    {
        var repo = new MoveHistoryRepo(_db);
        var batchId = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;

        var record = new MoveRecord
        {
            SourcePath = @"C:\Downloads\plan.rvt",
            DestPath = @"D:\Projects\001\plan.rvt",
            FileExt = ".rvt",
            FileCategory = "RevitProject",
            ProjectFolder = "001-Test",
            BatchId = batchId,
            MovedAt = now,
            Undone = false
        };

        await repo.AddAsync(record);
        var results = await repo.GetByBatchAsync(batchId);

        Assert.Single(results);
        var r = results[0];
        Assert.Equal(record.SourcePath, r.SourcePath);
        Assert.Equal(record.DestPath, r.DestPath);
        Assert.Equal(record.FileExt, r.FileExt);
        Assert.Equal(record.FileCategory, r.FileCategory);
        Assert.Equal(record.ProjectFolder, r.ProjectFolder);
        Assert.Equal(batchId, r.BatchId);
        Assert.False(r.Undone);
    }

    [Fact]
    public async Task MoveHistory_MarkUndone_SetsUndoneFlag()
    {
        var repo = new MoveHistoryRepo(_db);
        var batchId = Guid.NewGuid().ToString();

        await repo.AddAsync(new MoveRecord
        {
            SourcePath = @"C:\a.txt",
            DestPath = @"D:\b.txt",
            BatchId = batchId,
            MovedAt = DateTime.UtcNow,
            Undone = false
        });

        await repo.AddAsync(new MoveRecord
        {
            SourcePath = @"C:\c.txt",
            DestPath = @"D:\d.txt",
            BatchId = batchId,
            MovedAt = DateTime.UtcNow,
            Undone = false
        });

        await repo.MarkUndoneAsync(batchId);

        var results = await repo.GetByBatchAsync(batchId);
        Assert.All(results, r => Assert.True(r.Undone));
    }

    [Fact]
    public async Task MoveHistory_GetByBatch_EmptyBatch_ReturnsEmpty()
    {
        var repo = new MoveHistoryRepo(_db);
        var results = await repo.GetByBatchAsync("nonexistent-batch");
        Assert.Empty(results);
    }

    [Fact]
    public async Task MoveHistory_GetRecent_RespectsLimit()
    {
        var repo = new MoveHistoryRepo(_db);
        var batchId = Guid.NewGuid().ToString();

        for (int i = 0; i < 5; i++)
        {
            await repo.AddAsync(new MoveRecord
            {
                SourcePath = $@"C:\file{i}.txt",
                DestPath = $@"D:\file{i}.txt",
                BatchId = batchId,
                MovedAt = DateTime.UtcNow
            });
        }

        var results = await repo.GetRecentAsync(limit: 3);
        Assert.Equal(3, results.Count);
    }

    // ── DeferredFileRepo ───────────────────────────────────

    [Fact]
    public async Task DeferredFile_AddAndGetAll_RoundTrips()
    {
        var repo = new DeferredFileRepo(_db);

        await repo.AddAsync(@"C:\Downloads\skip-me.pdf");
        await repo.AddAsync(@"C:\Downloads\also-skip.docx");

        var paths = await repo.GetAllPathsAsync();

        Assert.Equal(2, paths.Count);
        Assert.Contains(@"C:\Downloads\skip-me.pdf", paths);
        Assert.Contains(@"C:\Downloads\also-skip.docx", paths);
    }

    [Fact]
    public async Task DeferredFile_Remove_DeletesRecord()
    {
        var repo = new DeferredFileRepo(_db);

        await repo.AddAsync(@"C:\temp\file.txt");
        await repo.RemoveAsync(@"C:\temp\file.txt");

        var paths = await repo.GetAllPathsAsync();
        Assert.Empty(paths);
    }

    [Fact]
    public async Task DeferredFile_GetAllPaths_EmptyTable_ReturnsEmptySet()
    {
        var repo = new DeferredFileRepo(_db);
        var paths = await repo.GetAllPathsAsync();
        Assert.Empty(paths);
    }

    // ── ScanStatsRepo ──────────────────────────────────────

    [Fact]
    public async Task ScanStats_AddAndGetMonthTotals_ReturnsCorrectTotals()
    {
        var repo = new ScanStatsRepo(_db);

        await repo.AddAsync(
            filesScanned: 10, filesMoved: 5, filesDeferred: 2,
            filesDeleted: 1, bytesMoved: 5000, bytesDeleted: 1000);

        await repo.AddAsync(
            filesScanned: 20, filesMoved: 8, filesDeferred: 3,
            filesDeleted: 2, bytesMoved: 8000, bytesDeleted: 2000);

        var (totalMoved, totalBytes) = await repo.GetMonthTotalsAsync();

        Assert.Equal(13, totalMoved);      // 5 + 8
        Assert.Equal(13000L, totalBytes);  // 5000 + 8000
    }

    [Fact]
    public async Task ScanStats_GetMonthTotals_EmptyTable_ReturnsZeros()
    {
        var repo = new ScanStatsRepo(_db);
        var (totalMoved, totalBytes) = await repo.GetMonthTotalsAsync();

        Assert.Equal(0, totalMoved);
        Assert.Equal(0L, totalBytes);
    }

    // ── AliasRepo ──────────────────────────────────────────

    [Fact]
    public async Task Alias_AddAndGetAll_RoundTrips()
    {
        var repo = new AliasRepo(_db);

        await repo.AddAsync("sbh", "Sworder Belcher Holt");
        await repo.AddAsync("cw", "Clockwork");

        var aliases = await repo.GetAllAsync();

        Assert.Equal(2, aliases.Count);
        Assert.Contains(aliases, a => a.Acronym == "sbh" && a.Expansion == "Sworder Belcher Holt");
        Assert.Contains(aliases, a => a.Acronym == "cw" && a.Expansion == "Clockwork");
    }

    [Fact]
    public async Task Alias_Remove_DeletesRecord()
    {
        var repo = new AliasRepo(_db);

        await repo.AddAsync("tmp", "Temporary");
        var all = await repo.GetAllAsync();
        Assert.Single(all);

        await repo.RemoveAsync(all[0].Id);

        var afterRemove = await repo.GetAllAsync();
        Assert.Empty(afterRemove);
    }

    [Fact]
    public async Task Alias_GetAll_EmptyTable_ReturnsEmptyList()
    {
        var repo = new AliasRepo(_db);
        var aliases = await repo.GetAllAsync();
        Assert.Empty(aliases);
    }

    // ── ProjectIndexRepo ───────────────────────────────────

    [Fact]
    public async Task ProjectIndex_UpsertAndGetAll_RoundTrips()
    {
        var repo = new ProjectIndexRepo(_db);
        var now = DateTime.UtcNow;

        await repo.UpsertAsync(new ProjectFolder
        {
            FolderPath = @"D:\Projects\001089-Brakes",
            FolderName = "001089-Brakes",
            LastModified = now.AddDays(-1),
            ScannedAt = now
        });

        await repo.UpsertAsync(new ProjectFolder
        {
            FolderPath = @"D:\Projects\002000-Smith",
            FolderName = "002000-Smith",
            LastModified = null,
            ScannedAt = now
        });

        var folders = await repo.GetAllAsync();

        Assert.Equal(2, folders.Count);
        Assert.Contains(folders, f => f.FolderName == "001089-Brakes");
        Assert.Contains(folders, f => f.FolderName == "002000-Smith");
    }

    [Fact]
    public async Task ProjectIndex_GetLastScanTime_ReturnsLatest()
    {
        var repo = new ProjectIndexRepo(_db);
        var earlier = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var later = new DateTime(2026, 3, 15, 12, 0, 0, DateTimeKind.Utc);

        await repo.UpsertAsync(new ProjectFolder
        {
            FolderPath = @"D:\A",
            FolderName = "A",
            ScannedAt = earlier
        });

        await repo.UpsertAsync(new ProjectFolder
        {
            FolderPath = @"D:\B",
            FolderName = "B",
            ScannedAt = later
        });

        var lastScan = await repo.GetLastScanTimeAsync();

        Assert.NotNull(lastScan);
        Assert.Equal(later, lastScan!.Value);
    }

    [Fact]
    public async Task ProjectIndex_Clear_RemovesAllRecords()
    {
        var repo = new ProjectIndexRepo(_db);

        await repo.UpsertAsync(new ProjectFolder
        {
            FolderPath = @"D:\X",
            FolderName = "X",
            ScannedAt = DateTime.UtcNow
        });

        await repo.ClearAsync();

        var folders = await repo.GetAllAsync();
        Assert.Empty(folders);
    }

    [Fact]
    public async Task ProjectIndex_GetAll_EmptyTable_ReturnsEmptyList()
    {
        var repo = new ProjectIndexRepo(_db);
        var folders = await repo.GetAllAsync();
        Assert.Empty(folders);
    }

    [Fact]
    public async Task ProjectIndex_NullLastModified_StoredCorrectly()
    {
        var repo = new ProjectIndexRepo(_db);

        await repo.UpsertAsync(new ProjectFolder
        {
            FolderPath = @"D:\NullMod",
            FolderName = "NullMod",
            LastModified = null,
            ScannedAt = DateTime.UtcNow
        });

        var folders = await repo.GetAllAsync();
        Assert.Single(folders);
        Assert.Null(folders[0].LastModified);
    }
}
