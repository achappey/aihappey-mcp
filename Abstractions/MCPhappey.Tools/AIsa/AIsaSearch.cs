using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.AIsa;

public static class AIsaSearch
{
    [Description("Search smarter and build faster with Querit.ai via AIsa POST /querit/search.")]
    [McpServerTool(
        Name = "aisa_search_querit_search",
        Title = "AIsa Querit search",
        ReadOnly = true,
        OpenWorld = true,
        Destructive = false)]
    public static async Task<CallToolResult?> AIsa_Search_Querit_Search(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Required search query.")] string query,
        [Description("Maximum number of results.")] int? count = null,
        [Description("Optional Querit filters object as JSON string.")] string? filtersJson = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(query);

                var payload = new JsonObject
                {
                    ["query"] = query,
                    ["count"] = count,
                    ["filters"] = ParseObjectOrNull(filtersJson, nameof(filtersJson))
                };

                var client = serviceProvider.GetRequiredService<AIsaClient>();
                return await client.PostAsync("querit/search", payload, cancellationToken);
            }));

    [Description("Run Tavily Search via AIsa POST /tavily/search.")]
    [McpServerTool(
        Name = "aisa_search_tavily_search",
        Title = "AIsa Tavily search",
        ReadOnly = true,
        OpenWorld = true,
        Destructive = false)]
    public static async Task<CallToolResult?> AIsa_Search_Tavily_Search(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Required search query.")] string query,
        [Description("Search depth: advanced, basic, fast, ultra-fast.")] string? searchDepth = null,
        [Description("Maximum chunks per source (1-3).")]
        int? chunksPerSource = null,
        [Description("Maximum number of results (0-20).")]
        int? maxResults = null,
        [Description("Topic: general, news, finance.")] string? topic = null,
        [Description("Time range enum value.")] string? timeRange = null,
        [Description("Start date (YYYY-MM-DD).")]
        string? startDate = null,
        [Description("End date (YYYY-MM-DD).")]
        string? endDate = null,
        [Description("Include LLM answer.")]
        bool? includeAnswer = null,
        [Description("Include cleaned raw content.")]
        bool? includeRawContent = null,
        [Description("Include image results.")]
        bool? includeImages = null,
        [Description("Include image descriptions.")]
        bool? includeImageDescriptions = null,
        [Description("Include favicon URL.")]
        bool? includeFavicon = null,
        [Description("Include only these domains as JSON array string, e.g. [\"example.com\"].")]
        string? includeDomainsJson = null,
        [Description("Exclude these domains as JSON array string, e.g. [\"example.com\"].")]
        string? excludeDomainsJson = null,
        [Description("Country enum value to boost.")]
        string? country = null,
        [Description("Auto-configure parameters.")]
        bool? autoParameters = null,
        [Description("Include usage info.")]
        bool? includeUsage = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(query);

                var payload = new JsonObject
                {
                    ["query"] = query,
                    ["search_depth"] = searchDepth,
                    ["chunks_per_source"] = chunksPerSource,
                    ["max_results"] = maxResults,
                    ["topic"] = topic,
                    ["time_range"] = timeRange,
                    ["start_date"] = startDate,
                    ["end_date"] = endDate,
                    ["include_answer"] = includeAnswer,
                    ["include_raw_content"] = includeRawContent,
                    ["include_images"] = includeImages,
                    ["include_image_descriptions"] = includeImageDescriptions,
                    ["include_favicon"] = includeFavicon,
                    ["include_domains"] = ParseStringArrayOrNull(includeDomainsJson, nameof(includeDomainsJson)),
                    ["exclude_domains"] = ParseStringArrayOrNull(excludeDomainsJson, nameof(excludeDomainsJson)),
                    ["country"] = country,
                    ["auto_parameters"] = autoParameters,
                    ["include_usage"] = includeUsage
                };

                var client = serviceProvider.GetRequiredService<AIsaClient>();
                return await client.PostAsync("tavily/search", payload, cancellationToken);
            }));

    [Description("Run Tavily Extract via AIsa POST /tavily/extract.")]
    [McpServerTool(
        Name = "aisa_search_tavily_extract",
        Title = "AIsa Tavily extract",
        ReadOnly = true,
        OpenWorld = true,
        Destructive = false)]
    public static async Task<CallToolResult?> AIsa_Search_Tavily_Extract(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Required URLs as JSON array string, e.g. [\"https://example.com\"].")]
        string urlsJson,
        [Description("Optional reranking query.")]
        string? query = null,
        [Description("Maximum chunks per source (1-5).")]
        int? chunksPerSource = null,
        [Description("Extract depth: basic or advanced.")]
        string? extractDepth = null,
        [Description("Include extracted images.")]
        bool? includeImages = null,
        [Description("Include favicon.")]
        bool? includeFavicon = null,
        [Description("Output format: markdown or text.")]
        string? format = null,
        [Description("Timeout in seconds (1-60).")]
        double? timeout = null,
        [Description("Include usage info.")]
        bool? includeUsage = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var urls = ParseStringArrayOrNull(urlsJson, nameof(urlsJson));
                if (urls == null || urls.Count == 0)
                    throw new ArgumentException("urlsJson must contain at least one URL.", nameof(urlsJson));

                var payload = new JsonObject
                {
                    ["urls"] = urls,
                    ["query"] = query,
                    ["chunks_per_source"] = chunksPerSource,
                    ["extract_depth"] = extractDepth,
                    ["include_images"] = includeImages,
                    ["include_favicon"] = includeFavicon,
                    ["format"] = format,
                    ["timeout"] = timeout,
                    ["include_usage"] = includeUsage
                };

                var client = serviceProvider.GetRequiredService<AIsaClient>();
                return await client.PostAsync("tavily/extract", payload, cancellationToken);
            }));

    [Description("Run Tavily Crawl via AIsa POST /tavily/crawl.")]
    [McpServerTool(
        Name = "aisa_search_tavily_crawl",
        Title = "AIsa Tavily crawl",
        ReadOnly = true,
        OpenWorld = true,
        Destructive = false)]
    public static async Task<CallToolResult?> AIsa_Search_Tavily_Crawl(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Root URL to crawl.")] string url,
        [Description("Natural language crawl instructions.")] string? instructions = null,
        [Description("Maximum chunks per source (1-5).")]
        int? chunksPerSource = null,
        [Description("Maximum depth (1-5).")]
        int? maxDepth = null,
        [Description("Maximum breadth (1-500).")]
        int? maxBreadth = null,
        [Description("Total processing limit (>=1).")]
        int? limit = null,
        [Description("Regex select paths as JSON array string.")] string? selectPathsJson = null,
        [Description("Regex select domains as JSON array string.")] string? selectDomainsJson = null,
        [Description("Regex exclude paths as JSON array string.")] string? excludePathsJson = null,
        [Description("Regex exclude domains as JSON array string.")] string? excludeDomainsJson = null,
        [Description("Allow external links in results.")] bool? allowExternal = null,
        [Description("Include images in crawl results.")] bool? includeImages = null,
        [Description("Extract depth: basic or advanced.")] string? extractDepth = null,
        [Description("Output format: markdown or text.")] string? format = null,
        [Description("Include favicon.")] bool? includeFavicon = null,
        [Description("Timeout in seconds (10-150).")]
        double? timeout = null,
        [Description("Include usage info.")] bool? includeUsage = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(url);

                var payload = new JsonObject
                {
                    ["url"] = url,
                    ["instructions"] = instructions,
                    ["chunks_per_source"] = chunksPerSource,
                    ["max_depth"] = maxDepth,
                    ["max_breadth"] = maxBreadth,
                    ["limit"] = limit,
                    ["select_paths"] = ParseStringArrayOrNull(selectPathsJson, nameof(selectPathsJson)),
                    ["select_domains"] = ParseStringArrayOrNull(selectDomainsJson, nameof(selectDomainsJson)),
                    ["exclude_paths"] = ParseStringArrayOrNull(excludePathsJson, nameof(excludePathsJson)),
                    ["exclude_domains"] = ParseStringArrayOrNull(excludeDomainsJson, nameof(excludeDomainsJson)),
                    ["allow_external"] = allowExternal,
                    ["include_images"] = includeImages,
                    ["extract_depth"] = extractDepth,
                    ["format"] = format,
                    ["include_favicon"] = includeFavicon,
                    ["timeout"] = timeout,
                    ["include_usage"] = includeUsage
                };

                var client = serviceProvider.GetRequiredService<AIsaClient>();
                return await client.PostAsync("tavily/crawl", payload, cancellationToken);
            }));

    [Description("Run Tavily Map via AIsa POST /tavily/map.")]
    [McpServerTool(
        Name = "aisa_search_tavily_map",
        Title = "AIsa Tavily map",
        ReadOnly = true,
        OpenWorld = true,
        Destructive = false)]
    public static async Task<CallToolResult?> AIsa_Search_Tavily_Map(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Root URL to map.")] string url,
        [Description("Natural language map instructions.")] string? instructions = null,
        [Description("Maximum depth (1-5).")]
        int? maxDepth = null,
        [Description("Maximum breadth (1-500).")]
        int? maxBreadth = null,
        [Description("Total processing limit (>=1).")]
        int? limit = null,
        [Description("Regex select paths as JSON array string.")] string? selectPathsJson = null,
        [Description("Regex select domains as JSON array string.")] string? selectDomainsJson = null,
        [Description("Regex exclude paths as JSON array string.")] string? excludePathsJson = null,
        [Description("Regex exclude domains as JSON array string.")] string? excludeDomainsJson = null,
        [Description("Allow external links in results.")] bool? allowExternal = null,
        [Description("Timeout in seconds (10-150).")]
        double? timeout = null,
        [Description("Include usage info.")] bool? includeUsage = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(url);

                var payload = new JsonObject
                {
                    ["url"] = url,
                    ["instructions"] = instructions,
                    ["max_depth"] = maxDepth,
                    ["max_breadth"] = maxBreadth,
                    ["limit"] = limit,
                    ["select_paths"] = ParseStringArrayOrNull(selectPathsJson, nameof(selectPathsJson)),
                    ["select_domains"] = ParseStringArrayOrNull(selectDomainsJson, nameof(selectDomainsJson)),
                    ["exclude_paths"] = ParseStringArrayOrNull(excludePathsJson, nameof(excludePathsJson)),
                    ["exclude_domains"] = ParseStringArrayOrNull(excludeDomainsJson, nameof(excludeDomainsJson)),
                    ["allow_external"] = allowExternal,
                    ["timeout"] = timeout,
                    ["include_usage"] = includeUsage
                };

                var client = serviceProvider.GetRequiredService<AIsaClient>();
                return await client.PostAsync("tavily/map", payload, cancellationToken);
            }));

    private static JsonObject? ParseObjectOrNull(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        try
        {
            var node = JsonNode.Parse(value);
            return node as JsonObject;
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"{parameterName} is not valid JSON.", parameterName, ex);
        }
    }

    private static JsonArray? ParseStringArrayOrNull(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        try
        {
            var node = JsonNode.Parse(value);
            if (node is not JsonArray array)
                throw new ArgumentException($"{parameterName} must be a JSON array of strings.", parameterName);

            var result = new JsonArray();
            foreach (var item in array)
            {
                var text = item?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(text))
                    result.Add(text);
            }

            return result;
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"{parameterName} is not valid JSON.", parameterName, ex);
        }
    }
}

