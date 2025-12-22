using System.ComponentModel;
using ModelContextProtocol.Server;
using Raffinert.FuzzySharp;
using Raffinert.FuzzySharp.PreProcess;
using Raffinert.FuzzySharp.SimilarityRatio;
using Raffinert.FuzzySharp.SimilarityRatio.Scorer;
using Raffinert.FuzzySharp.SimilarityRatio.Scorer.StrategySensitive;
using Raffinert.FuzzySharp.SimilarityRatio.Scorer.Composite;
using ModelContextProtocol.Protocol;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using MCPhappey.Core.Services;
using MCPhappey.Common.Extensions;

namespace MCPhappey.Tools.GitHub.FuzzySharp;

public static class FuzzySharpService
{
    private static IRatioScorer ResolveScorer(string? scorer) => (scorer ?? "weighted").ToLowerInvariant() switch
    {
        "ratio" => ScorerCache.Get<DefaultRatioScorer>(),
        "partial" => ScorerCache.Get<PartialRatioScorer>(),
        "token_set" => ScorerCache.Get<TokenSetScorer>(),
        "partial_token_set" => ScorerCache.Get<PartialTokenSetScorer>(),
        "token_sort" => ScorerCache.Get<TokenSortScorer>(),
        "partial_token_sort" => ScorerCache.Get<PartialTokenSortScorer>(),
        "token_abbreviation" => ScorerCache.Get<TokenAbbreviationScorer>(),
        "partial_token_abbreviation" => ScorerCache.Get<PartialTokenAbbreviationScorer>(),
        _ => ScorerCache.Get<WeightedRatioScorer>(),
    };

    // 1) Document Top Matches
    [Description("Return the top N queries with highest fuzzy score against the COMPLETE document text.")]
    [McpServerTool(
        Title = "Fuzzy document top matches",
        Name = "fuzzysharp_document_top_matches",
        ReadOnly = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> Fuzzy_DocumentTopMatches(
        [Description("User queries to match.")] string[] queries,
        RequestContext<CallToolRequestParams> requestContext,
        IServiceProvider serviceProvider,
        [Description("File url of the document.")] string fileUrl,
        [Description("Max number of results to return (default 3).")] int topN = 3,
        [Description("Scorer type (ratio | partial | token_set | partial_token_set | token_sort | partial_token_sort | token_abbreviation | partial_token_abbreviation | weighted). Default = weighted.")]
        string? scorer = null,
        [Description("Optional max characters from document (0 = no cap).")] int maxChars = 0,
        CancellationToken cancellationToken = default)
        =>
        await requestContext.WithExceptionCheck(async () =>
        {
            var downloadService = serviceProvider.GetRequiredService<DownloadService>();
            var files = await downloadService.ScrapeContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
            var file = files.FirstOrDefault() ?? throw new Exception("File not found or empty.");
            var document = file.Contents.ToString();

            if (maxChars > 0 && document?.Length > maxChars)
                document = document[..maxChars];

            var impl = ResolveScorer(scorer);

            var results = Process.ExtractTop(document, queries, scorer: impl, limit: topN)
                .Select(r => new { query = r.Value, score = r.Score })
                .ToList();

            return await Task.FromResult(results.ToJsonContentBlock("fuzzysharp_document_top_matches").ToCallToolResult());
        });

    // 2) Contains Any
    [Description("Check if any query in the array exceeds a fuzzy score threshold against the COMPLETE document.")]
    [McpServerTool(
        Title = "Fuzzy contains any",
        Name = "fuzzysharp_contains_any",
        ReadOnly = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> Fuzzy_ContainsAny(
        string[] queries,
        RequestContext<CallToolRequestParams> requestContext,
        IServiceProvider serviceProvider,
        string fileUrl,
        [Description("Minimum score threshold (default = 80).")] int threshold = 80,
        string? scorer = null,
        int maxChars = 0,
        CancellationToken cancellationToken = default)
        =>
            await requestContext.WithExceptionCheck(async () =>
            {
                var downloadService = serviceProvider.GetRequiredService<DownloadService>();
                var files = await downloadService.ScrapeContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
                var file = files.FirstOrDefault() ?? throw new Exception("File not found or empty.");
                var document = file.Contents.ToString();

                if (maxChars > 0 && document?.Length > maxChars)
                    document = document[..maxChars];

                var impl = ResolveScorer(scorer);

                bool found = queries.Any(q => impl.Score(document, q ?? string.Empty) >= threshold);

                return await Task.FromResult($"Contains: {found}".ToTextCallToolResponse());
            });

    // 3) Threshold Filter
    [Description("Return all queries from array that exceed a fuzzy score threshold against the COMPLETE document.")]
    [McpServerTool(
        Title = "Fuzzy threshold filter",
        Name = "fuzzysharp_threshold_filter",
        ReadOnly = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> Fuzzy_ThresholdFilter(
        string[] queries,
        RequestContext<CallToolRequestParams> requestContext,
        IServiceProvider serviceProvider,
        string fileUrl,
        [Description("Minimum score threshold (default = 80).")] int threshold = 80,
        string? scorer = null,
        int maxChars = 0,
        CancellationToken cancellationToken = default)
    =>
        await requestContext.WithExceptionCheck(async () =>
        {
            var downloadService = serviceProvider.GetRequiredService<DownloadService>();
            var files = await downloadService.ScrapeContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
            var file = files.FirstOrDefault() ?? throw new Exception("File not found or empty.");
            var document = file.Contents.ToString();

            if (maxChars > 0 && document?.Length > maxChars)
                document = document[..maxChars];

            var impl = ResolveScorer(scorer);

            var hits = queries
                .Select(q => new { query = q, score = impl.Score(document, q ?? string.Empty) })
                .Where(r => r.score >= threshold)
                .OrderByDescending(r => r.score)
                .ToList();

            return await Task.FromResult(hits.ToJsonContentBlock("fuzzysharp_threshold_filter").ToCallToolResult());
        });

    /// <summary>
    /// Compare a query string against the COMPLETE document text and return a single fuzzy score (0-100).
    /// </summary>
    [Description("Fuzzy score of a query against the COMPLETE document text (0-100).")]
    [McpServerTool(
        Title = "Fuzzy document score",
        Name = "fuzzysharp_document_score",
        ReadOnly = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> Fuzzy_DocumentScore(
        [Description("User query to match.")] string query,
        RequestContext<CallToolRequestParams> requestContext,
        IServiceProvider serviceProvider,
        [Description("File url of the docment.")] string fileUrl,
        [Description("Scorer: ratio | partial | token_set | partial_token_set | token_sort | partial_token_sort | token_abbreviation | partial_token_abbreviation | weighted (default)")]
        string? scorer = null,
        [Description("Optional soft cap of document length in characters (0 = no cap). Helps performance on huge texts.")] int maxChars = 0,
        CancellationToken cancellationToken = default)
    =>
        await requestContext.WithExceptionCheck(async () =>
        {
            var downloadService = serviceProvider.GetRequiredService<DownloadService>();
            var files = await downloadService.ScrapeContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
            var file = files.FirstOrDefault() ?? throw new Exception("File not found or empty.");
            var document = file.Contents.ToString();

            if (maxChars > 0 && document?.Length > maxChars)
                document = document[..maxChars]; // no chunking, just a soft cap if you want it

            var impl = ResolveScorer(scorer);
            // Use the chosen scorer explicitly
            var score = impl.Score(query, document);

            return await Task.FromResult($"Score: {score}".ToTextCallToolResponse());
        });

    // 1) Simple Ratio
    [Description("Levenshtein-based similarity (0-100).")]
    [McpServerTool(Title = "Fuzzy ratio", Name = "fuzzysharp_ratio", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    public static async Task<int> Fuzzy_Ratio(
        [Description("Left string")] string a,
        [Description("Right string")] string b) =>
        await Task.FromResult(Fuzz.Ratio(a ?? string.Empty, b ?? string.Empty));

    // 2) Partial Ratio
    [Description("Best partial match similarity (0-100).")]
    [McpServerTool(Title = "Fuzzy partial ratio", Name = "fuzzysharp_partial_ratio", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    public static async Task<int> Fuzzy_PartialRatio(string a, string b) =>
        await Task.FromResult(Fuzz.PartialRatio(a ?? string.Empty, b ?? string.Empty));

    // 3) Token Sort Ratio
    [Description("Ignores word order by sorting tokens (0-100).")]
    [McpServerTool(Title = "Token sort ratio", Name = "fuzzysharp_token_sort_ratio", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    public static async Task<int> Fuzzy_TokenSortRatio(string a, string b) =>
        await Task.FromResult(Fuzz.TokenSortRatio(a ?? string.Empty, b ?? string.Empty));

    // 4) Partial Token Sort Ratio
    [Description("Partial token sort similarity (0-100).")]
    [McpServerTool(Title = "Partial token sort ratio", Name = "fuzzysharp_partial_token_sort_ratio", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    public static async Task<int> Fuzzy_PartialTokenSortRatio(string a, string b) =>
        await Task.FromResult(Fuzz.PartialTokenSortRatio(a ?? string.Empty, b ?? string.Empty));

    // 5) Token Set Ratio
    [Description("Set-based token comparison (handles duplicates, 0-100).")]
    [McpServerTool(Title = "Token set ratio", Name = "fuzzysharp_token_set_ratio", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    public static async Task<int> Fuzzy_TokenSetRatio(string a, string b) =>
        await Task.FromResult(Fuzz.TokenSetRatio(a ?? string.Empty, b ?? string.Empty));

    // 6) Partial Token Set Ratio
    [Description("Partial token set similarity (0-100).")]
    [McpServerTool(Title = "Partial token set ratio", Name = "fuzzysharp_partial_token_set_ratio", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    public static async Task<int> Fuzzy_PartialTokenSetRatio(string a, string b) =>
        await Task.FromResult(Fuzz.PartialTokenSetRatio(a ?? string.Empty, b ?? string.Empty));

    // 7) Token Initialism Ratio
    [Description("Match initialisms (e.g., 'NASA' vs full name).")]
    [McpServerTool(Title = "Token initialism ratio", Name = "fuzzysharp_token_initialism_ratio", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    public static async Task<int> Fuzzy_TokenInitialismRatio(string a, string b) =>
        await Task.FromResult(Fuzz.TokenInitialismRatio(a ?? string.Empty, b ?? string.Empty));

    // 8) Partial Token Initialism Ratio
    [Description("Partial initialism matching (0-100).")]
    [McpServerTool(Title = "Partial token initialism ratio", Name = "fuzzysharp_partial_token_initialism_ratio", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    public static async Task<int> Fuzzy_PartialTokenInitialismRatio(string a, string b) =>
        await Task.FromResult(Fuzz.PartialTokenInitialismRatio(a ?? string.Empty, b ?? string.Empty));

    // 9) Token Abbreviation Ratio (with preprocess mode)
    [Description("Abbreviation-aware ratio (supports PreprocessMode).")]
    [McpServerTool(Title = "Token abbreviation ratio", Name = "fuzzysharp_token_abbreviation_ratio", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    public static async Task<int> Fuzzy_TokenAbbreviationRatio(
        string a,
        string b,
        [Description("'Full' or 'None' preprocessing")] string preprocess = "Full")
    {
        var mode = preprocess.Equals("none", StringComparison.OrdinalIgnoreCase) ? PreprocessMode.None : PreprocessMode.Full;
        return await Task.FromResult(Fuzz.TokenAbbreviationRatio(a ?? string.Empty, b ?? string.Empty, mode));
    }

    // 10) Partial Token Abbreviation Ratio
    [Description("Partial abbreviation-aware ratio (supports PreprocessMode).")]
    [McpServerTool(Title = "Partial token abbreviation ratio", Name = "fuzzysharp_partial_token_abbreviation_ratio", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    public static async Task<int> Fuzzy_PartialTokenAbbreviationRatio(
        string a,
        string b,
        [Description("'Full' or 'None' preprocessing")] string preprocess = "Full")
    {
        var mode = preprocess.Equals("none", StringComparison.OrdinalIgnoreCase) ? PreprocessMode.None : PreprocessMode.Full;
        return await Task.FromResult(Fuzz.PartialTokenAbbreviationRatio(a ?? string.Empty, b ?? string.Empty, mode));
    }

    // 11) Weighted Ratio
    [Description("Composite scorer mixing multiple strategies (0-100).")]
    [McpServerTool(Title = "Weighted ratio", Name = "fuzzysharp_weighted_ratio", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    public static async Task<int> Fuzzy_WeightedRatio(string a, string b) =>
        await Task.FromResult(Fuzz.WeightedRatio(a ?? string.Empty, b ?? string.Empty));

}

