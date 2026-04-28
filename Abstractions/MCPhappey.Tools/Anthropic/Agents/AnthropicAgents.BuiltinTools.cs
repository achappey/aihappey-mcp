using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Anthropic.Agents;

public static partial class AnthropicAgents
{
    [Description("Add or replace a built-in agent tool configuration on an Anthropic Managed Agent using flat primitive fields.")]
    [McpServerTool(
        Title = "Add built-in tool to Anthropic Agent",
        Name = "anthropic_agents_add_builtin_tool",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = false)]
    public static async Task<CallToolResult?> AnthropicAgents_AddBuiltinToolToAgent(
        [Description("Agent ID.")] string agentId,
        [Description("Built-in tool name. Allowed values: bash, edit, read, write, glob, grep, web_fetch, web_search.")] string toolName,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional enabled flag override.")] bool? enabled = null,
        [Description("Optional permission policy. Allowed values: always_allow or always_ask.")] string? permissionPolicy = null,
        
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var (typed, _, _) = await requestContext.Server.TryElicit(new AnthropicAgentBuiltinToolMutationRequest
                {
                    AgentId = agentId,
                    ToolName = toolName,
                    Enabled = enabled,
                    PermissionPolicy = permissionPolicy,
                   
                }, cancellationToken);

                if (string.IsNullOrWhiteSpace(typed.AgentId))
                    throw new ValidationException("agentId is required.");

                ValidateBuiltinToolName(typed.ToolName);
                if (!string.IsNullOrWhiteSpace(typed.PermissionPolicy))
                    AnthropicManagedAgentsHttp.ValidatePermissionPolicy(typed.PermissionPolicy);

                var current = await GetAgentAsync(serviceProvider, typed.AgentId,  cancellationToken);
                var tools = AnthropicManagedAgentsHttp.CloneArray(current["tools"]);
                var toolset = EnsureAgentToolset(tools);
                var configs = EnsureConfigsArray(toolset);

                RemoveBuiltinToolConfig(configs, typed.ToolName);

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

    [Description("Remove a built-in agent tool configuration from an Anthropic Managed Agent after explicit typed confirmation.")]
    [McpServerTool(
        Title = "Remove built-in tool from Anthropic Agent",
        Name = "anthropic_agents_remove_builtin_tool",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> AnthropicAgents_RemoveBuiltinToolFromAgent(
        [Description("Agent ID.")] string agentId,
        [Description("Built-in tool name to remove.")] string toolName,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ValidateBuiltinToolName(toolName);

                var expected = $"{agentId}:{toolName}";
                await AnthropicManagedAgentsHttp.ConfirmDeleteAsync<AnthropicDeleteAgentItem>(requestContext.Server, expected, cancellationToken);

                var current = await GetAgentAsync(serviceProvider, agentId,  cancellationToken);
                var tools = AnthropicManagedAgentsHttp.CloneArray(current["tools"]);
                var toolset = FindAgentToolset(tools)
                              ?? throw new ValidationException($"Agent '{agentId}' does not contain an {AgentToolsetType} toolset.");
                var configs = EnsureConfigsArray(toolset);

                if (!RemoveBuiltinToolConfig(configs, toolName))
                    throw new ValidationException($"Built-in tool config '{toolName}' was not found on agent '{agentId}'.");

                CleanupToolsetIfEmpty(tools, toolset);

                var body = CreateVersionedUpdateBody(current);
                body["tools"] = tools;

                return await UpdateAgentAsync(serviceProvider, agentId,  body, cancellationToken);
            }));

    [Description("Set the default built-in tool behavior for an Anthropic Managed Agent using normal form fields instead of raw JSON.")]
    [McpServerTool(
        Title = "Set built-in tool defaults on Anthropic Agent",
        Name = "anthropic_agents_set_builtin_tool_defaults",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = false)]
    public static async Task<CallToolResult?> AnthropicAgents_SetBuiltinToolDefaults(
        [Description("Agent ID.")] string agentId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional default enabled flag for built-in tools.")] bool? enabled = null,
        [Description("Optional default permission policy. Allowed values: always_allow or always_ask.")] string? permissionPolicy = null,
        
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var (typed, _, _) = await requestContext.Server.TryElicit(new AnthropicAgentBuiltinToolsetDefaultsRequest
                {
                    AgentId = agentId,
                    Enabled = enabled,
                    PermissionPolicy = permissionPolicy,
                   
                }, cancellationToken);

                if (string.IsNullOrWhiteSpace(typed.AgentId))
                    throw new ValidationException("agentId is required.");

                var current = await GetAgentAsync(serviceProvider, typed.AgentId,  cancellationToken);
                var tools = AnthropicManagedAgentsHttp.CloneArray(current["tools"]);
                var toolset = EnsureAgentToolset(tools);

                toolset["default_config"] = BuildToolsetDefaultConfig(typed.Enabled, typed.PermissionPolicy);

                var body = CreateVersionedUpdateBody(current);
                body["tools"] = tools;

                return await UpdateAgentAsync(serviceProvider, typed.AgentId,  body, cancellationToken);
            }));

    [Description("Clear the default built-in tool behavior from an Anthropic Managed Agent after explicit typed confirmation.")]
    [McpServerTool(
        Title = "Clear built-in tool defaults on Anthropic Agent",
        Name = "anthropic_agents_clear_builtin_tool_defaults",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> AnthropicAgents_ClearBuiltinToolDefaults(
        [Description("Agent ID.")] string agentId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var expected = $"{agentId}:builtin_tool_defaults";
                await AnthropicManagedAgentsHttp.ConfirmDeleteAsync<AnthropicDeleteAgentItem>(requestContext.Server, expected, cancellationToken);

                var current = await GetAgentAsync(serviceProvider, agentId,  cancellationToken);
                var tools = AnthropicManagedAgentsHttp.CloneArray(current["tools"]);
                var toolset = FindAgentToolset(tools)
                              ?? throw new ValidationException($"Agent '{agentId}' does not contain an {AgentToolsetType} toolset.");

                if (!toolset.Remove("default_config"))
                    throw new ValidationException($"Agent '{agentId}' does not have built-in tool defaults configured.");

                CleanupToolsetIfEmpty(tools, toolset);

                var body = CreateVersionedUpdateBody(current);
                body["tools"] = tools;

                return await UpdateAgentAsync(serviceProvider, agentId,  body, cancellationToken);
            }));
}
