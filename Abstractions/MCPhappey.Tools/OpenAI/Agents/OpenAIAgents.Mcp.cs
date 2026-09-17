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
    [Description("Add or replace an HTTP MCP server tool on an OpenAI agent. Headers and request metadata are managed separately.")]
    [McpServerTool(Title = "Set OpenAI Agent HTTP MCP Server", Name = "openai_agents_mcp_http_set", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> OpenAIAgents_McpHttpSet(
        string agentId, string serverLabel, string serverUrl,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        string? allowedTools = null, string? connectionOrigin = null, string? credentialId = null, bool required = false,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            var (input, rejected, _) = await requestContext.Server.TryElicit(new AgentHttpMcpRequest
            {
                AgentId = agentId, ServerLabel = serverLabel, ServerUrl = serverUrl, AllowedTools = allowedTools,
                ConnectionOrigin = connectionOrigin, CredentialId = credentialId, Required = required
            }, cancellationToken);
            if (rejected is not null) return rejected;
            return await requestContext.WithStructuredContent(async () =>
            {
                ValidateRequired(input.AgentId, "agentId"); ValidateRequired(input.ServerLabel, "serverLabel");
                OpenAIAgentsHttp.ValidateHttpsUrl(input.ServerUrl, "serverUrl");
                ValidateEnum(input.ConnectionOrigin, "connectionOrigin", "service", "environment");
                var current = await GetAgentAsync(serviceProvider, input.AgentId, cancellationToken);
                var tools = OpenAIAgentsHttp.CloneArray(current["tools"]);
                var existing = FindTool(tools, "mcp", input.ServerLabel);
                var tool = new JsonObject
                {
                    ["type"] = "mcp", ["server_label"] = input.ServerLabel, ["required"] = input.Required,
                    ["transport"] = new JsonObject { ["type"] = "http", ["server_url"] = input.ServerUrl }
                };
                PreserveMcpMaps(existing, tool);
                var allowed = OpenAIAgentsHttp.ParseDelimited(input.AllowedTools);
                if (allowed.Count > 0) tool["allowed_tools"] = OpenAIAgentsHttp.ToArray(allowed);
                SetOptionalString(tool, "connection_origin", input.ConnectionOrigin);
                SetOptionalString(tool, "credential_id", input.CredentialId);
                UpsertTool(tools, tool, "mcp", input.ServerLabel);
                return await UpdateAsync(serviceProvider, input.AgentId, new JsonObject { ["tools"] = tools }, cancellationToken);
            });
        });

    [Description("Add or replace a stdio MCP server tool on an OpenAI agent.")]
    [McpServerTool(Title = "Set OpenAI Agent Stdio MCP Server", Name = "openai_agents_mcp_stdio_set", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> OpenAIAgents_McpStdioSet(
        string agentId, string serverLabel, string command, string cwd,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        string? args = null, string? envVars = null, string? allowedTools = null, bool required = false,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            var (input, rejected, _) = await requestContext.Server.TryElicit(new AgentStdioMcpRequest
            {
                AgentId = agentId, ServerLabel = serverLabel, Command = command, Cwd = cwd,
                Args = args, EnvVars = envVars, AllowedTools = allowedTools, Required = required
            }, cancellationToken);
            if (rejected is not null) return rejected;
            return await requestContext.WithStructuredContent(async () =>
            {
                ValidateRequired(input.AgentId, "agentId"); ValidateRequired(input.ServerLabel, "serverLabel");
                ValidateRequired(input.Command, "command"); ValidateRequired(input.Cwd, "cwd");
                var transport = new JsonObject { ["type"] = "stdio", ["command"] = input.Command, ["cwd"] = input.Cwd };
                var parsedArgs = OpenAIAgentsHttp.ParseDelimited(input.Args); if (parsedArgs.Count > 0) transport["args"] = OpenAIAgentsHttp.ToArray(parsedArgs);
                var parsedEnv = OpenAIAgentsHttp.ParseDelimited(input.EnvVars); if (parsedEnv.Count > 0) transport["env_vars"] = OpenAIAgentsHttp.ToArray(parsedEnv);
                var tool = new JsonObject { ["type"] = "mcp", ["server_label"] = input.ServerLabel, ["transport"] = transport, ["required"] = input.Required };
                var allowed = OpenAIAgentsHttp.ParseDelimited(input.AllowedTools); if (allowed.Count > 0) tool["allowed_tools"] = OpenAIAgentsHttp.ToArray(allowed);
                return await MutateToolsAsync(serviceProvider, input.AgentId,
                    tools => UpsertTool(tools, tool, "mcp", input.ServerLabel), cancellationToken);
            });
        });

    [Description("Set or remove one non-secret HTTP transport header for an agent MCP server. Omit value to remove the header.")]
    [McpServerTool(Title = "Set OpenAI Agent MCP Header", Name = "openai_agents_mcp_header_set", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> OpenAIAgents_McpHeaderSet(
        string agentId, string serverLabel, string key,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        string? value = null, CancellationToken cancellationToken = default)
        => await MutateMcpMap(agentId, serverLabel, key, value, true, serviceProvider, requestContext, cancellationToken);

    [Description("Set or remove one request-metadata entry for an agent MCP server. Omit value to remove the entry.")]
    [McpServerTool(Title = "Set OpenAI Agent MCP Request Metadata", Name = "openai_agents_mcp_request_metadata_set", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> OpenAIAgents_McpRequestMetadataSet(
        string agentId, string serverLabel, string key,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        string? value = null, CancellationToken cancellationToken = default)
        => await MutateMcpMap(agentId, serverLabel, key, value, false, serviceProvider, requestContext, cancellationToken);

    private static async Task<CallToolResult?> MutateMcpMap(
        string agentId, string serverLabel, string key, string? value, bool header,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            var (input, rejected, _) = await requestContext.Server.TryElicit(new AgentMcpMapEntryRequest
            { AgentId = agentId, ServerLabel = serverLabel, Key = key, Value = value }, cancellationToken);
            if (rejected is not null) return rejected;
            return await requestContext.WithStructuredContent(async () =>
            {
                ValidateRequired(input.AgentId, "agentId"); ValidateRequired(input.ServerLabel, "serverLabel"); ValidateRequired(input.Key, "key");
                return await MutateToolsAsync(serviceProvider, input.AgentId, tools =>
                {
                    var tool = FindTool(tools, "mcp", input.ServerLabel) ?? throw new ValidationException($"MCP server '{input.ServerLabel}' was not found.");
                    JsonObject map;
                    if (header)
                    {
                        var transport = tool["transport"] as JsonObject ?? throw new ValidationException("The MCP transport is invalid.");
                        if (!string.Equals(transport["type"]?.GetValue<string>(), "http", StringComparison.Ordinal))
                            throw new ValidationException("Headers are only supported for HTTP MCP transports.");
                        map = OpenAIAgentsHttp.CloneObject(transport["headers"]);
                        if (input.Value is null) map.Remove(input.Key); else map[input.Key] = input.Value;
                        transport["headers"] = map;
                    }
                    else
                    {
                        map = OpenAIAgentsHttp.CloneObject(tool["request_metadata"]);
                        if (input.Value is null) map.Remove(input.Key); else map[input.Key] = input.Value;
                        tool["request_metadata"] = map;
                    }
                }, cancellationToken);
            });
        });

    [Description("Remove an MCP server tool from an OpenAI agent.")]
    [McpServerTool(Title = "Remove OpenAI Agent MCP Server", Name = "openai_agents_mcp_remove", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> OpenAIAgents_McpRemove(
        string agentId, string serverLabel,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await RemoveNamedTool(agentId, serverLabel, "mcp", serviceProvider, requestContext, cancellationToken);

    private static void PreserveMcpMaps(JsonObject? existing, JsonObject replacement)
    {
        if (existing?["request_metadata"] is not null) replacement["request_metadata"] = existing["request_metadata"]!.DeepClone();
        if (existing?["transport"] is JsonObject existingTransport
            && replacement["transport"] is JsonObject replacementTransport
            && existingTransport["headers"] is not null)
            replacementTransport["headers"] = existingTransport["headers"]!.DeepClone();
    }
}
