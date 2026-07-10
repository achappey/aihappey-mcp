using System.ComponentModel;
using DocumentFormat.OpenXml.Wordprocessing;
using MCPhappey.Common;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.AI;

public static class ChatApp
{
    

    [Description("Get MCP server usage statistics")]
    [McpServerTool(Title = "Get server usage statistics",
         ReadOnly = true)]
    public static async Task<CallToolResult?> ChatApp_GetMcpServerStats(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
    {
        var config = serviceProvider.GetService<McpApplicationInsights>();
        var serverList = serviceProvider.GetService<IReadOnlyList<ServerConfig>>();

        if (config == null || string.IsNullOrWhiteSpace(config.AppId) || string.IsNullOrWhiteSpace(config.AppKey))
            return "Stats not configured".ToErrorCallToolResponse();

        var baseline = new[] { "chatapp", "/token", "/register", "/message", "/sse", ".com/mcp" };

        var hiddenServers = serverList?
            .Where(x => x.Server.Hidden == true)
            .Select(x => x.Server.ServerInfo.Name.ToLowerInvariant())
            .ToArray() ?? [];

        var hasAnyValues = baseline.Concat(hiddenServers)
                                   // escape any embedded quotes, then wrap in quotes for Kusto
                                   .Select(s => $"\"{s.Replace("\"", "\\\"")}\"");

        var hasAnyList = string.Join(", ", hasAnyValues);

        // ------------------------------------------------------------------------
        // Kusto query – 90 days, per‑URL totals, top 10 000
        // ------------------------------------------------------------------------
        var kql = $@"
            requests
            | where timestamp > ago(14d)
            | where name startswith ""POST""
            | where not(tolower(url) has_any({hasAnyList}))
            | summarize TotalRequests = count() by Url = url
            | order by TotalRequests desc
            | take 10000";

        // --- Build REST call -------------------------------------------------------
        var queryUri =
            $"https://api.applicationinsights.io/v1/apps/{config.AppId}/query?query={Uri.EscapeDataString(kql)}";

        var httpFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var http = httpFactory.CreateClient();

        http.DefaultRequestHeaders.Add("x-api-key", config.AppKey);

        // --- Execute and handle response ------------------------------------------
        using var response = await http.GetAsync(queryUri, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var details = await response.Content.ReadAsStringAsync(cancellationToken);

            throw new Exception(details);
        }

        var stream = await response.Content.ReadAsStringAsync(cancellationToken);

        return stream.ToJsonCallToolResponse(queryUri);
    });

    [Description("Get available completions that can be used in prompts and can be completed during using those prompts.")]
    [McpServerTool(Title = "Get prompt completions",
        ReadOnly = true)]
    public static async Task<CallToolResult> ChatApp_GetCompletions(
       IServiceProvider serviceProvider)
    {
        var config = serviceProvider.GetServices<IAutoCompletion>();

        return await Task.FromResult(string.Join(",", config.SelectMany(z => z.GetArguments(serviceProvider))).ToTextCallToolResponse());
    }
}


public class McpApplicationInsights
{
    public string AppId { get; set; } = default!;
    public string AppKey { get; set; } = default!;
}