using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Google.Agents;

public static class GoogleAgents
{
    [Description("Please confirm to delete: {0}")]
    public sealed class DeleteAgentConfirmation : IHasName
    {
        [JsonPropertyName("name"), Description("Type the Google agent ID to confirm permanent deletion.")]
        public string Name { get; set; } = string.Empty;
    }

    [Description("Create a managed Google Agent. Supply a unique ID; baseAgent defaults to antigravity-preview-09-2026. Omit model for Google's default (gemini-3.8-flash). For web search set googleWebSearch=true. Use Google Agent Editor for complex environments and tool lists.")]
    [McpServerTool(Title = "Create Google Agent", Name = "google_agents_create", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> Create(
        IServiceProvider services,
        RequestContext<CallToolRequestParams> context,
        string? id = null,
        string? description = null,
        string? systemInstruction = null,
        string? baseAgent = null,
        string? model = null,
        long? maxTotalTokens = null,
        bool codeExecution = false,
        bool googleWebSearch = false,
        bool googleImageSearch = false,
        bool urlContext = false,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await context.WithStructuredContent(async () =>
        {
            var body = BuildScalars(id, description, systemInstruction, baseAgent, model, maxTotalTokens);
            var tools = new JsonArray();
            if (codeExecution) tools.Add(new JsonObject { ["type"] = "code_execution" });
            if (googleWebSearch || googleImageSearch)
            {
                var searchTypes = new JsonArray();
                if (googleWebSearch) searchTypes.Add("web_search");
                if (googleImageSearch) searchTypes.Add("image_search");
                tools.Add(new JsonObject { ["type"] = "google_search", ["search_types"] = searchTypes });
            }
            if (urlContext) tools.Add(new JsonObject { ["type"] = "url_context" });
            if (tools.Count > 0) body["tools"] = tools;
            return await CreateValidatedAsync(services, body, cancellationToken);
        }));

    [Description("Create a Google Agent with one remote-environment source. Use Google Agent Editor for multiple sources, environment variables, and network rules.")]
    [McpServerTool(Title = "Create Google Agent With Source", Name = "google_agents_create_with_source", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> CreateWithSource(
        string sourceType,
        string target,
        IServiceProvider services,
        RequestContext<CallToolRequestParams> context,
        string? source = null,
        string? content = null,
        string? encoding = null,
        string? id = null,
        string? description = null,
        string? systemInstruction = null,
        string? model = null,
        long? maxTotalTokens = null,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await context.WithStructuredContent(async () =>
        {
            var body = BuildScalars(id, description, systemInstruction, null, model, maxTotalTokens);
            var sourceNode = new JsonObject { ["type"] = sourceType, ["target"] = target };
            Add(sourceNode, "source", source);
            Add(sourceNode, "content", content);
            Add(sourceNode, "encoding", encoding);
            body["base_environment"] = new JsonObject
            {
                ["type"] = "remote",
                ["sources"] = new JsonArray(sourceNode)
            };
            return await CreateValidatedAsync(services, body, cancellationToken);
        }));

    [Description("Create a Google Agent forked from an existing environment ID.")]
    [McpServerTool(Title = "Create Google Agent From Environment", Name = "google_agents_create_from_environment", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> CreateFromEnvironment(
        string environmentId,
        IServiceProvider services,
        RequestContext<CallToolRequestParams> context,
        string? id = null,
        string? description = null,
        string? systemInstruction = null,
        string? baseAgent = null,
        string? model = null,
        long? maxTotalTokens = null,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await context.WithStructuredContent(async () =>
        {
            Required(environmentId, "environmentId");
            var body = BuildScalars(id, description, systemInstruction, baseAgent, model, maxTotalTokens);
            body["base_environment"] = environmentId;
            return await CreateValidatedAsync(services, body, cancellationToken);
        }));

    [Description("Create a Google Agent from a complete JSON payload stored in a SharePoint or OneDrive file.")]
    [McpServerTool(Title = "Create Google Agent From JSON File", Name = "google_agents_create_from_file", ReadOnly = false, OpenWorld = true, Destructive = false)]
    public static async Task<CallToolResult?> CreateFromFile(
        string payloadFileUrl,
        IServiceProvider services,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await context.WithStructuredContent(async () =>
        {
            EnsureMicrosoftFileUrl(payloadFileUrl);
            var file = (await services.GetRequiredService<DownloadService>()
                .DownloadContentAsync(services, context.Server, payloadFileUrl, cancellationToken)).FirstOrDefault()
                ?? throw new ValidationException("The Google Agent JSON file could not be downloaded.");
            var body = GoogleAgentDocument.Parse(file.Contents.ToString(), "payload file");
            return await CreateValidatedAsync(services, body, cancellationToken);
        }));

    [Description("Permanently delete a Google Agent after explicit typed confirmation.")]
    [McpServerTool(Title = "Delete Google Agent", Name = "google_agents_delete", ReadOnly = false, OpenWorld = false, Destructive = true)]
    public static async Task<CallToolResult?> Delete(
        string agentId,
        IServiceProvider services,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            Required(agentId, "agentId");
            return await context.ConfirmAndDeleteAsync<DeleteAgentConfirmation>(
                agentId,
                async ct => _ = await services.GetRequiredService<GoogleAgentsClient>().DeleteAsync(agentId, ct),
                $"Google agent '{agentId}' deleted successfully.",
                cancellationToken);
        });

    internal static async Task<JsonNode> CreateValidatedAsync(
        IServiceProvider services,
        JsonObject body,
        CancellationToken cancellationToken)
    {
        GoogleAgentDocument.EnsureValid(body);
        return await services.GetRequiredService<GoogleAgentsClient>().CreateAsync(body, cancellationToken);
    }

    private static JsonObject BuildScalars(
        string? id,
        string? description,
        string? systemInstruction,
        string? baseAgent,
        string? model,
        long? maxTotalTokens)
    {
        if (maxTotalTokens < 1) throw new ValidationException("maxTotalTokens must be positive.");
        var body = new JsonObject { ["base_agent"] = baseAgent ?? GoogleAgentDocument.SupportedBaseAgent };
        Add(body, "id", id);
        Add(body, "description", description);
        Add(body, "system_instruction", systemInstruction);
        if (model is not null || maxTotalTokens is not null)
        {
            var config = new JsonObject { ["type"] = "antigravity" };
            Add(config, "model", model);
            if (maxTotalTokens is not null) config["max_total_tokens"] = maxTotalTokens.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            body["agent_config"] = config;
        }
        return body;
    }

    internal static void EnsureMicrosoftFileUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || !(uri.Host.EndsWith(".sharepoint.com", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Equals("1drv.ms", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Equals("onedrive.live.com", StringComparison.OrdinalIgnoreCase)))
            throw new ValidationException("payloadFileUrl must be an HTTPS SharePoint or OneDrive URL.");
    }

    internal static void Required(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ValidationException($"{name} is required.");
    }

    internal static void Add(JsonObject target, string field, string? value)
    {
        if (value is not null) target[field] = value;
    }
}
