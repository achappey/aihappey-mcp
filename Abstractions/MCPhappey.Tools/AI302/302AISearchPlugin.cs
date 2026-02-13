using System.ComponentModel;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.AI302;

public static class AI302SearchPlugin
{
    [Description("Unified Search API for 302.AI providers.")]
    [McpServerTool(Title = "302.AI unified search", Name = "302ai_search_unified", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> AI302_Search_Unified(
        [Description("Search keyword.")] string query,
        [Description("Search provider, e.g. tavily, search1_search, search1_news, bocha, exa, firecrawl, metaso.")] string provider,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Maximum number of search results.")] int? maxResults = 5,
        [Description("Search category. Provider-specific values apply.")] string? category = null,
        [Description("Time filter value. Provider-specific formats apply.")] string? timeRange = null,
        [Description("Exa crawl start datetime in ISO8601, e.g. 2023-01-01T00:00:00.000Z.")] string? startCrawlDate = null,
        [Description("Exa crawl end datetime in ISO8601, e.g. 2023-12-31T23:59:59.000Z.")] string? endCrawlDate = null,
        [Description("Exa publish start datetime in ISO8601, e.g. 2023-01-01T00:00:00.000Z.")] string? startPublishedDate = null,
        [Description("Exa publish end datetime in ISO8601, e.g. 2023-12-31T23:59:59.000Z.")] string? endPublishedDate = null,
        [Description("Number of pages to crawl for full content. Effective for search1 providers.")] int? crawlResults = null,
        [Description("Whether to include image information in results.")] bool? includeImages = true,
        [Description("Comma-separated whitelist domains, e.g. example.com,news.example.com.")] string? includeDomains = null,
        [Description("Comma-separated blacklist domains, e.g. spam.example.com,ads.example.com.")] string? excludeDomains = null,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var client = serviceProvider.GetRequiredService<AI302Client>();

                var body = new JsonObject
                {
                    ["query"] = query,
                    ["provider"] = provider,
                    ["max_results"] = maxResults,
                    ["category"] = category,
                    ["time_range"] = timeRange,
                    ["startCrawlDate"] = startCrawlDate,
                    ["endCrawlDate"] = endCrawlDate,
                    ["startPublishedDate"] = startPublishedDate,
                    ["endPublishedDate"] = endPublishedDate,
                    ["crawl_results"] = crawlResults,
                    ["include_images"] = includeImages,
                    ["include_domains"] = includeDomains,
                    ["exclude_domains"] = excludeDomains
                };

                JsonNode? response = await client.PostAsync("302/general/search", body, cancellationToken);
                return response;
            }));
}
