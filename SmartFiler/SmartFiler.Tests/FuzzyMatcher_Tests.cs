using SmartFiler.Data;
using SmartFiler.Services;

namespace SmartFiler.Tests;

public class FuzzyMatcher_Tests : IDisposable
{
    private readonly SmartFilerDb _db;
    private readonly AliasRepo _aliasRepo;
    private readonly FuzzyMatcher _matcher;
    private readonly string _dbPath;

    public FuzzyMatcher_Tests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"smartfiler_test_{Guid.NewGuid():N}.db");
        _db = new SmartFilerDb(_dbPath);
        _db.EnsureCreated();
        _aliasRepo = new AliasRepo(_db);
        _matcher = new FuzzyMatcher(_aliasRepo);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { File.Delete(_dbPath); } catch { }
    }

    // ── Tokenize ───────────────────────────────────────────

    [Fact]
    public void Tokenize_HyphenSeparated_SplitsCorrectly()
    {
        var tokens = _matcher.Tokenize("001089-Brakes-Grantham");
        Assert.Equal(new[] { "001089", "brakes", "grantham" }, tokens);
    }

    [Fact]
    public void Tokenize_UnderscoreSeparated_SplitsCorrectly()
    {
        var tokens = _matcher.Tokenize("SBH_Door_Type_04");
        Assert.Equal(new[] { "sbh", "door", "type", "04" }, tokens);
    }

    [Fact]
    public void Tokenize_CamelCase_SplitsOnBoundaries()
    {
        var tokens = _matcher.Tokenize("OakwoodExterior");
        Assert.Equal(new[] { "oakwood", "exterior" }, tokens);
    }

    [Fact]
    public void Tokenize_EmptyString_ReturnsEmptyList()
    {
        var tokens = _matcher.Tokenize("");
        Assert.Empty(tokens);
    }

    [Fact]
    public void Tokenize_NullString_ReturnsEmptyList()
    {
        var tokens = _matcher.Tokenize(null!);
        Assert.Empty(tokens);
    }

    [Fact]
    public void Tokenize_WhitespaceOnly_ReturnsEmptyList()
    {
        var tokens = _matcher.Tokenize("   ");
        Assert.Empty(tokens);
    }

    [Fact]
    public void Tokenize_MixedDelimiters_SplitsCorrectly()
    {
        var tokens = _matcher.Tokenize("My-Project_Name v2");
        Assert.Equal(new[] { "my", "project", "name", "v2" }, tokens);
    }

    [Fact]
    public void Tokenize_AllLowercase_ReturnsAsIs()
    {
        var tokens = _matcher.Tokenize("simple");
        Assert.Equal(new[] { "simple" }, tokens);
    }

    // ── JaccardSimilarity ──────────────────────────────────

    [Fact]
    public void JaccardSimilarity_IdenticalSets_ReturnsOne()
    {
        var a = new HashSet<string> { "alpha", "beta", "gamma" };
        var b = new HashSet<string> { "alpha", "beta", "gamma" };

        Assert.Equal(1.0, _matcher.JaccardSimilarity(a, b));
    }

    [Fact]
    public void JaccardSimilarity_NoOverlap_ReturnsZero()
    {
        var a = new HashSet<string> { "alpha", "beta" };
        var b = new HashSet<string> { "gamma", "delta" };

        Assert.Equal(0.0, _matcher.JaccardSimilarity(a, b));
    }

    [Fact]
    public void JaccardSimilarity_PartialOverlap_ReturnsCorrectRatio()
    {
        // intersection = { "alpha" } → 1
        // union = { "alpha", "beta", "gamma" } → 3
        // Jaccard = 1/3
        var a = new HashSet<string> { "alpha", "beta" };
        var b = new HashSet<string> { "alpha", "gamma" };

        Assert.Equal(1.0 / 3.0, _matcher.JaccardSimilarity(a, b), precision: 10);
    }

    [Fact]
    public void JaccardSimilarity_BothEmpty_ReturnsZero()
    {
        var a = new HashSet<string>();
        var b = new HashSet<string>();

        Assert.Equal(0.0, _matcher.JaccardSimilarity(a, b));
    }

    [Fact]
    public void JaccardSimilarity_OneEmpty_ReturnsZero()
    {
        var a = new HashSet<string> { "alpha" };
        var b = new HashSet<string>();

        Assert.Equal(0.0, _matcher.JaccardSimilarity(a, b));
    }

    [Fact]
    public void JaccardSimilarity_SubsetRelation_ReturnsCorrectRatio()
    {
        // intersection = { "a", "b" } → 2
        // union = { "a", "b", "c" } → 3
        // Jaccard = 2/3
        var a = new HashSet<string> { "a", "b" };
        var b = new HashSet<string> { "a", "b", "c" };

        Assert.Equal(2.0 / 3.0, _matcher.JaccardSimilarity(a, b), precision: 10);
    }

    // ── ExpandAcronyms ─────────────────────────────────────

    [Fact]
    public void ExpandAcronyms_KnownAcronym_ReturnsExpansion()
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sbh"] = "Sworder Belcher Holt"
        };

        Assert.Equal("Sworder Belcher Holt", _matcher.ExpandAcronyms("sbh", aliases));
    }

    [Fact]
    public void ExpandAcronyms_UnknownToken_ReturnsNull()
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Assert.Null(_matcher.ExpandAcronyms("xyz", aliases));
    }

    // ── FindBestMatchAsync ─────────────────────────────────

    [Fact]
    public async Task FindBestMatchAsync_ExactTokenMatch_ReturnsFolderAboveThreshold()
    {
        var folders = new List<ProjectFolder>
        {
            new() { FolderPath = @"D:\Projects\001089-Brakes-Grantham", FolderName = "001089-Brakes-Grantham", ScannedAt = DateTime.UtcNow },
            new() { FolderPath = @"D:\Projects\002000-Smith-London", FolderName = "002000-Smith-London", ScannedAt = DateTime.UtcNow }
        };

        var (path, score) = await _matcher.FindBestMatchAsync("001089-Brakes-Drawing.rvt", folders);

        Assert.NotNull(path);
        Assert.Contains("001089", path);
        Assert.True(score >= 0.4);
    }

    [Fact]
    public async Task FindBestMatchAsync_NoMatch_ReturnsNullAndZero()
    {
        var folders = new List<ProjectFolder>
        {
            new() { FolderPath = @"D:\Projects\001089-Brakes-Grantham", FolderName = "001089-Brakes-Grantham", ScannedAt = DateTime.UtcNow }
        };

        var (path, score) = await _matcher.FindBestMatchAsync("TotallyUnrelatedFile.pdf", folders);

        Assert.Null(path);
        Assert.Equal(0.0, score);
    }
}
