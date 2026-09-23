using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using MCPhappey.Core.Services;
using MCPhappey.Tools.Google.Agents;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Google.AgentEditor;

public static partial class GoogleAgentEditor
{
    [Description("Enable or remove the singleton code-execution tool in a Google Agent draft.")]
    [McpServerTool(Title = "Configure Google Agent Draft Code Execution", Name = "google_agent_editor_code_execution_set", ReadOnly = false, Idempotent = true, OpenWorld = false, Destructive = false)]
    public static Task<CallToolResult?> SetCodeExecution(
        string draftName,
        bool enabled,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default)
        => SetSingletonTool(draftName, "code_execution", enabled, null, context, cancellationToken);

    [Description("Enable or remove the singleton URL-context tool in a Google Agent draft.")]
    [McpServerTool(Title = "Configure Google Agent Draft URL Context", Name = "google_agent_editor_url_context_set", ReadOnly = false, Idempotent = true, OpenWorld = false, Destructive = false)]
    public static Task<CallToolResult?> SetUrlContext(
        string draftName,
        bool enabled,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default)
        => SetSingletonTool(draftName, "url_context", enabled, null, context, cancellationToken);

    [Description("Configure Google web and image search in a Google Agent draft.")]
    [McpServerTool(Title = "Configure Google Agent Draft Search", Name = "google_agent_editor_google_search_set", ReadOnly = false, Idempotent = true, OpenWorld = false, Destructive = false)]
    public static Task<CallToolResult?> SetGoogleSearch(
        string draftName,
        bool enabled,
        RequestContext<CallToolRequestParams> context,
        bool webSearch = true,
        bool imageSearch = false,
        CancellationToken cancellationToken = default)
    {
        if (enabled && !webSearch && !imageSearch)
            throw new ValidationException("Enable at least one search type.");
        var searchTypes = new JsonArray();
        if (webSearch) searchTypes.Add("web_search");
        if (imageSearch) searchTypes.Add("image_search");
        return SetSingletonTool(draftName, "google_search", enabled,
            new JsonObject { ["search_types"] = searchTypes }, context, cancellationToken);
    }

    [Description("Add or replace a function tool. Its parameters JSON Schema is loaded from a SharePoint or OneDrive JSON file.")]
    [McpServerTool(Title = "Set Google Agent Draft Function", Name = "google_agent_editor_function_set", ReadOnly = false, Idempotent = true, OpenWorld = true, Destructive = false)]
    public static async Task<CallToolResult?> SetFunction(
        string draftName,
        string name,
        string parametersSchemaFileUrl,
        IServiceProvider services,
        RequestContext<CallToolRequestParams> context,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        GoogleAgents.Required(name, "name");
        GoogleAgents.EnsureMicrosoftFileUrl(parametersSchemaFileUrl);
        var file = (await services.GetRequiredService<DownloadService>()
            .DownloadContentAsync(services, context.Server, parametersSchemaFileUrl, cancellationToken)).FirstOrDefault()
            ?? throw new ValidationException("The parameters schema file could not be downloaded.");
        var schema = GoogleAgentDocument.Parse(file.Contents.ToString(), "parameters schema file");
        return await WithDraftMutation(draftName, context, document =>
        {
            var tools = EnsureArray(document, "tools");
            RemoveMatches(tools, item => String(item, "type") == "function" && String(item, "name") == name);
            var tool = new JsonObject { ["type"] = "function", ["name"] = name, ["parameters"] = schema };
            GoogleAgents.Add(tool, "description", description);
            tools.Add(tool);
        }, cancellationToken);
    }

    [Description("Remove a function tool by name from a Google Agent draft.")]
    [McpServerTool(Title = "Remove Google Agent Draft Function", Name = "google_agent_editor_function_remove", ReadOnly = false, Idempotent = true, OpenWorld = false, Destructive = false)]
    public static Task<CallToolResult?> RemoveFunction(
        string draftName,
        string name,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default)
        => RemoveNamedTool(draftName, "function", name, context, cancellationToken);

    [Description("Add or replace an HTTPS MCP server tool in a Google Agent draft.")]
    [McpServerTool(Title = "Set Google Agent Draft MCP Server", Name = "google_agent_editor_mcp_server_set", ReadOnly = false, Idempotent = true, OpenWorld = true, Destructive = false)]
    public static Task<CallToolResult?> SetMcpServer(
        string draftName,
        string name,
        string url,
        RequestContext<CallToolRequestParams> context,
        string? allowedMode = null,
        string? allowedTools = null,
        string? headerName = null,
        string? headerValue = null,
        CancellationToken cancellationToken = default)
        => WithDraftMutation(draftName, context, document =>
        {
            GoogleAgents.Required(name, "name");
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                throw new ValidationException("url must be an absolute HTTPS URL.");
            if (allowedMode is not null && allowedMode is not ("auto" or "any" or "none" or "validated"))
                throw new ValidationException("allowedMode must be auto, any, none, or validated.");
            if ((headerName is null) != (headerValue is null))
                throw new ValidationException("headerName and headerValue must be provided together.");
            var tools = EnsureArray(document, "tools");
            RemoveMatches(tools, item => String(item, "type") == "mcp_server" && String(item, "name") == name);
            var tool = new JsonObject { ["type"] = "mcp_server", ["name"] = name, ["url"] = url };
            if (allowedMode is not null || !string.IsNullOrWhiteSpace(allowedTools))
            {
                var choice = new JsonObject();
                GoogleAgents.Add(choice, "mode", allowedMode);
                if (!string.IsNullOrWhiteSpace(allowedTools)) choice["tools"] = Strings(allowedTools);
                tool["allowed_tools"] = new JsonArray(choice);
            }
            if (headerName is not null) tool["headers"] = new JsonObject { [headerName] = headerValue };
            tools.Add(tool);
        }, cancellationToken);

    [Description("Remove an MCP server tool by name from a Google Agent draft.")]
    [McpServerTool(Title = "Remove Google Agent Draft MCP Server", Name = "google_agent_editor_mcp_server_remove", ReadOnly = false, Idempotent = true, OpenWorld = false, Destructive = false)]
    public static Task<CallToolResult?> RemoveMcpServer(
        string draftName,
        string name,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default)
        => RemoveNamedTool(draftName, "mcp_server", name, context, cancellationToken);

    private static Task<CallToolResult?> SetSingletonTool(
        string draftName,
        string type,
        bool enabled,
        JsonObject? configuration,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken)
        => WithDraftMutation(draftName, context, document =>
        {
            var tools = EnsureArray(document, "tools");
            RemoveMatches(tools, item => String(item, "type") == type);
            if (!enabled) return;
            var tool = configuration?.DeepClone() as JsonObject ?? new JsonObject();
            tool["type"] = type;
            tools.Add(tool);
        }, cancellationToken);

    private static Task<CallToolResult?> RemoveNamedTool(
        string draftName,
        string type,
        string name,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken)
        => WithDraftMutation(draftName, context, document =>
        {
            if (document["tools"] is JsonArray tools)
                RemoveMatches(tools, item => String(item, "type") == type && String(item, "name") == name);
        }, cancellationToken);
}
