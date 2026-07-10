using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Anthropic.Agents;

public static partial class AnthropicAgents
{
    [Description("Add or replace an agent reference in an Anthropic Managed Agent multi-agent coordinator roster using flat primitive inputs. Creates the coordinator topology when missing.")]
    [McpServerTool(
        Title = "Add multi-agent roster agent to Anthropic Agent",
        Name = "anthropic_agents_add_multiagent_agent",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = false)]
    public static async Task<CallToolResult?> AnthropicAgents_AddMultiagentAgentToAgent(
        [Description("Coordinator agent ID to mutate.")] string agentId,
        [Description("Agent ID to add to the coordinator roster.")] string rosterAgentId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional version to pin for the roster agent. Omit to let Anthropic resolve the latest version during update.")] int? rosterAgentVersion = null,

        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var (typed, _, _) = await requestContext.Server.TryElicit(new AnthropicAgentMultiagentAgentMutationRequest
                {
                    AgentId = agentId,
                    RosterAgentId = rosterAgentId,
                    RosterAgentVersion = rosterAgentVersion,

                }, cancellationToken);

                if (string.IsNullOrWhiteSpace(typed.AgentId))
                    throw new ValidationException("agentId is required.");

                ValidateRosterAgent(typed.RosterAgentId, typed.RosterAgentVersion);

                var current = await GetAgentAsync(serviceProvider, typed.AgentId, cancellationToken);
                var multiagent = EnsureMultiagentCoordinator(current);
                var agents = EnsureMultiagentAgentsArray(multiagent);

                RemoveMultiagentAgent(agents, typed.RosterAgentId);

                var rosterAgent = new JsonObject
                {
                    ["type"] = MultiagentAgentType,
                    ["id"] = typed.RosterAgentId
                };

                if (typed.RosterAgentVersion.HasValue)
                    rosterAgent["version"] = typed.RosterAgentVersion.Value;

                agents.Add(rosterAgent);
                ValidateMultiagentRosterLimit(agents);

                var body = CreateVersionedUpdateBody(current);
                body["multiagent"] = multiagent;

                return await UpdateAgentAsync(serviceProvider, typed.AgentId, body, cancellationToken);
            }));

    [Description("Add or replace the self roster entry in an Anthropic Managed Agent multi-agent coordinator topology. Creates the coordinator topology when missing.")]
    [McpServerTool(
        Title = "Add multi-agent self to Anthropic Agent",
        Name = "anthropic_agents_add_multiagent_self",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = false)]
    public static async Task<CallToolResult?> AnthropicAgents_AddMultiagentSelfToAgent(
        [Description("Coordinator agent ID to mutate.")] string agentId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,

        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var (typed, _, _) = await requestContext.Server.TryElicit(new AnthropicAgentMultiagentSelfMutationRequest
                {
                    AgentId = agentId,

                }, cancellationToken);

                if (string.IsNullOrWhiteSpace(typed.AgentId))
                    throw new ValidationException("agentId is required.");

                var current = await GetAgentAsync(serviceProvider, typed.AgentId, cancellationToken);
                var multiagent = EnsureMultiagentCoordinator(current);
                var agents = EnsureMultiagentAgentsArray(multiagent);

                RemoveMultiagentSelf(agents);
                agents.Add(new JsonObject
                {
                    ["type"] = MultiagentSelfType
                });

                ValidateMultiagentRosterLimit(agents);

                var body = CreateVersionedUpdateBody(current);
                body["multiagent"] = multiagent;

                return await UpdateAgentAsync(serviceProvider, typed.AgentId, body, cancellationToken);
            }));

    [Description("Remove an agent reference from an Anthropic Managed Agent multi-agent coordinator roster after explicit typed confirmation. Clears multiagent when the roster becomes empty.")]
    [McpServerTool(
        Title = "Remove multi-agent roster agent from Anthropic Agent",
        Name = "anthropic_agents_remove_multiagent_agent",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> AnthropicAgents_RemoveMultiagentAgentFromAgent(
        [Description("Coordinator agent ID to mutate.")] string agentId,
        [Description("Agent ID to remove from the coordinator roster.")] string rosterAgentId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,

        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                if (string.IsNullOrWhiteSpace(agentId))
                    throw new ValidationException("agentId is required.");

                ValidateRosterAgent(rosterAgentId, null);

                var expected = $"{agentId}:multiagent:agent:{rosterAgentId}";
                await AnthropicManagedAgentsHttp.ConfirmDeleteAsync<AnthropicDeleteAgentItem>(requestContext.Server, expected, cancellationToken);

                var current = await GetAgentAsync(serviceProvider, agentId, cancellationToken);
                var multiagent = EnsureMultiagentCoordinator(current);
                var agents = EnsureMultiagentAgentsArray(multiagent);

                if (!RemoveMultiagentAgent(agents, rosterAgentId))
                    throw new ValidationException($"Multi-agent roster agent '{rosterAgentId}' was not found on agent '{agentId}'.");

                var body = CreateVersionedUpdateBody(current);
                SetMultiagentOrClear(body, multiagent);

                return await UpdateAgentAsync(serviceProvider, agentId, body, cancellationToken);
            }));

    [Description("Remove the self roster entry from an Anthropic Managed Agent multi-agent coordinator roster after explicit typed confirmation. Clears multiagent when the roster becomes empty.")]
    [McpServerTool(
        Title = "Remove multi-agent self from Anthropic Agent",
        Name = "anthropic_agents_remove_multiagent_self",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> AnthropicAgents_RemoveMultiagentSelfFromAgent(
        [Description("Coordinator agent ID to mutate.")] string agentId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,

        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                if (string.IsNullOrWhiteSpace(agentId))
                    throw new ValidationException("agentId is required.");

                var expected = $"{agentId}:multiagent:self";
                await AnthropicManagedAgentsHttp.ConfirmDeleteAsync<AnthropicDeleteAgentItem>(requestContext.Server, expected, cancellationToken);

                var current = await GetAgentAsync(serviceProvider, agentId, cancellationToken);
                var multiagent = EnsureMultiagentCoordinator(current);
                var agents = EnsureMultiagentAgentsArray(multiagent);

                if (!RemoveMultiagentSelf(agents))
                    throw new ValidationException($"Multi-agent self roster entry was not found on agent '{agentId}'.");

                var body = CreateVersionedUpdateBody(current);
                SetMultiagentOrClear(body, multiagent);

                return await UpdateAgentAsync(serviceProvider, agentId, body, cancellationToken);
            }));

    [Description("Clear the multi-agent coordinator topology from an Anthropic Managed Agent after explicit typed confirmation.")]
    [McpServerTool(
        Title = "Clear multi-agent coordinator on Anthropic Agent",
        Name = "anthropic_agents_clear_multiagent",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> AnthropicAgents_ClearMultiagentFromAgent(
        [Description("Coordinator agent ID to mutate.")] string agentId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,

        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                if (string.IsNullOrWhiteSpace(agentId))
                    throw new ValidationException("agentId is required.");

                var expected = $"{agentId}:multiagent";
                await AnthropicManagedAgentsHttp.ConfirmDeleteAsync<AnthropicDeleteAgentItem>(requestContext.Server, expected, cancellationToken);

                var current = await GetAgentAsync(serviceProvider, agentId, cancellationToken);
                if (current["multiagent"] is null)
                    throw new ValidationException($"Agent '{agentId}' does not have multiagent configured.");

                var body = CreateVersionedUpdateBody(current);
                body.Add("multiagent", null);

                return await UpdateAgentAsync(serviceProvider, agentId, body, cancellationToken);
            }));
}
