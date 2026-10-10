using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Anthropic.Agents;

public static partial class AnthropicAgents
{
    private const string WorkflowMultiagentType = "multiagent_20261001";

    [Description("Configure native Anthropic Managed Agent workflows using flat inputs. Omitted settings are preserved; no fields are reset. Creates multiagent_20261001 when missing, but never migrates a legacy coordinator. Workflows and subagents default to enabled on this type. Existing sessions are unaffected. Reserved ant__ custom tool names must be renamed or removed before updating.")]
    [McpServerTool(
        Title = "Configure workflows on Anthropic Agent",
        Name = "anthropic_agents_configure_workflows",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = false)]
    public static async Task<CallToolResult?> AnthropicAgents_ConfigureWorkflows(
        [Description("Agent ID to configure.")] string agentId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional workflow enabled flag. Omit to preserve the stored setting, or use the native enabled default when multiagent is missing.")] bool? workflowsEnabled = null,
        [Description("Optional workflow inline-agent enabled flag. Disabling requires at least one predefined workflow agent. Requires workflows to be enabled.")] bool? inlineAgentsEnabled = null,
        [Description("Optional subagent delegation enabled flag. Set false with workflowsEnabled true for workflows-only mode. Omit to preserve the stored setting.")] bool? subagentsEnabled = null,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var typed = requestContext.Elicit(new AnthropicAgentConfigureWorkflowsRequest
                {
                    AgentId = agentId,
                    WorkflowsEnabled = workflowsEnabled,
                    InlineAgentsEnabled = inlineAgentsEnabled,
                    SubagentsEnabled = subagentsEnabled
                });

                var current = await GetAgentAsync(serviceProvider, typed.AgentId, cancellationToken);
                var body = BuildConfigureWorkflowsUpdate(current, typed);

                return await UpdateAgentAsync(serviceProvider, typed.AgentId, body, cancellationToken);
            }));

    [Description("Add, replace, or remove one native workflow predefined agent or self reference using flat inputs. Fetches the current configuration and replaces only workflows.predefined_agents, preserving other settings. Adding never re-enables disabled workflows. Self also matches its resolved agent ID/version representation. Removal uses typed confirmation. Legacy coordinator configurations are not migrated.")]
    [McpServerTool(
        Title = "Update workflow agents on Anthropic Agent",
        Name = "anthropic_agents_update_workflow_agents",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> AnthropicAgents_UpdateWorkflowAgents(
        [Description("Agent ID whose workflow list is being updated.")] string agentId,
        [Description("List operation: add (adds or replaces the matching reference) or remove.")] string operation,
        [Description("Reference type: agent or self. Self refers to the agent being updated.")] string referenceType,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Predefined agent ID. Required for an agent reference; omit for self.")] string? predefinedAgentId = null,
        [Description("Optional version pin for an agent reference when adding. Omit to resolve the latest version. Self is resolved by Anthropic; omit for self and removal.")] int? predefinedAgentVersion = null,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var typed = requestContext.Elicit(new AnthropicAgentUpdateWorkflowAgentsRequest
                {
                    AgentId = agentId,
                    Operation = operation,
                    ReferenceType = referenceType,
                    PredefinedAgentId = predefinedAgentId,
                    PredefinedAgentVersion = predefinedAgentVersion
                });

                var current = await GetAgentAsync(serviceProvider, typed.AgentId, cancellationToken);
                var body = BuildWorkflowAgentsUpdate(current, typed);

                if (string.Equals(typed.Operation, "remove", StringComparison.OrdinalIgnoreCase))
                {
                    var reference = string.Equals(typed.ReferenceType, MultiagentSelfType, StringComparison.OrdinalIgnoreCase)
                        ? MultiagentSelfType
                        : $"agent:{typed.PredefinedAgentId}";
                    var expected = $"{typed.AgentId}:workflows:{reference}";
                    await AnthropicManagedAgentsHttp.ConfirmDeleteAsync<AnthropicDeleteAgentItem>(requestContext, expected, cancellationToken);
                }

                return await UpdateAgentAsync(serviceProvider, typed.AgentId, body, cancellationToken);
            }));

    private static JsonObject BuildConfigureWorkflowsUpdate(
        JsonObject current,
        AnthropicAgentConfigureWorkflowsRequest request)
    {
        var stored = GetWorkflowMultiagent(current);
        if (request.WorkflowsEnabled is null && request.InlineAgentsEnabled is null && request.SubagentsEnabled is null)
            throw new ValidationException("Provide workflowsEnabled, inlineAgentsEnabled, and/or subagentsEnabled.");

        var workflows = stored?["workflows"] as JsonObject;
        var workflowsEnabled = request.WorkflowsEnabled ?? !IsWorkflowSettingDisabled(workflows);
        if (request.InlineAgentsEnabled.HasValue && !workflowsEnabled)
            throw new ValidationException("Inline agents can only be configured when workflows are enabled.");

        if (request.InlineAgentsEnabled == false && (workflows?["predefined_agents"] as JsonArray)?.Count is not > 0)
            throw new ValidationException("Disabling workflow inline agents requires at least one predefined agent.");

        var multiagent = new JsonObject { ["type"] = WorkflowMultiagentType };
        if (request.WorkflowsEnabled.HasValue || request.InlineAgentsEnabled.HasValue)
        {
            var patch = new JsonObject { ["type"] = workflowsEnabled ? "enabled" : "disabled" };
            if (request.InlineAgentsEnabled.HasValue)
                patch["inline_agents"] = WorkflowEnabledSetting(request.InlineAgentsEnabled.Value);
            multiagent["workflows"] = patch;
        }

        if (request.SubagentsEnabled.HasValue)
            multiagent["subagents"] = WorkflowEnabledSetting(request.SubagentsEnabled.Value);

        var body = CreateVersionedUpdateBody(current);
        body["multiagent"] = multiagent;
        return body;
    }

    private static JsonObject BuildWorkflowAgentsUpdate(
        JsonObject current,
        AnthropicAgentUpdateWorkflowAgentsRequest request)
    {
        var stored = GetWorkflowMultiagent(current);
        var adding = string.Equals(request.Operation, "add", StringComparison.OrdinalIgnoreCase);
        if (!adding && !string.Equals(request.Operation, "remove", StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("operation must be 'add' or 'remove'.");

        var self = string.Equals(request.ReferenceType, MultiagentSelfType, StringComparison.OrdinalIgnoreCase);
        if (!self && !string.Equals(request.ReferenceType, MultiagentAgentType, StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("referenceType must be 'agent' or 'self'.");

        if (self)
        {
            if (request.PredefinedAgentId is not null || request.PredefinedAgentVersion.HasValue)
                throw new ValidationException("Omit predefinedAgentId and predefinedAgentVersion for self; Anthropic resolves the self reference.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.PredefinedAgentId))
                throw new ValidationException("predefinedAgentId is required for an agent reference.");
            if (request.PredefinedAgentVersion is < 1)
                throw new ValidationException("predefinedAgentVersion must be at least 1 when provided.");
        }

        if (!adding && request.PredefinedAgentVersion.HasValue)
            throw new ValidationException("Omit predefinedAgentVersion when removing a reference.");

        var workflows = stored?["workflows"] as JsonObject;
        if (IsWorkflowSettingDisabled(workflows))
            throw new ValidationException("Workflows are disabled. Enable workflows explicitly before updating predefined agents.");

        var agents = AnthropicManagedAgentsHttp.CloneArray(workflows?["predefined_agents"]);
        var targetId = self ? request.AgentId : request.PredefinedAgentId;
        var removed = false;
        for (var index = agents.Count - 1; index >= 0; index--)
        {
            if (agents[index] is not JsonObject agent)
                continue;

            var type = agent["type"]?.GetValue<string>();
            var matches = string.Equals(type, MultiagentAgentType, StringComparison.Ordinal)
                && string.Equals(agent["id"]?.GetValue<string>(), targetId, StringComparison.Ordinal);
            if (string.Equals(type, MultiagentSelfType, StringComparison.Ordinal)
                && string.Equals(targetId, request.AgentId, StringComparison.Ordinal))
                matches = true;

            if (!matches)
                continue;

            agents.RemoveAt(index);
            removed = true;
        }

        if (adding)
        {
            var reference = new JsonObject { ["type"] = self ? MultiagentSelfType : MultiagentAgentType };
            if (!self)
            {
                reference["id"] = request.PredefinedAgentId;
                if (request.PredefinedAgentVersion.HasValue)
                    reference["version"] = request.PredefinedAgentVersion.Value;
            }
            agents.Add(reference);
        }
        else if (!removed)
            throw new ValidationException($"Workflow reference '{targetId}' was not found on agent '{request.AgentId}'.");

        if (agents.Count > MultiagentRosterLimit)
            throw new ValidationException($"workflows.predefined_agents cannot contain more than {MultiagentRosterLimit} entries.");
        if (agents.Count == 0 && IsWorkflowSettingDisabled(workflows?["inline_agents"] as JsonObject))
            throw new ValidationException("At least one predefined workflow agent is required while inline agents are disabled.");

        var body = CreateVersionedUpdateBody(current);
        body["multiagent"] = new JsonObject
        {
            ["type"] = WorkflowMultiagentType,
            ["workflows"] = new JsonObject
            {
                ["type"] = "enabled",
                ["predefined_agents"] = agents
            }
        };
        return body;
    }

    private static JsonObject? GetWorkflowMultiagent(JsonObject current)
    {
        if (current["multiagent"] is null)
            return null;

        if (current["multiagent"] is not JsonObject multiagent
            || !string.Equals(multiagent["type"]?.GetValue<string>(), WorkflowMultiagentType, StringComparison.Ordinal))
            throw new ValidationException($"Workflow tools require '{WorkflowMultiagentType}'. Existing coordinator or other multiagent configurations must be migrated explicitly.");

        return multiagent;
    }

    private static bool IsWorkflowSettingDisabled(JsonObject? setting)
        => string.Equals(setting?["type"]?.GetValue<string>(), "disabled", StringComparison.Ordinal);

    private static JsonObject WorkflowEnabledSetting(bool enabled)
        => new() { ["type"] = enabled ? "enabled" : "disabled" };
}
