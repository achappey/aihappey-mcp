using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.SandBase.Agents;

public static class SandBaseAgents
{
    [Description("Create a SandBase agent. Omitted tools install the default toolset; explicit empty tools JSON installs none. Configure optional arrays and metadata with the update tool.")]
    [McpServerTool(Title = "Create SandBase agent", Name = "sandbase_agents_create", ReadOnly = false, Destructive = false, OpenWorld = false)]
    public static async Task<CallToolResult?> Create(string name, string modelId, IServiceProvider services,
        RequestContext<CallToolRequestParams> context, string? system = null, string? description = null,
        string? toolsJson = null, string? mcpServersJson = null, string? skillsJson = null,
        string? handoffsJson = null, string? metadataJson = null, CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () => await context.WithStructuredContent(async () =>
        {
            var body = new JsonObject { ["name"] = SandBaseClient.Required(name, "name"), ["model"] = SandBaseClient.Required(modelId, "modelId") };
            if (system is not null) body["system"] = system;
            if (description is not null) body["description"] = description;
            AddOptional(body, "tools", toolsJson);
            AddOptional(body, "mcp_servers", mcpServersJson);
            AddOptional(body, "skills", skillsJson);
            AddOptional(body, "handoffs", handoffsJson);
            if (metadataJson is not null) body["metadata"] = Metadata(metadataJson);
            return await SandBaseClient.SendAsync(services, HttpMethod.Post, "v1/agents", body, cancellationToken);
        }));

    [Description("Partially update an agent. Optional version enables optimistic locking (409 on stale version). Array JSON replaces the entire array; 'null' clears it. Null metadata clears caller-owned metadata. Omission preserves fields.")]
    [McpServerTool(Title = "Update SandBase agent", Name = "sandbase_agents_update", ReadOnly = false, Destructive = false, OpenWorld = false)]
    public static async Task<CallToolResult?> Update(string agentId, IServiceProvider services,
        RequestContext<CallToolRequestParams> context, int? version = null, string? name = null,
        string? modelId = null, string? description = null, string? system = null,
        string? toolsJson = null, string? mcpServersJson = null, string? skillsJson = null,
        string? handoffsJson = null, string? metadataJson = null, CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () => await context.WithStructuredContent(async () =>
        {
            var body = new JsonObject();
            if (version is not null) body["version"] = version > 0 ? version : throw new ValidationException("version must be positive.");
            if (name is not null) body["name"] = SandBaseClient.Required(name, "name");
            if (modelId is not null) body["model"] = SandBaseClient.Required(modelId, "modelId");
            if (description is not null) body["description"] = description;
            if (system is not null) body["system"] = system;
            AddOptional(body, "tools", toolsJson);
            AddOptional(body, "mcp_servers", mcpServersJson);
            AddOptional(body, "skills", skillsJson);
            AddOptional(body, "handoffs", handoffsJson);
            if (metadataJson is not null) body["metadata"] = metadataJson.Trim() == "null" ? null : Metadata(metadataJson);
            if (body.Count == (version is null ? 0 : 1)) throw new ValidationException("Supply at least one change.");
            return await SandBaseClient.SendAsync(services, HttpMethod.Post,
                $"v1/agents/{SandBaseClient.AgentId(agentId)}", body, cancellationToken);
        }));

    [Description("Archive a SandBase agent (soft delete). Existing sessions retain their pinned snapshot. Confirm the action before invoking.")]
    [McpServerTool(Title = "Archive SandBase agent", Name = "sandbase_agents_archive", ReadOnly = false, Destructive = true, OpenWorld = false)]
    public static async Task<CallToolResult?> Archive(string agentId, IServiceProvider services,
        RequestContext<CallToolRequestParams> context, CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () => await context.WithStructuredContent(async () =>
            await SandBaseClient.SendAsync(services, HttpMethod.Post,
                $"v1/agents/{SandBaseClient.AgentId(agentId)}/archive", cancellationToken: cancellationToken)));

    private static void AddOptional(JsonObject body, string name, string? json)
    {
        if (json is not null) body[name] = json.Trim() == "null" ? null : SandBaseClient.Array(json, name);
    }

    private static JsonObject Metadata(string json)
    {
        var result = SandBaseClient.Object(json, "metadataJson");
        if (result.ContainsKey("_sandbase")) throw new ValidationException("_sandbase metadata is platform-owned.");
        return result;
    }
}
