using System.ComponentModel;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.OpperAI.Tools;

public static class OpperAITools
{
    [Description("Search the web with Opper AI and return structured results with title, URL, snippet, and published date where available.")]
    [McpServerTool(Title = "Opper AI web search", Name = "opperai_tools_web_search", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> OpperAI_Tools_WebSearch(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Search query.")] string query,
        [Description("Maximum number of search results to return.")] int? maxResults = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(query);

                var client = serviceProvider.GetRequiredService<OpperAIClient>();
                var payload = new JsonObject
                {
                    ["query"] = query
                };

                if (maxResults.HasValue)
                    payload["max_results"] = maxResults.Value;

                return await client.PostJsonAsync("tools/web/search", payload, cancellationToken);
            }));

    [Description("Fetch a URL with Opper AI and return structured markdown content with title and resolved URL.")]
    [McpServerTool(Title = "Opper AI web fetch", Name = "opperai_tools_web_fetch", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> OpperAI_Tools_WebFetch(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("URL to fetch and convert to markdown.")] string url,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(url);

                var client = serviceProvider.GetRequiredService<OpperAIClient>();
                var payload = new JsonObject
                {
                    ["url"] = url
                };

                return await client.PostJsonAsync("tools/web/fetch", payload, cancellationToken);
            }));
}
