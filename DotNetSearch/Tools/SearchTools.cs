using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;

namespace DotNetSearch.Tools;

[McpServerToolType]
public static class SearchTools
{
    private static readonly string[] DefaultExtensions =
    [
        ".cs", ".ts", ".js", ".jsx", ".tsx", ".json", ".md", ".txt", ".xml",
        ".yaml", ".yml", ".html", ".css", ".py", ".java", ".cpp", ".c", ".h",
        ".go", ".rs", ".sh", ".ps1", ".psm1", ".psd1", ".toml", ".ini", ".env",
        ".config", ".csproj", ".props", ".targets", ".razor", ".vue", ".svelte"
    ];

    // Directories to skip during file enumeration (build artefacts, dependencies, VCS internals)
    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        // .NET / MSBuild
        "bin", "obj",
        // Node / JS
        "node_modules", ".next", ".nuxt", "dist", "build", "out",
        // VCS
        ".git", ".svn", ".hg",
        // Python
        ".venv", "venv", "__pycache__", ".mypy_cache", ".pytest_cache",
        // Misc IDE / tools
        ".vs", ".idea", ".vscode", ".cache", "coverage", ".terraform"
    };

    // Stop-words to ignore when tokenising natural-language queries
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a","an","the","is","in","on","at","to","for","of","and","or","not",
        "with","this","that","it","be","are","was","were","as","by","from",
        "we","i","you","me","us","do","does","did","has","have","had",
        "how","what","where","when","which","who","why","can","could",
        "should","would","will","may","might","use","used","using","get"
    };

    private const int MinTopK    = 1;
    private const int MaxTopK    = 100;
    private const int MaxContext = 5;

    // BM25 tuning
    private const double BM25_K1 = 1.5;
    private const double BM25_B  = 0.75;

    [McpServerTool]
    [Description(
        "Search a local directory for files whose content matches a query keyword or phrase. " +
        "Supports both exact keyword search and natural-language / semantic-style queries by " +
        "tokenising the query, expanding CamelCase and snake_case identifiers, and ranking " +
        "results with a BM25 relevance score. " +
        "Returns the top matching lines with file path, line number, relevance score and content snippet. " +
        "Pass 'repo' as an absolute or relative local directory path.")]
    public static string Search(
        [Description("Keyword, identifier, or natural-language phrase to search for")] string query,
        [Description("Local directory path to search (absolute or relative)")] string repo,
        [Description("Maximum number of results to return (default: 5, min: 1, max: 100)")] int top_k = 5,
        [Description("Number of surrounding context lines to include above and below each match (default: 0, max: 5)")] int context_lines = 0)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(query))
                return JsonError("query must not be empty.");

            if (string.IsNullOrWhiteSpace(repo))
                return JsonError("repo path must not be empty.");

            top_k         = Math.Clamp(top_k, MinTopK, MaxTopK);
            context_lines = Math.Clamp(context_lines, 0, MaxContext);

            string resolvedPath;
            try { resolvedPath = Path.GetFullPath(repo); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            { return JsonError($"Invalid path '{repo}': {ex.Message}"); }

            if (!Directory.Exists(resolvedPath))
                return JsonError($"Directory not found: {resolvedPath}");

            List<string> files;
            try { files = SafeEnumerateFiles(resolvedPath); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            { return JsonError($"Cannot access directory '{resolvedPath}': {ex.Message}"); }

            // --- Query analysis ---
            bool isExact   = IsSingleToken(query);
            var  tokens    = Tokenise(query);          // NL / multi-word query terms
            bool useScored = tokens.Count > 1 || !isExact;

            var hits = useScored
                ? ScoredSearch(files, resolvedPath, query, tokens, top_k, context_lines)
                : ExactSearch(files, resolvedPath, query, top_k, context_lines);

            if (hits.Count == 0)
                return SafeSerialize(new
                {
                    results = Array.Empty<object>(),
                    message = $"No matches found for '{query}' in {resolvedPath}",
                    mode    = useScored ? "semantic" : "exact"
                });

            return SafeSerialize(new
            {
                results = hits.Select(h => BuildResultObj(h, context_lines)),
                total   = hits.Count,
                query,
                mode    = useScored ? "semantic" : "exact",
                repo    = resolvedPath
            });
        }
        catch (Exception ex)
        {
            return JsonError($"Unexpected error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------
    // Exact search � fast path for single-token / symbol queries
    // -------------------------------------------------------------------------
    private static List<Hit> ExactSearch(
        List<string> files, string root, string query, int topK, int ctx)
    {
        var bag = new ConcurrentBag<Hit>();

        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, file =>
        {
            try
            {
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        bag.Add(new Hit(
                            SafeRelPath(root, file), i + 1,
                            lines[i].Trim(), 1.0,
                            ctx > 0 ? GetContext(lines, i, ctx) : null));
                    }
                }
            }
            catch { /* skip unreadable / locked / oversized files */ }
        });

        return bag
            .OrderBy(h => h.FilePath)
            .ThenBy(h => h.Line)
            .Take(topK)
            .ToList();
    }

    // -------------------------------------------------------------------------
    // Scored search � BM25 over token set; handles NL queries & identifiers
    // -------------------------------------------------------------------------
    private static List<Hit> ScoredSearch(
        List<string> files, string root, string query,
        List<string> tokens, int topK, int ctx)
    {
        // Also include the raw query as an extra "token" so exact phrase still scores high
        var allTerms = tokens.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!IsSingleToken(query)) allTerms.Add(query);

        // Average document length (in tokens) � needed for BM25 normalisation
        // Estimate with a two-pass: first pass collects per-file token counts
        var fileTokenCounts = new ConcurrentDictionary<string, int>();
        var fileLines       = new ConcurrentDictionary<string, string[]>();

        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, file =>
        {
            try
            {
                var lines = File.ReadAllLines(file);
                fileLines[file]       = lines;
                fileTokenCounts[file] = lines.Sum(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
            }
            catch { /* skip */ }
        });

        double avgDocLen = fileTokenCounts.Count == 0 ? 100.0
            : fileTokenCounts.Values.Average();
        int N = fileTokenCounts.Count;

        // Document frequency per term (how many files contain each term)
        var df = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        Parallel.ForEach(fileLines, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, kvp =>
        {
            var text = string.Join(" ", kvp.Value);
            foreach (var term in allTerms)
                if (text.Contains(term, StringComparison.OrdinalIgnoreCase))
                    df.AddOrUpdate(term, 1, (_, c) => c + 1);
        });

        // Score each line across all files
        var bag = new ConcurrentBag<Hit>();

        Parallel.ForEach(fileLines, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, kvp =>
        {
            var file  = kvp.Key;
            var lines = kvp.Value;
            int docLen = fileTokenCounts.GetValueOrDefault(file, 100);

            for (int i = 0; i < lines.Length; i++)
            {
                double score = ScoreLine(lines[i], allTerms, df, N, docLen, avgDocLen);
                if (score > 0)
                {
                    bag.Add(new Hit(
                        SafeRelPath(root, file), i + 1,
                        lines[i].Trim(), Math.Round(score, 4),
                        ctx > 0 ? GetContext(lines, i, ctx) : null));
                }
            }
        });

        return bag
            .OrderByDescending(h => h.Score)
            .ThenBy(h => h.FilePath)
            .Take(topK)
            .ToList();
    }

    // -------------------------------------------------------------------------
    // BM25 line scorer
    // -------------------------------------------------------------------------
    private static double ScoreLine(
        string line, HashSet<string> terms,
        ConcurrentDictionary<string, int> df, int N, int docLen, double avgDocLen)
    {
        var lineTokens = TokeniseLine(line);
        if (lineTokens.Count == 0) return 0;

        double score = 0;
        foreach (var term in terms)
        {
            int tf  = lineTokens.Count(t => t.Equals(term, StringComparison.OrdinalIgnoreCase));
            if (tf == 0) continue;

            int    dft  = df.GetValueOrDefault(term, 0);
            double idf  = dft > 0 ? Math.Log((N - dft + 0.5) / (dft + 0.5) + 1) : 0;
            double norm = (tf * (BM25_K1 + 1)) /
                          (tf + BM25_K1 * (1 - BM25_B + BM25_B * docLen / avgDocLen));
            score += idf * norm;
        }
        return score;
    }

    // -------------------------------------------------------------------------
    // Tokeniser: splits NL query into meaningful terms
    // Handles: CamelCase, snake_case, kebab-case, dots, punctuation
    // Removes stop-words and short noise tokens
    // -------------------------------------------------------------------------
    private static List<string> Tokenise(string input)
    {
        // Expand CamelCase: "McpServerTool" ? "Mcp Server Tool"
        var expanded = Regex.Replace(input, @"(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ");

        // Split on non-alphanumeric (spaces, _, -, ., /, \, :)
        var raw = Regex.Split(expanded, @"[^a-zA-Z0-9]+");

        return raw
            .Select(t => t.Trim())
            .Where(t => t.Length >= 2 && !StopWords.Contains(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // Tokenise a single line of source code into searchable terms
    private static List<string> TokeniseLine(string line)
    {
        var expanded = Regex.Replace(line, @"(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ");
        return Regex.Split(expanded, @"[^a-zA-Z0-9]+")
            .Where(t => t.Length >= 2)
            .ToList();
    }

    private static bool IsSingleToken(string query) =>
        !query.Contains(' ') && !query.Contains('_') && !query.Contains('-') &&
        !Regex.IsMatch(query, @"(?<=[a-z])(?=[A-Z])");

    // -------------------------------------------------------------------------
    // Context lines: N lines above and below the match
    // -------------------------------------------------------------------------
    private static string[] GetContext(string[] lines, int matchIndex, int ctx)
    {
        int start = Math.Max(0, matchIndex - ctx);
        int end   = Math.Min(lines.Length - 1, matchIndex + ctx);
        return lines[start..(end + 1)];
    }

    private static object BuildResultObj(Hit h, int ctx) =>
        ctx > 0 && h.Context != null
            ? new { file_path = h.FilePath, line = h.Line, score = h.Score, content = h.Content, context = h.Context }
            : (object)new { file_path = h.FilePath, line = h.Line, score = h.Score, content = h.Content };

    // -------------------------------------------------------------------------
    // Safe file enumeration � BFS, skips permission-denied / broken paths
    // -------------------------------------------------------------------------
    private static List<string> SafeEnumerateFiles(string root)
    {
        var files = new List<string>();
        var dirs  = new Queue<string>();
        dirs.Enqueue(root);

        while (dirs.Count > 0)
        {
            var dir = dirs.Dequeue();
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    try
                    {
                        if (DefaultExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                            files.Add(file);
                    }
                    catch { /* skip bad entry */ }
                }
            }
            catch { /* skip inaccessible directory */ }

            try
            {
                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    try
                    {
                        if (!SkippedDirectories.Contains(Path.GetFileName(sub)))
                            dirs.Enqueue(sub);
                    }
                    catch { /* skip */ }
                }
            }
            catch { /* skip */ }
        }

        files.Sort();
        return files;
    }

    private static string SafeRelPath(string root, string file)
    {
        try { return Path.GetRelativePath(root, file); }
        catch { return file; }
    }

    // =========================================================================
    // TOOL: search_related
    // =========================================================================

    [McpServerTool]
    [Description(
        "Find code chunks semantically similar to a specific location in a file. " +
        "Use after `search` to explore related implementations, callers, or patterns. " +
        "Pass file_path and line from a prior search result. " +
        "Extracts a smart chunk around the given line and finds the most structurally " +
        "similar code blocks across the entire repo using BM25 token scoring.")]
    public static string SearchRelated(
        [Description("Path to the file as stored in the search result (relative or absolute)")] string file_path,
        [Description("Line number (1-indexed) within the file, from a prior search result")] int line,
        [Description("Local directory path to search (absolute or relative)")] string repo,
        [Description("Number of similar chunks to return (default: 5, min: 1, max: 100)")] int top_k = 5)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(file_path))
                return JsonError("file_path must not be empty.");

            if (string.IsNullOrWhiteSpace(repo))
                return JsonError("repo path must not be empty.");

            if (line < 1)
                return JsonError("line must be >= 1.");

            top_k = Math.Clamp(top_k, MinTopK, MaxTopK);

            string resolvedRepo;
            try { resolvedRepo = Path.GetFullPath(repo); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            { return JsonError($"Invalid repo path '{repo}': {ex.Message}"); }

            if (!Directory.Exists(resolvedRepo))
                return JsonError($"Directory not found: {resolvedRepo}");

            // Resolve the seed file � accept both absolute and relative paths
            string seedFile;
            if (Path.IsPathRooted(file_path))
                seedFile = file_path;
            else
                seedFile = Path.GetFullPath(Path.Combine(resolvedRepo, file_path));

            if (!File.Exists(seedFile))
                return JsonError($"File not found: {seedFile}");

            // --- Extract seed chunk around the given line ---
            string[] seedLines;
            try { seedLines = File.ReadAllLines(seedFile); }
            catch (Exception ex) { return JsonError($"Cannot read file '{seedFile}': {ex.Message}"); }

            if (line > seedLines.Length)
                return JsonError($"Line {line} is out of range � file has {seedLines.Length} lines.");

            string seedChunk;
            try { seedChunk = ExtractChunk(seedLines, line - 1); }   // 0-based index
            catch (Exception ex) { return JsonError($"Failed to extract chunk at line {line}: {ex.Message}"); }

            if (string.IsNullOrWhiteSpace(seedChunk))
                return JsonError($"No meaningful content found around line {line} in '{file_path}'.");

            // Tokenise the seed chunk to build a rich query
            List<string> seedTokens;
            try { seedTokens = Tokenise(seedChunk); }
            catch (Exception ex) { return JsonError($"Failed to tokenise seed chunk: {ex.Message}"); }

            if (seedTokens.Count == 0)
                return JsonError($"Could not extract meaningful tokens from the code at line {line}.");

            // --- Enumerate all repo files ---
            List<string> files;
            try { files = SafeEnumerateFiles(resolvedRepo); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            { return JsonError($"Cannot access directory '{resolvedRepo}': {ex.Message}"); }

            if (files.Count == 0)
                return SafeSerialize(new
                {
                    results   = Array.Empty<object>(),
                    message   = $"No searchable files found in {resolvedRepo}",
                    seed_file = SafeRelPath(resolvedRepo, seedFile),
                    seed_line = line
                });

            // --- Score all chunks across all files using BM25 ---
            var allTerms = seedTokens.ToHashSet(StringComparer.OrdinalIgnoreCase);

            var fileTokenCounts = new ConcurrentDictionary<string, int>();
            var fileLines       = new ConcurrentDictionary<string, string[]>();

            Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, file =>
            {
                try
                {
                    var lines = File.ReadAllLines(file);
                    fileLines[file]       = lines;
                    fileTokenCounts[file] = lines.Sum(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
                }
                catch { /* skip */ }
            });

            double avgDocLen = fileTokenCounts.Count == 0 ? 100.0 : fileTokenCounts.Values.Average();
            int N = fileTokenCounts.Count;

            if (fileLines.IsEmpty)
                return SafeSerialize(new
                {
                    results   = Array.Empty<object>(),
                    message   = "Could not read any files in the repository.",
                    seed_file = SafeRelPath(resolvedRepo, seedFile),
                    seed_line = line
                });

            var df = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            Parallel.ForEach(fileLines, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, kvp =>
            {
                try
                {
                    var text = string.Join(" ", kvp.Value);
                    foreach (var term in allTerms)
                        if (text.Contains(term, StringComparison.OrdinalIgnoreCase))
                            df.AddOrUpdate(term, 1, (_, c) => c + 1);
                }
                catch { /* skip corrupt entries */ }
            });

            // Score by chunks (windows of ChunkSize lines), not individual lines
            const int ChunkSize = 20;
            const int ChunkStep = ChunkSize / 2;
            var    bag        = new ConcurrentBag<Hit>();
            string seedFileRel  = SafeRelPath(resolvedRepo, seedFile);
            int    seedLineIdx  = line - 1;   // 0-based, hoisted out of the loop

            Parallel.ForEach(fileLines, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, kvp =>
            {
                try
                {
                    var    fileArr = kvp.Value;
                    string relPath = SafeRelPath(resolvedRepo, kvp.Key);
                    int    docLen  = fileTokenCounts.GetValueOrDefault(kvp.Key, 100);
                    bool   isSeedF = string.Equals(relPath, seedFileRel, StringComparison.OrdinalIgnoreCase);

                    for (int cs = 0; cs < fileArr.Length; cs += ChunkStep)
                    {
                        try
                        {
                            int ce = Math.Min(cs + ChunkSize, fileArr.Length);
                            if (ce <= cs) continue;                     // guard empty slice

                            // Skip the seed window itself
                            if (isSeedF && cs <= seedLineIdx && seedLineIdx < ce) continue;

                            var    chunkArr  = fileArr[cs..ce];
                            string chunkText = string.Join(" ", chunkArr);

                            double score = ScoreLine(chunkText, allTerms, df, N, docLen, avgDocLen);
                            if (score <= 0) continue;

                            // Representative content: first non-blank line of the chunk
                            string content = chunkArr
                                .FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))
                                ?.Trim() ?? string.Empty;

                            if (string.IsNullOrEmpty(content)) continue;

                            bag.Add(new Hit(relPath, cs + 1, content, Math.Round(score, 4), null));
                        }
                        catch { /* skip this window */ }
                    }
                }
                catch { /* skip this file entirely */ }
            });

            List<Hit> hits;
            try
            {
                hits = bag
                    .GroupBy(h => (h.FilePath, h.Line))           // de-duplicate overlapping windows
                    .Select(g => g.OrderByDescending(h => h.Score).First())
                    .OrderByDescending(h => h.Score)
                    .Take(top_k)
                    .ToList();
            }
            catch (Exception ex)
            { return JsonError($"Failed to rank results: {ex.Message}"); }

            if (hits.Count == 0)
                return SafeSerialize(new
                {
                    results = Array.Empty<object>(),
                    message = $"No related code found for {file_path}:{line}",
                    seed_file  = seedFileRel,
                    seed_line  = line,
                    seed_tokens = seedTokens
                });

            return SafeSerialize(new
            {
                results = hits.Select(h => new
                {
                    file_path = h.FilePath,
                    line      = h.Line,
                    score     = h.Score,
                    content   = h.Content
                }),
                total      = hits.Count,
                seed_file  = seedFileRel,
                seed_line  = line,
                repo       = resolvedRepo
            });
        }
        catch (Exception ex)
        {
            return JsonError($"Unexpected error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------
    // Chunk extractor: pulls a meaningful window around a target line
    // Uses surrounding code up to ChunkRadius lines, trimmed to non-blank edges
    // -------------------------------------------------------------------------
    private static string ExtractChunk(string[] lines, int targetIdx, int radius = 15)
    {
        if (lines.Length == 0) return string.Empty;

        targetIdx = Math.Clamp(targetIdx, 0, lines.Length - 1);
        int start = Math.Clamp(targetIdx - radius, 0, lines.Length - 1);
        int end   = Math.Clamp(targetIdx + radius, 0, lines.Length - 1);

        // Trim leading/trailing blank lines (stop before start == end to avoid infinite loop)
        while (start < end && string.IsNullOrWhiteSpace(lines[start])) start++;
        while (end   > start && string.IsNullOrWhiteSpace(lines[end]))   end--;

        try { return string.Join("\n", lines[start..(end + 1)]); }
        catch { return lines[targetIdx]; }   // fallback: just the target line
    }

    // -------------------------------------------------------------------------
    // JSON helpers
    // -------------------------------------------------------------------------
    private static string SafeSerialize(object value)
    {
        try { return JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }); }
        catch (Exception ex) { return JsonError($"Serialization failed: {ex.Message}"); }
    }

    private static string JsonError(string message)
    {
        try { return JsonSerializer.Serialize(new { error = message }); }
        catch { return $"{{\"error\":\"{message.Replace("\"", "\\'")}\"}}" ; }
    }

    // -------------------------------------------------------------------------
    // Internal types
    // -------------------------------------------------------------------------
    private sealed record Hit(
        string   FilePath,
        int      Line,
        string   Content,
        double   Score,
        string[]? Context);
}

