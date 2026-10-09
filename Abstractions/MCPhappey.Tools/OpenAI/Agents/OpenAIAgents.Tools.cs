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
    [Description("Add or replace an application function tool on an OpenAI agent. The parameters schema is loaded from a file URL.")]
    [McpServerTool(Title = "Set OpenAI Agent Function Tool", Name = "openai_agents_function_tool_set", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> OpenAIAgents_FunctionToolSet(
        string agentId, string name, string description, string schemaFileUrl,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        bool deferLoading = false,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            var input = requestContext.Elicit(new AgentFunctionToolRequest
            { AgentId = agentId, Name = name, Description = description, SchemaFileUrl = schemaFileUrl, DeferLoading = deferLoading });


            return await requestContext.WithStructuredContent(async () =>
            {
                ValidateRequired(input.AgentId, "agentId"); ValidateRequired(input.Name, "name");
                ValidateRequired(input.Description, "description");
                var schema = await LoadJsonSchemaAsync(serviceProvider, requestContext.Server, input.SchemaFileUrl, cancellationToken);
                return await MutateToolsAsync(serviceProvider, input.AgentId, tools => UpsertTool(tools, new JsonObject
                {
                    ["type"] = "function",
                    ["name"] = input.Name,
                    ["description"] = input.Description,
                    ["parameters"] = schema,
                    ["defer_loading"] = input.DeferLoading
                }, "function", input.Name), cancellationToken);
            });
        });

    [Description("Remove an application function tool from an OpenAI agent by name.")]
    [McpServerTool(Title = "Remove OpenAI Agent Function Tool", Name = "openai_agents_function_tool_remove", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> OpenAIAgents_FunctionToolRemove(
        string agentId, string name,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await RemoveNamedTool(agentId, name, "function", serviceProvider, requestContext, cancellationToken);

    [Description("Enable or disable the singleton tool-search capability used to discover deferred functions.")]
    [McpServerTool(Title = "Configure OpenAI Agent Tool Search", Name = "openai_agents_tool_search_set", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> OpenAIAgents_ToolSearchSet(
        string agentId, bool enabled,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await SetSingletonTool(agentId, "tool_search", enabled, null, serviceProvider, requestContext, cancellationToken);

    [Description("Enable or disable programmatic tool calling for an OpenAI agent.")]
    [McpServerTool(Title = "Configure OpenAI Agent Programmatic Tool Calling", Name = "openai_agents_programmatic_tool_calling_set", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> OpenAIAgents_ProgrammaticToolCallingSet(
        string agentId, bool enabled,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await SetSingletonTool(agentId, "programmatic_tool_calling", enabled, new JsonObject { ["enabled"] = true }, serviceProvider, requestContext, cancellationToken);

    [Description("Enable or disable browser computer use for an OpenAI-hosted agent session.")]
    [McpServerTool(Title = "Configure OpenAI Agent Computer Use", Name = "openai_agents_computer_use_set", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> OpenAIAgents_ComputerUseSet(
        string agentId, bool enabled,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        bool includeScreenshots = false, CancellationToken cancellationToken = default)
        => await SetSingletonTool(agentId, "computer_use", enabled,
            new JsonObject { ["include_screenshots"] = includeScreenshots }, serviceProvider, requestContext, cancellationToken);

    private static async Task<CallToolResult?> SetSingletonTool(
        string agentId, string type, bool enabled, JsonObject? extra,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ValidateRequired(agentId, "agentId");
                return await MutateToolsAsync(serviceProvider, agentId, tools =>
                {
                    RemoveTool(tools, type);
                    if (enabled)
                    {
                        var tool = extra?.DeepClone() as JsonObject ?? new JsonObject();
                        tool["type"] = type;
                        tools.Add(tool);
                    }
                }, cancellationToken);
            }));

    [Description("Add or replace web-search configuration on an OpenAI agent.")]
    [McpServerTool(Title = "Set OpenAI Agent Web Search", Name = "openai_agents_web_search_set", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> OpenAIAgents_WebSearchSet(
        string agentId,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        string? contextSize = null, string? mode = null,
        string? city = null, string? country = null, string? region = null, string? timezone = null,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            var input = requestContext.Elicit(new AgentWebSearchRequest
            {
                AgentId = agentId,
                ContextSize = contextSize,
                Mode = mode,
                City = city,
                Country = country,
                Region = region,
                Timezone = timezone
            });

            return await requestContext.WithStructuredContent(async () =>
            {
                ValidateRequired(input.AgentId, "agentId");
                ValidateEnum(input.ContextSize, "contextSize", "low", "medium", "high");
                ValidateEnum(input.Mode, "mode", "disabled", "cached", "live");
                if (input.Country is { Length: > 0 } && input.Country.Length != 2)
                    throw new ValidationException("country must be a two-letter ISO country code.");
                var tool = new JsonObject { ["type"] = "web_search" };
                SetOptionalString(tool, "context_size", input.ContextSize); SetOptionalString(tool, "mode", input.Mode);
                if (new[] { input.City, input.Country, input.Region, input.Timezone }.Any(x => x is not null))
                {
                    var location = new JsonObject();
                    SetOptionalString(location, "city", input.City); SetOptionalString(location, "country", input.Country);
                    SetOptionalString(location, "region", input.Region); SetOptionalString(location, "timezone", input.Timezone);
                    tool["location"] = location;
                }
                return await MutateToolsAsync(serviceProvider, input.AgentId, tools =>
                {
                    var existing = FindTool(tools, "web_search");
                    if (existing is not null)
                    {
                        if (tool["context_size"] is null && existing["context_size"] is not null)
                            tool["context_size"] = existing["context_size"]!.DeepClone();
                        if (tool["mode"] is null && existing["mode"] is not null)
                            tool["mode"] = existing["mode"]!.DeepClone();
                        if (tool["location"] is null && existing["location"] is not null)
                            tool["location"] = existing["location"]!.DeepClone();
                        if (existing["allowed_domains"] is not null)
                            tool["allowed_domains"] = existing["allowed_domains"]!.DeepClone();
                    }
                    UpsertTool(tools, tool, "web_search");
                }, cancellationToken);
            });
        });

    [Description("Remove web search from an OpenAI agent.")]
    [McpServerTool(Title = "Remove OpenAI Agent Web Search", Name = "openai_agents_web_search_remove", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> OpenAIAgents_WebSearchRemove(
        string agentId, IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await SetSingletonTool(agentId, "web_search", false, null, serviceProvider, requestContext, cancellationToken);

    [Description("Add one allowed domain to the agent's existing web-search tool.")]
    [McpServerTool(Title = "Add OpenAI Agent Web Search Domain", Name = "openai_agents_web_search_domain_add", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> OpenAIAgents_WebSearchDomainAdd(
        string agentId, string domain, IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext, CancellationToken cancellationToken = default)
        => await MutateWebDomain(agentId, domain, false, serviceProvider, requestContext, cancellationToken);

    [Description("Remove one allowed domain from the agent's existing web-search tool.")]
    [McpServerTool(Title = "Remove OpenAI Agent Web Search Domain", Name = "openai_agents_web_search_domain_remove", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> OpenAIAgents_WebSearchDomainRemove(
        string agentId, string domain, IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext, CancellationToken cancellationToken = default)
        => await MutateWebDomain(agentId, domain, true, serviceProvider, requestContext, cancellationToken);

    private static async Task<CallToolResult?> MutateWebDomain(string agentId, string domain, bool remove,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext, CancellationToken cancellationToken)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ValidateRequired(agentId, "agentId"); ValidateRequired(domain, "domain");
                return await MutateToolsAsync(serviceProvider, agentId, tools =>
                {
                    var tool = FindTool(tools, "web_search") ?? throw new ValidationException("Configure web search before editing domains.");
                    var domains = OpenAIAgentsHttp.CloneArray(tool["allowed_domains"]);
                    if (remove) RemoveArrayItem(domains, domain); else AddArrayItem(domains, domain);
                    tool["allowed_domains"] = domains.Count == 0 ? null : domains;
                }, cancellationToken);
            }));

    private static async Task<CallToolResult?> RemoveNamedTool(
        string agentId, string name, string type,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            var input = requestContext.Elicit(new AgentToolIdentityRequest
            { AgentId = agentId, Name = name });

            return await requestContext.WithStructuredContent(async () =>
            {
                ValidateRequired(input.AgentId, "agentId"); ValidateRequired(input.Name, "name");
                return await MutateToolsAsync(serviceProvider, input.AgentId, tools =>
                {
                    if (!RemoveTool(tools, type, input.Name))
                        throw new ValidationException($"No {type} tool named '{input.Name}' exists.");
                }, cancellationToken);
            });
        });
}
