using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using MCPhappey.Core.Services;
using MCPhappey.Tools.OpenAI.AgentsApi;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.OpenAI.Agents;

public static partial class OpenAIAgents
{
    private static string BuildAgentUrl(string agentId)
        => $"{BaseUrl}/{Uri.EscapeDataString(agentId)}";

    private static async Task<JsonObject> GetAgentAsync(IServiceProvider serviceProvider, string agentId, CancellationToken cancellationToken)
    {
        ValidateRequired(agentId, "agentId");
        return await OpenAIAgentsHttp.GetObjectAsync(serviceProvider, BuildAgentUrl(agentId), cancellationToken);
    }

    private static async Task<JsonNode> UpdateAsync(IServiceProvider serviceProvider, string agentId, JsonObject body, CancellationToken cancellationToken)
    {
        ValidateRequired(agentId, "agentId");
        EnsureMutation(body);
        return await OpenAIAgentsHttp.SendAsync(serviceProvider, HttpMethod.Post, BuildAgentUrl(agentId), body, cancellationToken);
    }

    private static async Task<JsonNode> MutateToolsAsync(
        IServiceProvider serviceProvider,
        string agentId,
        Action<JsonArray> mutation,
        CancellationToken cancellationToken)
    {
        var current = await GetAgentAsync(serviceProvider, agentId, cancellationToken);
        var tools = OpenAIAgentsHttp.CloneArray(current["tools"]);
        mutation(tools);
        return await UpdateAsync(serviceProvider, agentId, new JsonObject { ["tools"] = tools }, cancellationToken);
    }

    private static JsonObject? FindTool(JsonArray tools, string type, string? identity = null)
        => tools.OfType<JsonObject>().FirstOrDefault(tool =>
            string.Equals(tool["type"]?.GetValue<string>(), type, StringComparison.Ordinal)
            && (identity is null
                || string.Equals(tool[type == "mcp" ? "server_label" : "name"]?.GetValue<string>(), identity, StringComparison.Ordinal)));

    private static void UpsertTool(JsonArray tools, JsonObject replacement, string type, string? identity = null)
    {
        RemoveTool(tools, type, identity);
        tools.Add(replacement);
    }

    private static bool RemoveTool(JsonArray tools, string type, string? identity = null)
    {
        var removed = false;
        for (var index = tools.Count - 1; index >= 0; index--)
        {
            if (tools[index] is not JsonObject tool
                || !string.Equals(tool["type"]?.GetValue<string>(), type, StringComparison.Ordinal))
                continue;

            var actualIdentity = tool[type == "mcp" ? "server_label" : "name"]?.GetValue<string>();
            if (identity is not null && !string.Equals(actualIdentity, identity, StringComparison.Ordinal))
                continue;

            tools.RemoveAt(index);
            removed = true;
        }
        return removed;
    }

    private static async Task<JsonObject> LoadJsonSchemaAsync(
        IServiceProvider serviceProvider,
        McpServer server,
        string fileUrl,
        CancellationToken cancellationToken)
    {
        ValidateRequired(fileUrl, "schemaFileUrl");
        var files = await serviceProvider.GetRequiredService<DownloadService>()
            .DownloadContentAsync(serviceProvider, server, fileUrl, cancellationToken);
        var file = files.FirstOrDefault() ?? throw new ValidationException("The JSON Schema file could not be downloaded.");
        return OpenAIAgentsHttp.ParseObject(file.Contents.ToString(), "schemaFileUrl content");
    }

    private static void ValidateRequired(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ValidationException($"{parameterName} is required.");
    }

    private static void ValidateEnum(string? value, string parameterName, params string[] allowed)
    {
        if (value is not null && !allowed.Contains(value, StringComparer.Ordinal))
            throw new ValidationException($"{parameterName} must be one of: {string.Join(", ", allowed)}.");
    }

    private static void ValidateMetadata(string key, string? value)
    {
        ValidateRequired(key, "key");
        if (key.Length > 64) throw new ValidationException("Metadata keys cannot exceed 64 characters.");
        if (value is not null && value.Length > 512) throw new ValidationException("Metadata values cannot exceed 512 characters.");
    }

    private static void EnsureMutation(JsonObject body)
    {
        if (body.Count == 0)
            throw new ValidationException("At least one update value is required.");
    }

    private static void SetOptionalString(JsonObject body, string name, string? value)
    {
        if (value is not null) body[name] = value;
    }

    private static async Task SetAgentConfigurationAsync(JsonObject body, AgentScalarRequest input,
        IServiceProvider serviceProvider, McpServer server, CancellationToken cancellationToken)
    {
        var update = input as AgentUpdateScalarRequest;
        if (input.MaxConcurrentSubagents is < 1)
            throw new ValidationException("maxConcurrentSubagents must be positive.");
        if (input.MaxConcurrentSubagents is not null && input.MultiAgentEnabled is false)
            throw new ValidationException("maxConcurrentSubagents requires multiAgentEnabled to be true.");
        if (input.MaxConcurrentSubagents is not null && input.MultiAgentEnabled is null)
            throw new ValidationException("Specify multiAgentEnabled when setting maxConcurrentSubagents.");
        if (input.MultiAgentEnabled is not null)
        {
            var multi = new JsonObject { ["enabled"] = input.MultiAgentEnabled.Value };
            if (input.MaxConcurrentSubagents is not null) multi["max_concurrent_subagents"] = input.MaxConcurrentSubagents;
            body["multi_agent"] = multi;
        }

        ValidateEnum(input.ReasoningEffort, "reasoningEffort", "none", "minimal", "low", "medium", "high", "xhigh", "max");
        ValidateEnum(input.ReasoningSummary, "reasoningSummary", "concise", "detailed", "auto");
        if (update?.ClearReasoning == true && (input.ReasoningEffort is not null || input.ReasoningSummary is not null))
            throw new ValidationException("clearReasoning cannot be combined with reasoning settings.");
        if (update?.ClearReasoning == true) body["reasoning"] = null;
        else if (input.ReasoningEffort is not null || input.ReasoningSummary is not null)
        {
            var reasoning = new JsonObject();
            SetOptionalString(reasoning, "effort", input.ReasoningEffort);
            SetOptionalString(reasoning, "summary", input.ReasoningSummary);
            body["reasoning"] = reasoning;
        }

        ValidateEnum(input.ServiceTier, "serviceTier", "auto", "default", "flex", "priority", "fast", "ultrafast");
        if (update?.ClearServiceTier == true && input.ServiceTier is not null)
            throw new ValidationException("clearServiceTier cannot be combined with serviceTier.");
        if (update?.ClearServiceTier == true) body["service_tier"] = null;
        else SetOptionalString(body, "service_tier", input.ServiceTier);

        ValidateEnum(input.TextVerbosity, "textVerbosity", "low", "medium", "high");
        if (update?.ClearText == true && (input.TextVerbosity is not null || input.TextSchemaFileUrl is not null))
            throw new ValidationException("clearText cannot be combined with text settings.");
        if (update?.ClearText == true) body["text"] = null;
        else if (input.TextVerbosity is not null || input.TextSchemaFileUrl is not null)
        {
            var text = new JsonObject();
            SetOptionalString(text, "verbosity", input.TextVerbosity);
            if (input.TextSchemaFileUrl is not null)
                text["format"] = new JsonObject { ["type"] = "json_schema",
                    ["schema"] = await LoadJsonSchemaAsync(serviceProvider, server, input.TextSchemaFileUrl, cancellationToken) };
            body["text"] = text;
        }
    }

    private static void AddArrayItem(JsonArray items, string value)
    {
        ValidateRequired(value, "value");
        if (!items.Any(item => string.Equals(item?.GetValue<string>(), value, StringComparison.Ordinal)))
            items.Add(value);
    }

    private static void RemoveArrayItem(JsonArray items, string value)
    {
        ValidateRequired(value, "value");
        for (var index = items.Count - 1; index >= 0; index--)
            if (string.Equals(items[index]?.GetValue<string>(), value, StringComparison.Ordinal))
                items.RemoveAt(index);
    }
}
