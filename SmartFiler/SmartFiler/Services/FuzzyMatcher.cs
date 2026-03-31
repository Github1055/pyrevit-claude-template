using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SmartFiler.Data;

namespace SmartFiler.Services
{
    /// <summary>
    /// Matches file names to project folders using tokenization, acronym expansion,
    /// and Jaccard similarity scoring.
    /// </summary>
    public sealed class FuzzyMatcher
    {
        private static readonly Regex CamelCaseBoundary =
            new(@"(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", RegexOptions.Compiled);

        private static readonly Regex SplitDelimiters =
            new(@"[-_\s]+", RegexOptions.Compiled);

        private readonly Data.AliasRepo _aliasRepo;
        private Dictionary<string, string>? _cachedAliases;
        private readonly object _aliasLock = new();

        /// <summary>
        /// Creates a new matcher that uses the given <see cref="AliasRepo"/>
        /// to expand acronyms found in file names.
        /// </summary>
        public FuzzyMatcher(Data.AliasRepo aliasRepo)
        {
            _aliasRepo = aliasRepo;
        }

        /// <summary>
        /// Splits a name into lowercase tokens on hyphens, underscores, spaces, and
        /// camelCase boundaries. Empty tokens are removed.
        /// </summary>
        /// <param name="name">The name to tokenize (e.g. "MyProject-v2_final").</param>
        /// <returns>A list of lowercase tokens (e.g. ["my", "project", "v2", "final"]).</returns>
        public List<string> Tokenize(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return new List<string>();

            // First split on camelCase boundaries
            var camelSplit = CamelCaseBoundary.Replace(name, " ");

            // Then split on delimiters
            var parts = SplitDelimiters.Split(camelSplit);

            return parts
                .Select(p => p.ToLowerInvariant())
                .Where(p => p.Length > 0)
                .ToList();
        }

        /// <summary>
        /// Computes the Jaccard similarity coefficient between two token sets.
        /// Returns |intersection| / |union|, or 0 if both sets are empty.
        /// </summary>
        public double JaccardSimilarity(ISet<string> a, ISet<string> b)
        {
            if (a.Count == 0 && b.Count == 0)
                return 0;

            int intersectionCount = 0;
            var smaller = a.Count <= b.Count ? a : b;
            var larger = a.Count <= b.Count ? b : a;

            foreach (var token in smaller)
            {
                if (larger.Contains(token))
                    intersectionCount++;
            }

            int unionCount = a.Count + b.Count - intersectionCount;
            return (double)intersectionCount / unionCount;
        }

        /// <summary>
        /// If the token matches a known alias acronym (case-insensitive), returns
        /// the expansion string. Otherwise returns <c>null</c>.
        /// </summary>
        /// <param name="token">A single lowercase token from a filename.</param>
        /// <param name="aliases">The acronym-to-expansion dictionary.</param>
        /// <returns>The expansion string if found; otherwise <c>null</c>.</returns>
        public string? ExpandAcronyms(string token, Dictionary<string, string> aliases)
        {
            if (aliases.TryGetValue(token, out var expansion))
                return expansion;

            return null;
        }

        /// <summary>
        /// Finds the project folder that best matches a given file name using
        /// tokenized Jaccard similarity with acronym expansion.
        /// </summary>
        /// <param name="fileName">The file name to match (with or without extension).</param>
        /// <param name="projectFolders">Candidate project folders to compare against.</param>
        /// <param name="threshold">Minimum similarity score to consider a match (default 0.4).</param>
        /// <returns>
        /// A tuple of the best matching folder path and its score, or <c>(null, 0)</c>
        /// if no folder meets the threshold.
        /// </returns>
        public Task<(string? FolderPath, double Score)> FindBestMatchAsync(
            string fileName,
            List<ProjectFolder> projectFolders,
            double threshold = 0.4)
        {
            return Task.Run(() =>
            {
                var aliases = GetAliases();

                // Tokenize filename without extension
                var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                var fileTokens = Tokenize(nameWithoutExt);

                // Expand any acronym tokens
                var expandedFileTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var token in fileTokens)
                {
                    expandedFileTokens.Add(token);
                    var expanded = ExpandAcronyms(token, aliases);
                    if (expanded is not null)
                    {
                        foreach (var t in Tokenize(expanded))
                            expandedFileTokens.Add(t);
                    }
                }

                string? bestPath = null;
                double bestScore = 0;

                foreach (var folder in projectFolders)
                {
                    var folderTokens = new HashSet<string>(
                        Tokenize(folder.FolderName), StringComparer.OrdinalIgnoreCase);

                    // Also expand folder name acronyms
                    var expandedFolderTokens = new HashSet<string>(folderTokens, StringComparer.OrdinalIgnoreCase);
                    foreach (var token in folderTokens)
                    {
                        var expanded = ExpandAcronyms(token, aliases);
                        if (expanded is not null)
                        {
                            foreach (var t in Tokenize(expanded))
                                expandedFolderTokens.Add(t);
                        }
                    }

                    var score = JaccardSimilarity(expandedFileTokens, expandedFolderTokens);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestPath = folder.FolderPath;
                    }
                }

                if (bestScore >= threshold)
                    return (bestPath, bestScore);

                return ((string?)null, 0.0);
            });
        }

        /// <summary>
        /// Loads aliases from the repo on first call and caches them for subsequent use.
        /// </summary>
        private Dictionary<string, string> GetAliases()
        {
            if (_cachedAliases is not null)
                return _cachedAliases;

            lock (_aliasLock)
            {
                if (_cachedAliases is null)
                {
                    var aliasList = _aliasRepo.GetAllAsync().GetAwaiter().GetResult();
                    _cachedAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var a in aliasList)
                    {
                        _cachedAliases.TryAdd(a.Acronym, a.Expansion);
                    }
                }
            }

            return _cachedAliases;
        }
    }
}
