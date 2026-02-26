using System.ComponentModel;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.AI302;

public static class AI302AcademicPaperSearchPlugin
{
    [Description("Search arXiv papers and optionally translate titles.")]
    [McpServerTool(Title = "302.AI arXiv paper search", Name = "302ai_search_academic_arxiv", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> AI302_Search_Academic_Arxiv(
        [Description("Search terms.")] string query,
        [Description("Maximum results per page.")] int maxResults,
        [Description("Page number, starting at 1.")] int page,
        [Description("Sort mode: -submitted_date or relevance.")] string sortBy,
        [Description("Language for translated titles, e.g. zh, en, ja.")] string language,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Comma-separated list of arXiv IDs to filter by.")] string? idList = null,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var client = serviceProvider.GetRequiredService<AI302Client>();

                var body = new JsonObject
                {
                    ["query"] = query,
                    ["max_results"] = maxResults,
                    ["page"] = page,
                    ["sort_by"] = sortBy,
                    ["language"] = language,
                    ["id_list"] = string.IsNullOrWhiteSpace(idList)
                        ? []
                        : new JsonArray([.. idList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(a => (JsonNode?)a)])
                };

                JsonNode? response = await client.PostAsync("302/search/academic/arxiv", body, cancellationToken);
                return response;
            }));

    [Description("Search Google Scholar-style academic paper results.")]
    [McpServerTool(Title = "302.AI Google paper search", Name = "302ai_search_academic_google", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> AI302_Search_Academic_Google(
        [Description("Search terms.")] string query,
        [Description("Maximum results per page.")] int maxResults,
        [Description("Page number, starting at 1.")] int page,
        [Description("Sort mode.")] string sortBy,
        [Description("Language for result translation/format.")] string language,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var client = serviceProvider.GetRequiredService<AI302Client>();

                var body = new JsonObject
                {
                    ["query"] = query,
                    ["max_results"] = maxResults,
                    ["page"] = page,
                    ["sort_by"] = sortBy,
                    ["language"] = language
                };

                JsonNode? response = await client.PostAsync("302/search/academic/google", body, cancellationToken);
                return response;
            }));
}

