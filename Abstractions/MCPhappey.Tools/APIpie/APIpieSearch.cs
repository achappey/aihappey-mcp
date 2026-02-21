using System.ComponentModel;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.APIpie;

public static class APIpieSearch
{
    [Description("Perform web search with APIpie and return ranked results from providers like Google or Valyu.")]
    [McpServerTool(Title = "APIpie web search", Name = "apipie_search_web", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> APIpie_Search_Web(
        [Description("Search query string.")] string query,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Search provider (google or valyu).")]
        string search_provider = "valyu",
        [Description("Country code for localized results.")]
        string geo = "us",
        [Description("Language code for search results.")]
        string lang = "en",
        [Description("Number of results to return.")]
        int results = 20,
        [Description("Safe search filtering level (1=on, -1=moderate, -2=off).")]
        int? safeSearch = -1,
        [Description("User identifier for observability and billing.")]
        string? user = null,
        [Description("Optional list of allowed sources (valyu only).")]
        List<string>? whitelist = null,
        [Description("Optional list of excluded sources (valyu only).")]
        List<string>? blacklist = null,
        [Description("Maximum price in dollars for a thousand retrievals (valyu only).")]
        decimal? max_price = null,
        [Description("Start date for time-filtered searches in YYYY-MM-DD format (valyu only).")]
        string? start_date = null,
        [Description("End date for time-filtered searches in YYYY-MM-DD format (valyu only).")]
        string? end_date = null,
        [Description("Enable fast mode for reduced latency (valyu only).")]
        bool? fast_mode = null,
        [Description("Natural language category phrase to guide search relevance.")]
        string? category = null,
        [Description("Content length per result. For valyu: short, medium, large, max, or custom integer text.")]
        string? response_length = null,
        [Description("Tune retrieval process based on AI tool call behavior (valyu only).")]
        bool? is_tool_call = null,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var client = serviceProvider.GetRequiredService<APIpieClient>();

                var body = new
                {
                    query,
                    search_provider,
                    geo,
                    lang,
                    results,
                    safeSearch,
                    user,
                    whitelist,
                    blacklist,
                    max_price,
                    start_date,
                    end_date,
                    fast_mode,
                    category,
                    response_length,
                    is_tool_call
                };

                return await client.PostAsync("v1/search", body, cancellationToken)
                    ?? throw new Exception("APIpie returned no response.");
            }));

    [Description("Generate an AI-powered answer based on APIpie Valyu search results.")]
    [McpServerTool(Title = "APIpie search answer", Name = "apipie_search_answer", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> APIpie_Search_Answer(
        [Description("Question to answer.")] string query,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Search provider. Must be valyu.")]
        string search_provider = "valyu",
        [Description("Search type: web, proprietary, or all.")]
        string search = "all",
        [Description("Number of search results to use (1-10).")]
        int results = 5,
        [Description("Enable fast mode for reduced latency.")]
        bool fastMode = false,
        [Description("User identifier for observability and billing.")]
        string? user = null,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var client = serviceProvider.GetRequiredService<APIpieClient>();

                var body = new
                {
                    query,
                    search_provider,
                    search,
                    results,
                    fastMode,
                    user
                };

                return await client.PostAsync("v1/search/answer", body, cancellationToken)
                    ?? throw new Exception("APIpie returned no response.");
            }));

    [Description("Scrape content from a webpage using APIpie.")]
    [McpServerTool(Title = "APIpie scrape webpage", Name = "apipie_scrape_webpage", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> APIpie_Scrape_Webpage(
        [Description("URL to scrape content from.")] string url,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Scrape provider (brightdata or valyu).")]
        string provider = "brightdata",
        [Description("Response format (raw or parsed).")]
        string format = "parsed",
        [Description("Force JavaScript rendering before scraping.")]
        bool? scrape_render = null,
        [Description("Content extraction length. For valyu: short, medium, large, max, or custom integer text.")]
        string? scrape_length = null,
        [Description("Include summary in scrape response when supported.")]
        bool? summary = null,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var client = serviceProvider.GetRequiredService<APIpieClient>();

                var body = new
                {
                    url,
                    provider,
                    format,
                    scrape_render,
                    scrape_length,
                    summary
                };

                return await client.PostAsync("v1/scrape", body, cancellationToken)
                    ?? throw new Exception("APIpie returned no response.");
            }));
}

