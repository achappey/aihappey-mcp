using System.ComponentModel;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using MCPhappey.Common.Models;

namespace MCPhappey.Tools.Mistral.Agents;

public static partial class AgentsPlugin
{
    private const string BaseUrl = "https://api.mistral.ai/v1/agents";

    [Description("Please confirm deletion of the agent with this exact ID: {0}")]
    public class DeleteAgentInput : IHasName
    {
        [JsonPropertyName("name")]
        [Description("ID of the agent.")]
        public string Name { get; set; } = default!;
    }

    [Description("Delete a Mistral Agent by ID.")]
    [McpServerTool(
        Title = "Mistral delete agent",
        IconSource = MistralConstants.ICON_SOURCE,
        Name = "mistral_agents_delete",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> MistralAgents_DeleteAgent(
        [Description("The ID of the agent to delete.")] string agentId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            var mistral = serviceProvider.GetRequiredService<MistralClient>();
            return await requestContext.ConfirmAndDeleteAsync<DeleteAgentInput>(
                expectedName: agentId,
                deleteAction: async _ =>
                {
                    var resp = await mistral.DeleteAgentAsync(agentId, cancellationToken);
                    var json = await resp.Content.ReadAsStringAsync(cancellationToken);

                    if (!resp.IsSuccessStatusCode)
                        throw new Exception($"{resp.StatusCode}: {json}");
                },
                successText: $"Agent '{agentId}' deleted successfully!",
                ct: cancellationToken);
        }));

    [Description("Send a message to a Mistral Agent using the conversation API. No store, no streaming.")]
    [McpServerTool(
        Title = "Mistral run agent conversation",
        IconSource = MistralConstants.ICON_SOURCE,
        Name = "mistral_agents_run",
        ReadOnly = false,
        OpenWorld = true,
        Destructive = false)]
    public static async Task<CallToolResult?> MistralAgents_Run(
        [Description("ID of the agent to run.")] string agentId,
        [Description("User message input.")] string input,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            var mistral = serviceProvider.GetRequiredService<MistralClient>();
            var body = new Dictionary<string, object?>
            {
                ["agent_id"] = agentId,
                ["inputs"] = input,
                ["store"] = false,
                ["stream"] = false,
                ["handoff_execution"] = "server"
            };

            var resp = await mistral.RunAgentConversationAsync(body, cancellationToken);
            var json = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"{resp.StatusCode}: {json}");

            JsonNode? parsed = JsonNode.Parse(json);
            return await Task.FromResult(parsed);
        }));

    [Description("Create a new Mistral Agent that can be used in conversations.")]
    [McpServerTool(
        Title = "Mistral create agent",
        IconSource = MistralConstants.ICON_SOURCE,
        Name = "mistral_agents_create",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = false)]
    public static async Task<CallToolResult?> MistralAgents_CreateAgent(
        [Description("Name of the new agent.")] string name,
        [Description("Model the agent will use. Example: mistral-large-latest")] string model,
        [Description("Purpose or instructions for the agent.")] string? instructions,
        [Description("Optional description of the agent.")] string? description,
        [Description("Optional list of tools. Options: image_generation, code_interpreter, web_search and web_search_premium")] List<string>? tools,
        [Description("Optional list of agent ids available for handoffs.")] List<string>? handoffs,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            var mistral = serviceProvider.GetRequiredService<MistralClient>();

            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new CreateAgentInput
                {
                    Name = name,
                    Model = model,
                    Instructions = instructions,
                    Description = description
                },
                cancellationToken
            );

            var body = new Dictionary<string, object?>
            {
                ["name"] = typed.Name,
                ["model"] = typed.Model,
                ["instructions"] = typed.Instructions,
                ["description"] = typed.Description
            };

            if (handoffs != null && handoffs.Any())
            {
                body["handoffs"] = handoffs;
            }

            var toolList = tools?
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => new { type = t })
                .ToList();

            if (toolList != null && toolList.Any())
            {
                body["tools"] = toolList;
            }

            var resp = await mistral.CreateAgentAsync(body, cancellationToken);
            var json = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"{resp.StatusCode}: {json}");

            return json.ToJsonCallToolResponse($"{BaseUrl}");
        }));

    public class CreateAgentInput
    {
        [Required]
        [JsonPropertyName("name")]
        [Description("The name of the agent.")]
        public string Name { get; set; } = default!;

        [Required]
        [JsonPropertyName("model")]
        [Description("The model the agent will run. Example: mistral-large-latest")]
        public string Model { get; set; } = default!;

        [JsonPropertyName("instructions")]
        [Description("Prompt instructions the agent will follow during conversations.")]
        public string? Instructions { get; set; }

        [JsonPropertyName("description")]
        [Description("Optional description of the agent.")]
        public string? Description { get; set; }
    }
}
