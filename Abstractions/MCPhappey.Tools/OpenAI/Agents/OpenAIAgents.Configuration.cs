using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.OpenAI.AgentsApi;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.OpenAI.Agents;

public static partial class OpenAIAgents
{
    [Description("Add or replace one metadata entry on an OpenAI agent without changing other metadata.")]
    [McpServerTool(Title = "Set OpenAI Agent Metadata", Name = "openai_agents_metadata_set", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> OpenAIAgents_MetadataSet(
        string agentId, string key, string value,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await MutateMetadata(agentId, key, value, false, serviceProvider, requestContext, cancellationToken);

    [Description("Remove one metadata entry from an OpenAI agent.")]
    [McpServerTool(Title = "Remove OpenAI Agent Metadata", Name = "openai_agents_metadata_remove", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> OpenAIAgents_MetadataRemove(
        string agentId, string key,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await MutateMetadata(agentId, key, null, true, serviceProvider, requestContext, cancellationToken);

    private static async Task<CallToolResult?> MutateMetadata(
        string agentId, string key, string? value, bool remove,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            var (input, rejected, _) = await requestContext.Server.TryElicit(new AgentMetadataRequest
            { AgentId = agentId, Key = key, Value = value }, cancellationToken);
            if (rejected is not null) return rejected;

            return await requestContext.WithStructuredContent(async () =>
            {
                ValidateRequired(input.AgentId, "agentId");
                ValidateMetadata(input.Key, input.Value);
                var agent = await GetAgentAsync(serviceProvider, input.AgentId, cancellationToken);
                var metadata = OpenAIAgentsHttp.CloneObject(agent["metadata"]);
                if (remove) metadata.Remove(input.Key); else metadata[input.Key] = input.Value;
                if (metadata.Count > 16) throw new ValidationException("OpenAI agents support at most 16 metadata entries.");
                return await UpdateAsync(serviceProvider, input.AgentId, new JsonObject { ["metadata"] = metadata }, cancellationToken);
            });
        });

    [Description("Configure whether an OpenAI agent may create subagents and its concurrency limit.")]
    [McpServerTool(Title = "Configure OpenAI Agent Multi-Agent", Name = "openai_agents_multi_agent_set", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> OpenAIAgents_MultiAgentSet(
        string agentId, bool enabled,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        int? maxConcurrentSubagents = null,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            var (input, rejected, _) = await requestContext.Server.TryElicit(new AgentMultiAgentRequest
            { AgentId = agentId, Enabled = enabled, MaxConcurrentSubagents = maxConcurrentSubagents }, cancellationToken);
            if (rejected is not null) return rejected;
            return await requestContext.WithStructuredContent(async () =>
            {
                ValidateRequired(input.AgentId, "agentId");
                if (!input.Enabled && input.MaxConcurrentSubagents is not null)
                    throw new ValidationException("maxConcurrentSubagents is only valid when multi-agent support is enabled.");
                var config = new JsonObject { ["enabled"] = input.Enabled };
                if (input.MaxConcurrentSubagents is not null) config["max_concurrent_subagents"] = input.MaxConcurrentSubagents;
                return await UpdateAsync(serviceProvider, input.AgentId, new JsonObject { ["multi_agent"] = config }, cancellationToken);
            });
        });

    [Description("Configure OpenAI agent reasoning effort and summary format.")]
    [McpServerTool(Title = "Configure OpenAI Agent Reasoning", Name = "openai_agents_reasoning_set", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> OpenAIAgents_ReasoningSet(
        string agentId,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        string? effort = null, string? summary = null,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            var (input, rejected, _) = await requestContext.Server.TryElicit(new AgentReasoningRequest
            { AgentId = agentId, Effort = effort, Summary = summary }, cancellationToken);
            if (rejected is not null) return rejected;
            return await requestContext.WithStructuredContent(async () =>
            {
                ValidateRequired(input.AgentId, "agentId");
                ValidateEnum(input.Effort, "effort", "none", "minimal", "low", "medium", "high", "xhigh", "max");
                ValidateEnum(input.Summary, "summary", "concise", "detailed", "auto");
                var config = new JsonObject();
                SetOptionalString(config, "effort", input.Effort);
                SetOptionalString(config, "summary", input.Summary);
                EnsureMutation(config);
                return await UpdateAsync(serviceProvider, input.AgentId, new JsonObject { ["reasoning"] = config }, cancellationToken);
            });
        });

    [Description("Set the service tier used for OpenAI agent model requests.")]
    [McpServerTool(Title = "Set OpenAI Agent Service Tier", Name = "openai_agents_service_tier_set", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> OpenAIAgents_ServiceTierSet(
        string agentId, string serviceTier,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ValidateRequired(agentId, "agentId");
                ValidateEnum(serviceTier, "serviceTier", "auto", "default", "flex", "priority", "fast");
                return await UpdateAsync(serviceProvider, agentId, new JsonObject { ["service_tier"] = serviceTier }, cancellationToken);
            }));

    [Description("Configure ordinary-text output and optional verbosity for an OpenAI agent.")]
    [McpServerTool(Title = "Set OpenAI Agent Text Output", Name = "openai_agents_text_set", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> OpenAIAgents_TextSet(
        string agentId,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        string? verbosity = null,
        CancellationToken cancellationToken = default)
        => await SetTextAsync(agentId, verbosity, null, serviceProvider, requestContext, cancellationToken);

    [Description("Configure JSON Schema constrained output for an OpenAI agent from a downloadable JSON Schema file.")]
    [McpServerTool(Title = "Set OpenAI Agent JSON Schema Output", Name = "openai_agents_text_json_schema_set", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> OpenAIAgents_TextJsonSchemaSet(
        string agentId, string schemaFileUrl,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        string? verbosity = null,
        CancellationToken cancellationToken = default)
        => await SetTextAsync(agentId, verbosity, schemaFileUrl, serviceProvider, requestContext, cancellationToken);

    private static async Task<CallToolResult?> SetTextAsync(
        string agentId, string? verbosity, string? schemaFileUrl,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            var (input, rejected, _) = await requestContext.Server.TryElicit(new AgentTextRequest
            { AgentId = agentId, Verbosity = verbosity, SchemaFileUrl = schemaFileUrl }, cancellationToken);
            if (rejected is not null) return rejected;
            return await requestContext.WithStructuredContent(async () =>
            {
                ValidateRequired(input.AgentId, "agentId");
                ValidateEnum(input.Verbosity, "verbosity", "low", "medium", "high");
                var text = new JsonObject();
                SetOptionalString(text, "verbosity", input.Verbosity);
                text["format"] = input.SchemaFileUrl is null
                    ? new JsonObject { ["type"] = "text" }
                    : new JsonObject
                    {
                        ["type"] = "json_schema",
                        ["schema"] = await LoadJsonSchemaAsync(serviceProvider, requestContext.Server, input.SchemaFileUrl, cancellationToken)
                    };
                return await UpdateAsync(serviceProvider, input.AgentId, new JsonObject { ["text"] = text }, cancellationToken);
            });
        });
}
