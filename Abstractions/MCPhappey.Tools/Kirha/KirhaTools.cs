using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Kirha;

public static class KirhaTools
{
    [Description("Execute a Kirha hosted tool and return the tool result, usage, and error state as structured content.")]
    [McpServerTool(Title = "Kirha tool execute", Name = "kirha_tools_execute", ReadOnly = true, OpenWorld = true, UseStructuredContent = true)]
    public static async Task<CallToolResult?> ExecuteTool(
        [Description("Kirha tool name to execute.")] string toolName,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional input object as a JSON string. Defaults to an empty object.")] string? inputJson = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
                var client = serviceProvider.GetRequiredService<KirhaClient>();

                var body = new JsonObject
                {
                    ["tool_name"] = toolName,
                    ["input"] = ParseOptionalJsonObject(inputJson, nameof(inputJson)) ?? new JsonObject()
                };

                return await client.PostChatAsync("v1/tools/execute", body, cancellationToken)
                    ?? throw new Exception("Kirha returned no response.");
            }));

    private static JsonObject? ParseOptionalJsonObject(string? json, string paramName)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        var node = JsonNode.Parse(json);
        return node as JsonObject ?? throw new ArgumentException($"{paramName} must be a JSON object string.", paramName);
    }
}
