using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Anthropic.Agents;

public static partial class AnthropicAgents
{
    [Description("Add or replace a custom tool on an Anthropic Managed Agent. Supply the input schema via fileUrl so elicitation stays a normal form instead of raw JSON.")]
    [McpServerTool(
        Title = "Add custom tool to Anthropic Agent",
        Name = "anthropic_agents_add_custom_tool",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = false)]
    public static async Task<CallToolResult?> AnthropicAgents_AddCustomToolToAgent(
        [Description("Agent ID.")] string agentId,
        [Description("Unique custom tool name.")] string toolName,
        [Description("Custom tool description.")] string description,
        [Description("URL of the JSON Schema file for the custom tool input. SharePoint and OneDrive URLs are supported through the default downloader.")] string fileUrl,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var (typed, _, _) = await requestContext.Server.TryElicit(new AnthropicAgentCustomToolMutationRequest
                {
                    AgentId = agentId,
                    ToolName = toolName,
                    Description = description,
                    FileUrl = fileUrl,
              
                }, cancellationToken);

                if (string.IsNullOrWhiteSpace(typed.AgentId))
                    throw new ValidationException("agentId is required.");

                if (string.IsNullOrWhiteSpace(typed.ToolName))
                    throw new ValidationException("toolName is required.");

                if (string.IsNullOrWhiteSpace(typed.Description))
                    throw new ValidationException("description is required.");

                if (string.IsNullOrWhiteSpace(typed.FileUrl))
                    throw new ValidationException("fileUrl is required.");

                var inputSchema = await LoadCustomToolInputSchemaAsync(serviceProvider, requestContext, typed.FileUrl, cancellationToken);

                var current = await GetAgentAsync(serviceProvider, typed.AgentId,  cancellationToken);
                var tools = AnthropicManagedAgentsHttp.CloneArray(current["tools"]);

                RemoveCustomTool(tools, typed.ToolName);
                tools.Add(new JsonObject
                {
                    ["type"] = CustomToolType,
                    ["name"] = typed.ToolName,
                    ["description"] = typed.Description,
                    ["input_schema"] = inputSchema
                });

                var body = CreateVersionedUpdateBody(current);
                body["tools"] = tools;

                return await UpdateAgentAsync(serviceProvider, typed.AgentId,  body, cancellationToken);
            }));

    [Description("Remove a custom tool from an Anthropic Managed Agent after explicit typed confirmation.")]
    [McpServerTool(
        Title = "Remove custom tool from Anthropic Agent",
        Name = "anthropic_agents_remove_custom_tool",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> AnthropicAgents_RemoveCustomToolFromAgent(
        [Description("Agent ID.")] string agentId,
        [Description("Custom tool name to remove.")] string toolName,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var expected = $"{agentId}:{toolName}";
                await AnthropicManagedAgentsHttp.ConfirmDeleteAsync<AnthropicDeleteAgentItem>(requestContext.Server, expected, cancellationToken);

                var current = await GetAgentAsync(serviceProvider, agentId,  cancellationToken);
                var tools = AnthropicManagedAgentsHttp.CloneArray(current["tools"]);

                if (!RemoveCustomTool(tools, toolName))
                    throw new ValidationException($"Custom tool '{toolName}' was not found on agent '{agentId}'.");

                var body = CreateVersionedUpdateBody(current);
                body["tools"] = tools;

                return await UpdateAgentAsync(serviceProvider, agentId,  body, cancellationToken);
            }));
}
