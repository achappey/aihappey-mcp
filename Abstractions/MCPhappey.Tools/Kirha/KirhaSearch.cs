using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Kirha;

public static class KirhaSearch
{
    [Description("Execute a Kirha search and return summary, raw data, planning, usage, and account metadata as structured content.")]
    [McpServerTool(Title = "Kirha search", Name = "kirha_search", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> Search(
        [Description("Search query string.")] string query,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional vertical identifier.")] string? verticalId = null,
        [Description("Optional summarization object as a JSON string.")] string? summarizationJson = null,
        [Description("Include raw step data in the response.")] bool includeRawData = true,
        [Description("Include planning details in the response.")] bool includePlanning = false,
        [Description("Enable deterministic tool planning.")] bool useDeterministicToolPlanning = false,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(query);
                var client = serviceProvider.GetRequiredService<KirhaClient>();

                var body = new JsonObject
                {
                    ["query"] = query,
                    ["include_raw_data"] = includeRawData,
                    ["include_planning"] = includePlanning,
                    ["use_deterministic_tool_planning"] = useDeterministicToolPlanning
                };

                SetIfHasValue(body, "vertical_id", verticalId);
                SetIfHasValue(body, "summarization", ParseOptionalJsonObject(summarizationJson, nameof(summarizationJson)));

                return await client.PostChatAsync("v1/search", body, cancellationToken)
                    ?? throw new Exception("Kirha returned no response.");
            }));

    [Description("Create a Kirha search plan and return the pending confirmation or clarification plan as structured content.")]
    [McpServerTool(Title = "Kirha search plan", Name = "kirha_search_plan", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> SearchPlan(
        [Description("Search query string.")] string query,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional vertical identifier.")] string? verticalId = null,
        [Description("Enable deterministic tool planning.")] bool? useDeterministicToolPlanning = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(query);
                var client = serviceProvider.GetRequiredService<KirhaClient>();

                var body = new JsonObject
                {
                    ["query"] = query
                };

                SetIfHasValue(body, "vertical_id", verticalId);
                SetIfHasValue(body, "use_deterministic_tool_planning", useDeterministicToolPlanning);

                return await client.PostChatAsync("v1/search/plan", body, cancellationToken)
                    ?? throw new Exception("Kirha returned no response.");
            }));

    [Description("Run a previously created Kirha search plan and return summary, raw data, planning, usage, and account metadata as structured content.")]
    [McpServerTool(Title = "Kirha search plan run", Name = "kirha_search_plan_run", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> SearchPlanRun(
        [Description("Identifier of the search plan to execute.")] string planId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional summarization object as a JSON string.")] string? summarizationJson = null,
        [Description("Include raw step data in the response.")] bool includeRawData = true,
        [Description("Include planning details in the response.")] bool includePlanning = false,
        [Description("Enable deterministic tool planning.")] bool useDeterministicToolPlanning = false,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(planId);
                var client = serviceProvider.GetRequiredService<KirhaClient>();

                var body = new JsonObject
                {
                    ["plan_id"] = planId,
                    ["include_raw_data"] = includeRawData,
                    ["include_planning"] = includePlanning,
                    ["use_deterministic_tool_planning"] = useDeterministicToolPlanning
                };

                SetIfHasValue(body, "summarization", ParseOptionalJsonObject(summarizationJson, nameof(summarizationJson)));

                return await client.PostChatAsync("v1/search/plan/run", body, cancellationToken)
                    ?? throw new Exception("Kirha returned no response.");
            }));

    private static JsonObject? ParseOptionalJsonObject(string? json, string paramName)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        var node = JsonNode.Parse(json);
        return node as JsonObject ?? throw new ArgumentException($"{paramName} must be a JSON object string.", paramName);
    }

    private static void SetIfHasValue<T>(JsonObject body, string key, T? value)
    {
        if (value is null)
            return;

        body[key] = JsonSerializer.SerializeToNode(value);
    }
}
