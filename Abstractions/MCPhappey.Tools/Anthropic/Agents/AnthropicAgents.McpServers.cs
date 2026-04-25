using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Anthropic.Agents;

public static partial class AnthropicAgents
{
    [Description("Add or replace an MCP server entry on an Anthropic Managed Agent using flat primitive fields.")]
    [McpServerTool(
        Title = "Add MCP server to Anthropic Agent",
        Name = "anthropic_agents_add_mcp_server",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = false)]
    public static async Task<CallToolResult?> AnthropicAgents_AddMcpServerToAgent(
        [Description("Agent ID.")] string agentId,
        [Description("Unique MCP server name.")] string serverName,
        [Description("MCP server URL.")] string serverUrl,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional extra anthropic-beta values as comma, semicolon, or newline separated strings.")] string? anthropicBetaCsv = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var (typed, _, _) = await requestContext.Server.TryElicit(new AnthropicAgentMcpServerMutationRequest
                {
                    AgentId = agentId,
                    ServerName = serverName,
                    ServerUrl = serverUrl,
                    AnthropicBetaCsv = anthropicBetaCsv
                }, cancellationToken);

                if (string.IsNullOrWhiteSpace(typed.AgentId))
                    throw new ValidationException("agentId is required.");

                if (string.IsNullOrWhiteSpace(typed.ServerName))
                    throw new ValidationException("serverName is required.");

                if (string.IsNullOrWhiteSpace(typed.ServerUrl))
                    throw new ValidationException("serverUrl is required.");

                var current = await GetAgentAsync(serviceProvider, typed.AgentId, typed.AnthropicBetaCsv, cancellationToken);
                var servers = AnthropicManagedAgentsHttp.CloneArray(current["mcp_servers"]);

                RemoveMcpServer(servers, typed.ServerName);
                servers.Add(new JsonObject
                {
                    ["name"] = typed.ServerName,
                    ["type"] = "url",
                    ["url"] = typed.ServerUrl
                });

                var body = CreateVersionedUpdateBody(current);
                body["mcp_servers"] = servers;

                return await UpdateAgentAsync(serviceProvider, typed.AgentId, typed.AnthropicBetaCsv, body, cancellationToken);
            }));

    [Description("Remove an MCP server entry from an Anthropic Managed Agent after explicit typed confirmation.")]
    [McpServerTool(
        Title = "Remove MCP server from Anthropic Agent",
        Name = "anthropic_agents_remove_mcp_server",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> AnthropicAgents_RemoveMcpServerFromAgent(
        [Description("Agent ID.")] string agentId,
        [Description("MCP server name to remove.")] string serverName,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional extra anthropic-beta values as comma, semicolon, or newline separated strings.")] string? anthropicBetaCsv = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var expected = $"{agentId}:{serverName}";
                await AnthropicManagedAgentsHttp.ConfirmDeleteAsync<AnthropicDeleteAgentItem>(requestContext.Server, expected, cancellationToken);

                var current = await GetAgentAsync(serviceProvider, agentId, anthropicBetaCsv, cancellationToken);
                var servers = AnthropicManagedAgentsHttp.CloneArray(current["mcp_servers"]);

                if (!RemoveMcpServer(servers, serverName))
                    throw new ValidationException($"MCP server '{serverName}' was not found on agent '{agentId}'.");

                var body = CreateVersionedUpdateBody(current);
                body["mcp_servers"] = servers;

                return await UpdateAgentAsync(serviceProvider, agentId, anthropicBetaCsv, body, cancellationToken);
            }));
}
