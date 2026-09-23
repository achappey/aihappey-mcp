using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Google.AgentEditor;

public static partial class GoogleAgentEditor
{
    [Description("Set an existing environment ID as the draft's base environment. This replaces any inline remote-environment configuration.")]
    [McpServerTool(Title = "Set Google Agent Draft Environment", Name = "google_agent_editor_environment_set", ReadOnly = false, Idempotent = true, OpenWorld = false, Destructive = false)]
    public static Task<CallToolResult?> SetEnvironment(
        string draftName,
        string environmentId,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default)
        => WithDraftMutation(draftName, context, document =>
        {
            Agents.GoogleAgents.Required(environmentId, "environmentId");
            document["base_environment"] = environmentId;
        }, cancellationToken);

    [Description("Create or configure the draft's inline remote environment and optional environment ID.")]
    [McpServerTool(Title = "Configure Google Agent Draft Remote Environment", Name = "google_agent_editor_remote_environment_set", ReadOnly = false, Idempotent = true, OpenWorld = false, Destructive = false)]
    public static Task<CallToolResult?> SetRemoteEnvironment(
        string draftName,
        RequestContext<CallToolRequestParams> context,
        string? environmentId = null,
        bool clearEnvironmentId = false,
        CancellationToken cancellationToken = default)
        => WithDraftMutation(draftName, context, document =>
        {
            var environment = EnsureRemoteEnvironment(document);
            Set(environment, "environment_id", environmentId, clearEnvironmentId);
        }, cancellationToken);

    [Description("Add or replace one environment variable using either a plain value or a server-managed credential ID.")]
    [McpServerTool(Title = "Set Google Agent Draft Environment Variable", Name = "google_agent_editor_env_set", ReadOnly = false, Idempotent = true, OpenWorld = false, Destructive = false)]
    public static Task<CallToolResult?> SetEnvironmentVariable(
        string draftName,
        string variableName,
        RequestContext<CallToolRequestParams> context,
        string? value = null,
        string? credential = null,
        CancellationToken cancellationToken = default)
        => WithDraftMutation(draftName, context, document =>
        {
            Agents.GoogleAgents.Required(variableName, "variableName");
            if ((value is null) == (credential is null))
                throw new ValidationException("Provide exactly one of value or credential.");
            var env = EnsureObject(EnsureRemoteEnvironment(document), "env");
            env[variableName] = value is not null
                ? new JsonObject { ["value"] = value }
                : new JsonObject { ["credential"] = credential };
        }, cancellationToken);

    [Description("Remove one environment variable from a Google Agent draft.")]
    [McpServerTool(Title = "Remove Google Agent Draft Environment Variable", Name = "google_agent_editor_env_remove", ReadOnly = false, Idempotent = true, OpenWorld = false, Destructive = false)]
    public static Task<CallToolResult?> RemoveEnvironmentVariable(
        string draftName,
        string variableName,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default)
        => WithDraftMutation(draftName, context, document =>
        {
            Agents.GoogleAgents.Required(variableName, "variableName");
            if (document["base_environment"] is JsonObject environment && environment["env"] is JsonObject env)
                env.Remove(variableName);
        }, cancellationToken);

    [Description("Disable all outbound networking for a Google Agent draft, or clear network restrictions to allow default outbound access.")]
    [McpServerTool(Title = "Set Google Agent Draft Network Mode", Name = "google_agent_editor_network_set", ReadOnly = false, Idempotent = true, OpenWorld = false, Destructive = false)]
    public static Task<CallToolResult?> SetNetworkMode(
        string draftName,
        bool disabled,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default)
        => WithDraftMutation(draftName, context, document =>
        {
            var environment = EnsureRemoteEnvironment(document);
            if (disabled) environment["network"] = "disabled";
            else environment.Remove("network");
        }, cancellationToken);

    [Description("Add or replace one outbound domain allowlist entry, with optional credential-based header injection.")]
    [McpServerTool(Title = "Set Google Agent Draft Network Allowlist Entry", Name = "google_agent_editor_network_allow_set", ReadOnly = false, Idempotent = true, OpenWorld = false, Destructive = false)]
    public static Task<CallToolResult?> SetNetworkAllowlist(
        string draftName,
        string domain,
        RequestContext<CallToolRequestParams> context,
        string? credential = null,
        string? headerName = null,
        string? headerValue = null,
        CancellationToken cancellationToken = default)
        => WithDraftMutation(draftName, context, document =>
        {
            Agents.GoogleAgents.Required(domain, "domain");
            if ((headerName is null) != (headerValue is null))
                throw new ValidationException("headerName and headerValue must be provided together.");
            var environment = EnsureRemoteEnvironment(document);
            var network = environment["network"] as JsonObject ?? new JsonObject();
            environment["network"] = network;
            var allowlist = EnsureArray(network, "allowlist");
            RemoveMatches(allowlist, item => String(item, "domain") == domain);
            var entry = new JsonObject { ["domain"] = domain };
            Google.Agents.GoogleAgents.Add(entry, "credential", credential);
            if (headerName is not null)
                entry["transform"] = new JsonObject { [headerName] = headerValue };
            allowlist.Add(entry);
        }, cancellationToken);

    [Description("Remove one outbound domain from the draft's network allowlist.")]
    [McpServerTool(Title = "Remove Google Agent Draft Network Allowlist Entry", Name = "google_agent_editor_network_allow_remove", ReadOnly = false, Idempotent = true, OpenWorld = false, Destructive = false)]
    public static Task<CallToolResult?> RemoveNetworkAllowlist(
        string draftName,
        string domain,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default)
        => WithDraftMutation(draftName, context, document =>
        {
            if (document["base_environment"] is JsonObject environment
                && environment["network"] is JsonObject network
                && network["allowlist"] is JsonArray allowlist)
                RemoveMatches(allowlist, item => String(item, "domain") == domain);
        }, cancellationToken);

    [Description("Add one source mount to the draft's remote environment. sourceKey uniquely identifies the entry for later replacement or removal.")]
    [McpServerTool(Title = "Set Google Agent Draft Source", Name = "google_agent_editor_source_set", ReadOnly = false, Idempotent = true, OpenWorld = false, Destructive = false)]
    public static Task<CallToolResult?> SetSource(
        string draftName,
        string sourceKey,
        string type,
        string target,
        RequestContext<CallToolRequestParams> context,
        string? source = null,
        string? content = null,
        string? encoding = null,
        CancellationToken cancellationToken = default)
        => WithDraftMutation(draftName, context, document =>
        {
            Agents.GoogleAgents.Required(sourceKey, "sourceKey");
            Agents.GoogleAgents.Required(target, "target");
            if (type is not ("gcs" or "inline" or "repository"))
                throw new ValidationException("type must be gcs, inline, or repository.");
            var sources = EnsureArray(EnsureRemoteEnvironment(document), "sources");
            RemoveMatches(sources, item => String(item, "target") == sourceKey || String(item, "target") == target);
            var entry = new JsonObject { ["type"] = type, ["target"] = target };
            Agents.GoogleAgents.Add(entry, "source", source);
            Agents.GoogleAgents.Add(entry, "content", content);
            Agents.GoogleAgents.Add(entry, "encoding", encoding);
            sources.Add(entry);
        }, cancellationToken);

    [Description("Remove a source mount from the draft by its target path.")]
    [McpServerTool(Title = "Remove Google Agent Draft Source", Name = "google_agent_editor_source_remove", ReadOnly = false, Idempotent = true, OpenWorld = false, Destructive = false)]
    public static Task<CallToolResult?> RemoveSource(
        string draftName,
        string target,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default)
        => WithDraftMutation(draftName, context, document =>
        {
            if (document["base_environment"] is JsonObject environment && environment["sources"] is JsonArray sources)
                RemoveMatches(sources, item => String(item, "target") == target);
        }, cancellationToken);
}
