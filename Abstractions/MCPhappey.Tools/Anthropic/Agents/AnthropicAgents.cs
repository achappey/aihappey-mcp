using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Anthropic.Agents;

public static partial class AnthropicAgents
{
    private const string BaseUrl = $"{AnthropicManagedAgentsHttp.ApiBaseUrl}/v1/agents";
    private const string AgentToolsetType = "agent_toolset_20260401";
    private const string McpToolsetType = "mcp_toolset";
    private const string CustomToolType = "custom";

    [Description("Please confirm to delete: {0}")]
    public sealed class AnthropicDeleteAgentItem : IHasName
    {
        [JsonPropertyName("name")]
        [Description("Confirm deletion.")]
        public string Name { get; set; } = string.Empty;
    }

    [Description("Create an Anthropic Managed Agent with only flat primitive fields. Add metadata, skills, MCP servers, and tools with the dedicated Anthropic agent mutation tools.")]
    [McpServerTool(
        Title = "Create Anthropic Agent",
        Name = "anthropic_agents_create",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = false)]
    public static async Task<CallToolResult?> AnthropicAgents_Create(
        [Description("Human-readable name for the agent.")] string name,
        [Description("Model identifier such as claude-sonnet-4-6.")] string modelId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional description.")] string? description = null,
        [Description("Optional model speed. Allowed values: standard or fast.")] string? modelSpeed = null,
        [Description("Optional system prompt.")] string? system = null,
        
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var (typed, _, _) = await requestContext.Server.TryElicit(new AnthropicCreateAgentRequest
                {
                    Name = name,
                    ModelId = modelId,
                    Description = description,
                    ModelSpeed = modelSpeed,
                    System = system
                }, cancellationToken);

                if (string.IsNullOrWhiteSpace(typed.Name))
                    throw new ValidationException("name is required.");

                var body = new JsonObject
                {
                    ["name"] = typed.Name,
                    ["model"] = AnthropicManagedAgentsHttp.BuildModelNode(typed.ModelId, typed.ModelSpeed)
                };

                SetStringIfProvided(body, "description", typed.Description);
                SetStringIfProvided(body, "system", typed.System);

                return await AnthropicManagedAgentsHttp.SendAsync(
                    serviceProvider,
                    HttpMethod.Post,
                    BaseUrl,
                    body,
                    
                    cancellationToken);
            }));

    [Description("Update only the flat scalar fields on an Anthropic Managed Agent. Use dedicated Anthropic agent tools for metadata, skills, MCP servers, and tool configurations.")]
    [McpServerTool(
        Title = "Update Anthropic Agent",
        Name = "anthropic_agents_update",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = false)]
    public static async Task<CallToolResult?> AnthropicAgents_Update(
        [Description("Agent ID to update.")] string agentId,
        [Description("Current agent version used for optimistic concurrency.")] int version,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional updated name. Omit to preserve the current name.")] string? name = null,
        [Description("Optional updated description. Provide an empty string to clear.")] string? description = null,
        [Description("Optional updated model identifier. Omit to preserve the current model.")] string? modelId = null,
        [Description("Optional updated model speed. Allowed values: standard or fast. Requires modelId when provided.")] string? modelSpeed = null,
        [Description("Optional updated system prompt. Provide an empty string to clear.")] string? system = null,
        
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var (typed, _, _) = await requestContext.Server.TryElicit(new AnthropicUpdateAgentRequest
                {
                    AgentId = agentId,
                    Version = version,
                    Name = name,
                    Description = description,
                    ModelId = modelId,
                    ModelSpeed = modelSpeed,
                    System = system
                }, cancellationToken);

                if (string.IsNullOrWhiteSpace(typed.AgentId))
                    throw new ValidationException("agentId is required.");

                if (typed.Version < 1)
                    throw new ValidationException("version must be at least 1.");

                if (typed.Name is not null && string.IsNullOrWhiteSpace(typed.Name))
                    throw new ValidationException("name cannot be empty. Omit it to preserve the current name.");

                if (typed.ModelId is not null && string.IsNullOrWhiteSpace(typed.ModelId))
                    throw new ValidationException("modelId cannot be empty. Omit it to preserve the current model.");

                var body = new JsonObject
                {
                    ["version"] = typed.Version
                };

                if (typed.Name is not null) body["name"] = typed.Name;
                if (typed.Description is not null) body["description"] = typed.Description;
                if (typed.System is not null) body["system"] = typed.System;

                if (!string.IsNullOrWhiteSpace(typed.ModelId))
                    body["model"] = AnthropicManagedAgentsHttp.BuildModelNode(typed.ModelId, typed.ModelSpeed);
                else if (!string.IsNullOrWhiteSpace(typed.ModelSpeed))
                    throw new ValidationException("modelId is required when modelSpeed is provided.");

                return await UpdateAgentAsync(serviceProvider, typed.AgentId,  body, cancellationToken);
            }));

    [Description("Archive an Anthropic Managed Agent. The archive request is confirmed through elicitation before execution.")]
    [McpServerTool(
        Title = "Archive Anthropic Agent",
        Name = "anthropic_agents_archive",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> AnthropicAgents_Archive(
        [Description("Agent ID to archive.")] string agentId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var (typed, _, _) = await requestContext.Server.TryElicit(new AnthropicArchiveAgentRequest
                {
                    AgentId = agentId,                  
                }, cancellationToken);

                if (string.IsNullOrWhiteSpace(typed.AgentId))
                    throw new ValidationException("agentId is required.");

                return await AnthropicManagedAgentsHttp.SendAsync(
                    serviceProvider,
                    HttpMethod.Post,
                    $"{BaseUrl}/{Uri.EscapeDataString(typed.AgentId)}/archive",
                    null,
                    
                    cancellationToken);
            }));
}
