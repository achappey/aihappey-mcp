using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Anthropic.Agents;

public static partial class AnthropicAgents
{
    [Description("Add or replace an MCP tool configuration on an Anthropic Managed Agent. The target MCP toolset is created automatically when missing.")]
    [McpServerTool(
        Title = "Add MCP tool to Anthropic Agent",
        Name = "anthropic_agents_add_mcp_tool",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = false)]
    public static async Task<CallToolResult?> AnthropicAgents_AddMcpToolToAgent(
        [Description("Agent ID.")] string agentId,
        [Description("Name of the MCP server referenced by the toolset.")] string mcpServerName,
        [Description("MCP tool name.")] string toolName,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional enabled flag override.")] bool? enabled = null,
        [Description("Optional permission policy. Allowed values: always_allow or always_ask.")] string? permissionPolicy = null,
        
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var (typed, _, _) = await requestContext.Server.TryElicit(new AnthropicAgentMcpToolMutationRequest
                {
                    AgentId = agentId,
                    McpServerName = mcpServerName,
                    ToolName = toolName,
                    Enabled = enabled,
                    PermissionPolicy = permissionPolicy,
                   
                }, cancellationToken);

                if (string.IsNullOrWhiteSpace(typed.AgentId))
                    throw new ValidationException("agentId is required.");

                if (string.IsNullOrWhiteSpace(typed.McpServerName))
                    throw new ValidationException("mcpServerName is required.");

                if (string.IsNullOrWhiteSpace(typed.ToolName))
                    throw new ValidationException("toolName is required.");

                if (!string.IsNullOrWhiteSpace(typed.PermissionPolicy))
                    AnthropicManagedAgentsHttp.ValidatePermissionPolicy(typed.PermissionPolicy);

                var current = await GetAgentAsync(serviceProvider, typed.AgentId,  cancellationToken);
                var tools = AnthropicManagedAgentsHttp.CloneArray(current["tools"]);
                var toolset = EnsureMcpToolset(tools, typed.McpServerName);
                var configs = EnsureConfigsArray(toolset);

                RemoveMcpToolConfig(configs, typed.ToolName);

                var config = new JsonObject
                {
                    ["name"] = typed.ToolName
                };

                if (typed.Enabled.HasValue)
                    config["enabled"] = typed.Enabled.Value;

                if (!string.IsNullOrWhiteSpace(typed.PermissionPolicy))
                    config["permission_policy"] = AnthropicManagedAgentsHttp.BuildPermissionPolicy(typed.PermissionPolicy);

                configs.Add(config);

                var body = CreateVersionedUpdateBody(current);
                body["tools"] = tools;

                return await UpdateAgentAsync(serviceProvider, typed.AgentId,  body, cancellationToken);
            }));

    [Description("Remove an MCP tool configuration from an Anthropic Managed Agent after explicit typed confirmation.")]
    [McpServerTool(
        Title = "Remove MCP tool from Anthropic Agent",
        Name = "anthropic_agents_remove_mcp_tool",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> AnthropicAgents_RemoveMcpToolFromAgent(
        [Description("Agent ID.")] string agentId,
        [Description("Name of the MCP server referenced by the toolset.")] string mcpServerName,
        [Description("MCP tool name to remove.")] string toolName,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var expected = $"{agentId}:{mcpServerName}:{toolName}";
                await AnthropicManagedAgentsHttp.ConfirmDeleteAsync<AnthropicDeleteAgentItem>(requestContext.Server, expected, cancellationToken);

                var current = await GetAgentAsync(serviceProvider, agentId,  cancellationToken);
                var tools = AnthropicManagedAgentsHttp.CloneArray(current["tools"]);
                var toolset = FindMcpToolset(tools, mcpServerName)
                              ?? throw new ValidationException($"Agent '{agentId}' does not contain an MCP toolset for server '{mcpServerName}'.");
                var configs = EnsureConfigsArray(toolset);

                if (!RemoveMcpToolConfig(configs, toolName))
                    throw new ValidationException($"MCP tool config '{toolName}' was not found for server '{mcpServerName}' on agent '{agentId}'.");

                CleanupToolsetIfEmpty(tools, toolset);

                var body = CreateVersionedUpdateBody(current);
                body["tools"] = tools;

                return await UpdateAgentAsync(serviceProvider, agentId,  body, cancellationToken);
            }));

    [Description("Set the default behavior for tools coming from a specific MCP server on an Anthropic Managed Agent using normal form fields instead of raw JSON.")]
    [McpServerTool(
        Title = "Set MCP tool defaults on Anthropic Agent",
        Name = "anthropic_agents_set_mcp_tool_defaults",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = false)]
    public static async Task<CallToolResult?> AnthropicAgents_SetMcpToolDefaults(
        [Description("Agent ID.")] string agentId,
        [Description("Name of the MCP server referenced by the toolset.")] string mcpServerName,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional default enabled flag for tools from this MCP server.")] bool? enabled = null,
        [Description("Optional default permission policy. Allowed values: always_allow or always_ask.")] string? permissionPolicy = null,
        
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var (typed, _, _) = await requestContext.Server.TryElicit(new AnthropicAgentMcpToolsetDefaultsRequest
                {
                    AgentId = agentId,
                    McpServerName = mcpServerName,
                    Enabled = enabled,
                    PermissionPolicy = permissionPolicy,
                   
                }, cancellationToken);

                if (string.IsNullOrWhiteSpace(typed.AgentId))
                    throw new ValidationException("agentId is required.");

                if (string.IsNullOrWhiteSpace(typed.McpServerName))
                    throw new ValidationException("mcpServerName is required.");

                var current = await GetAgentAsync(serviceProvider, typed.AgentId,  cancellationToken);
                var tools = AnthropicManagedAgentsHttp.CloneArray(current["tools"]);
                var toolset = EnsureMcpToolset(tools, typed.McpServerName);

                toolset["default_config"] = BuildToolsetDefaultConfig(typed.Enabled, typed.PermissionPolicy);

                var body = CreateVersionedUpdateBody(current);
                body["tools"] = tools;

                return await UpdateAgentAsync(serviceProvider, typed.AgentId,  body, cancellationToken);
            }));

    [Description("Clear the default behavior for tools coming from a specific MCP server on an Anthropic Managed Agent after explicit typed confirmation.")]
    [McpServerTool(
        Title = "Clear MCP tool defaults on Anthropic Agent",
        Name = "anthropic_agents_clear_mcp_tool_defaults",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> AnthropicAgents_ClearMcpToolDefaults(
        [Description("Agent ID.")] string agentId,
        [Description("Name of the MCP server referenced by the toolset.")] string mcpServerName,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var expected = $"{agentId}:{mcpServerName}:mcp_tool_defaults";
                await AnthropicManagedAgentsHttp.ConfirmDeleteAsync<AnthropicDeleteAgentItem>(requestContext.Server, expected, cancellationToken);

                var current = await GetAgentAsync(serviceProvider, agentId,  cancellationToken);
                var tools = AnthropicManagedAgentsHttp.CloneArray(current["tools"]);
                var toolset = FindMcpToolset(tools, mcpServerName)
                              ?? throw new ValidationException($"Agent '{agentId}' does not contain an MCP toolset for server '{mcpServerName}'.");

                if (!toolset.Remove("default_config"))
                    throw new ValidationException($"Agent '{agentId}' does not have MCP tool defaults configured for server '{mcpServerName}'.");

                CleanupToolsetIfEmpty(tools, toolset);

                var body = CreateVersionedUpdateBody(current);
                body["tools"] = tools;

                return await UpdateAgentAsync(serviceProvider, agentId,  body, cancellationToken);
            }));
}
