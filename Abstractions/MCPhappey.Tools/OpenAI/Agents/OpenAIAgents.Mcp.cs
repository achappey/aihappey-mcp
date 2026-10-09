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
        string? connectionOrigin = null, string? credentialId = null, bool required = false,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            var input = requestContext.Elicit(new AgentHttpMcpRequest
            {
                AgentId = agentId,
                ServerLabel = serverLabel,
                ServerUrl = serverUrl,
                ConnectionOrigin = connectionOrigin,
                CredentialId = credentialId,
                Required = required
            });

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
                    ["type"] = "mcp",
                    ["server_label"] = input.ServerLabel,
                    ["required"] = input.Required,
                    ["transport"] = new JsonObject { ["type"] = "http", ["server_url"] = input.ServerUrl }
                };
                PreserveMcpMaps(existing, tool);
                if (existing?["allowed_tools"] is not null) tool["allowed_tools"] = existing["allowed_tools"]!.DeepClone();
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
        bool required = false,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            var input = requestContext.Elicit(new AgentStdioMcpRequest
            {
                AgentId = agentId,
                ServerLabel = serverLabel,
                Command = command,
                Cwd = cwd,
                Required = required
            });

            return await requestContext.WithStructuredContent(async () =>
            {
                ValidateRequired(input.AgentId, "agentId"); ValidateRequired(input.ServerLabel, "serverLabel");
                ValidateRequired(input.Command, "command"); ValidateRequired(input.Cwd, "cwd");
                var transport = new JsonObject { ["type"] = "stdio", ["command"] = input.Command, ["cwd"] = input.Cwd };
                var tool = new JsonObject { ["type"] = "mcp", ["server_label"] = input.ServerLabel, ["transport"] = transport, ["required"] = input.Required };
                return await MutateToolsAsync(serviceProvider, input.AgentId,
                    tools =>
                    {
                        var existing = FindTool(tools, "mcp", input.ServerLabel);
                        if (existing?["allowed_tools"] is not null) tool["allowed_tools"] = existing["allowed_tools"]!.DeepClone();
                        if (existing?["request_metadata"] is not null) tool["request_metadata"] = existing["request_metadata"]!.DeepClone();
                        if (existing?["transport"]?["type"]?.GetValue<string>() == "stdio")
                        {
                            foreach (var key in new[] { "args", "env_vars" })
                                if (existing["transport"]?[key] is JsonNode value) transport[key] = value.DeepClone();
                        }
                        UpsertTool(tools, tool, "mcp", input.ServerLabel);
                    }, cancellationToken);
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
            var input = requestContext.Elicit(new AgentMcpMapEntryRequest
            { AgentId = agentId, ServerLabel = serverLabel, Key = key, Value = value });

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

    [Description("Add one permitted tool name to an existing agent MCP server.")]
    [McpServerTool(Title = "Add OpenAI Agent MCP Allowed Tool", Name = "openai_agents_mcp_allowed_tool_add", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> OpenAIAgents_McpAllowedToolAdd(string agentId, string serverLabel, string toolName,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext, CancellationToken cancellationToken = default)
        => await MutateMcpArray(agentId, serverLabel, "allowed_tools", toolName, false, serviceProvider, requestContext, cancellationToken);

    [Description("Remove one permitted tool name from an existing agent MCP server.")]
    [McpServerTool(Title = "Remove OpenAI Agent MCP Allowed Tool", Name = "openai_agents_mcp_allowed_tool_remove", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> OpenAIAgents_McpAllowedToolRemove(string agentId, string serverLabel, string toolName,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext, CancellationToken cancellationToken = default)
        => await MutateMcpArray(agentId, serverLabel, "allowed_tools", toolName, true, serviceProvider, requestContext, cancellationToken);

    [Description("Append one argument to a stdio MCP server command (order and duplicates are preserved).")]
    [McpServerTool(Title = "Add OpenAI Agent MCP Stdio Argument", Name = "openai_agents_mcp_stdio_arg_add", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> OpenAIAgents_McpStdioArgAdd(string agentId, string serverLabel, string argument,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext, CancellationToken cancellationToken = default)
        => await MutateMcpArray(agentId, serverLabel, "args", argument, false, serviceProvider, requestContext, cancellationToken);

    [Description("Remove an argument at a zero-based index from a stdio MCP command.")]
    [McpServerTool(Title = "Remove OpenAI Agent MCP Stdio Argument", Name = "openai_agents_mcp_stdio_arg_remove", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> OpenAIAgents_McpStdioArgRemove(string agentId, string serverLabel, int index,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext, CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () => await MutateToolsAsync(serviceProvider, agentId, tools =>
            {
                var transport = GetStdioTransport(tools, serverLabel);
                var args = OpenAIAgentsHttp.CloneArray(transport["args"]);
                if (index < 0 || index >= args.Count) throw new ValidationException("Argument index is out of range.");
                args.RemoveAt(index);
                transport["args"] = args;
            }, cancellationToken)));

    [Description("Add one inherited environment variable name to a stdio MCP server.")]
    [McpServerTool(Title = "Add OpenAI Agent MCP Stdio Environment Variable", Name = "openai_agents_mcp_stdio_env_var_add", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> OpenAIAgents_McpStdioEnvVarAdd(string agentId, string serverLabel, string variableName,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext, CancellationToken cancellationToken = default)
        => await MutateMcpArray(agentId, serverLabel, "env_vars", variableName, false, serviceProvider, requestContext, cancellationToken);

    [Description("Remove one inherited environment variable name from a stdio MCP server.")]
    [McpServerTool(Title = "Remove OpenAI Agent MCP Stdio Environment Variable", Name = "openai_agents_mcp_stdio_env_var_remove", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> OpenAIAgents_McpStdioEnvVarRemove(string agentId, string serverLabel, string variableName,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext, CancellationToken cancellationToken = default)
        => await MutateMcpArray(agentId, serverLabel, "env_vars", variableName, true, serviceProvider, requestContext, cancellationToken);

    private static JsonObject GetStdioTransport(JsonArray tools, string serverLabel)
    {
        var tool = FindTool(tools, "mcp", serverLabel) ?? throw new ValidationException($"MCP server '{serverLabel}' was not found.");
        var transport = tool["transport"] as JsonObject;
        if (transport?["type"]?.GetValue<string>() != "stdio")
            throw new ValidationException("Arguments and environment variables require a stdio MCP transport.");
        return transport;
    }

    private static async Task<CallToolResult?> MutateMcpArray(string agentId, string serverLabel, string field,
        string value, bool remove, IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ValidateRequired(agentId, "agentId"); ValidateRequired(serverLabel, "serverLabel");
                ValidateRequired(value, "value");
                return await MutateToolsAsync(serviceProvider, agentId, tools =>
                {
                    var target = field == "allowed_tools"
                        ? FindTool(tools, "mcp", serverLabel) ?? throw new ValidationException($"MCP server '{serverLabel}' was not found.")
                        : GetStdioTransport(tools, serverLabel);
                    var items = OpenAIAgentsHttp.CloneArray(target[field]);
                    if (remove) RemoveArrayItem(items, value);
                    else if (field == "args") items.Add(value);
                    else AddArrayItem(items, value);
                    // An empty allowlist must remain explicit; omission would grant access to every server tool.
                    target[field] = items;
                }, cancellationToken);
            }));

    private static void PreserveMcpMaps(JsonObject? existing, JsonObject replacement)
    {
        if (existing?["request_metadata"] is not null) replacement["request_metadata"] = existing["request_metadata"]!.DeepClone();
        if (existing?["transport"] is JsonObject existingTransport
            && replacement["transport"] is JsonObject replacementTransport
            && existingTransport["headers"] is not null)
            replacementTransport["headers"] = existingTransport["headers"]!.DeepClone();
    }
}
