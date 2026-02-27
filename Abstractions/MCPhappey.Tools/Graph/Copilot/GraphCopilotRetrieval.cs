using System.ComponentModel;
using System.Text;
using System.Text.Json;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.Copilot;

public static class GraphCopilotRetrieval
{
    [Description("Retrieve Microsoft 365 Copilot semantic search results using the Microsoft Graph Retrieval API.")]
    [McpServerTool(
        Title = "Microsoft 365 Copilot Retrieval",
        Name = "graph_copilot_retrieval",
        OpenWorld = false,
        ReadOnly = true)]
    public static async Task<CallToolResult?> Graph_CopilotRetrieval(
        RequestContext<CallToolRequestParams> requestContext,
        IServiceProvider serviceProvider,
        [Description("The semantic search query")] string query,
        [Description("Indicates whether extracts should be retrieved from SharePoint, OneDrive, or Copilot connectors. Acceptable values are sharePoint, oneDriveBusiness, and externalItem.")] string dataSource = "sharePoint",
        [Description("The number of results that are returned in the response. Must be between 1 and 25.")] int? maximumNumberOfResults = null,
        [Description("Optional KQL filterExpression (e.g. path:\"https://contoso.sharepoint.com/sites/HR/\")")] string? filterExpression = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var httpClient = await serviceProvider.GetGraphHttpClient(requestContext.Server);

            // Be strict: API is picky
            var max = maximumNumberOfResults ?? 10;
            if (max < 1) max = 1;
            if (max > 25) max = 25;

            var body = new Dictionary<string, object?>
            {
                ["queryString"] = query,
                ["dataSource"] = dataSource, // must be exact casing per docs
                ["maximumNumberOfResults"] = max,
                ["resourceMetadata"] = new[] { "title", "author" }
            };

            if (!string.IsNullOrWhiteSpace(filterExpression))
                body["filterExpression"] = filterExpression;

            var json = JsonSerializer.Serialize(body);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Absolute URL so we never accidentally hit /v1.0/beta/...
            using var resp = await httpClient.PostAsync(
                "https://graph.microsoft.com/beta/copilot/retrieval",
                content,
                cancellationToken);

            var payload = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
            {
                throw new Exception(payload);
            }

            using var doc = JsonDocument.Parse(payload);
            return doc.RootElement.Clone();
        })));


}