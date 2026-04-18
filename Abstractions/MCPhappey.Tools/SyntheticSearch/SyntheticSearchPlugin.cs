using System.ComponentModel;
using System.Text.Json;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.SyntheticSearch;

public static class SyntheticSearchPlugin
{
    [Description("Search the web with Synthetic's zero-data-retention search API for coding agents.")]
    [McpServerTool(Idempotent = false, OpenWorld = true,
        Destructive = false, ReadOnly = true,
        Title = "Synthetic search")]
    public static async Task<CallToolResult?> Synthetic_Search(
      [Description("The search query to execute.")] string query,
      IServiceProvider serviceProvider,
      RequestContext<CallToolRequestParams> requestContext,
      CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var syntheticSearch = serviceProvider.GetRequiredService<SyntheticSearchClient>();
        var body = JsonSerializer.Serialize(new
        {
            query
        });

        using var resp = await syntheticSearch.SearchAsync(body, cancellationToken);
        var json = await resp.Content.ReadAsStringAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"{resp.StatusCode}: {json}");

        return json;
    }));
}
