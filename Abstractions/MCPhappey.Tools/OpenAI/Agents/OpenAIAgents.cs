using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.OpenAI.AgentsApi;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.OpenAI.Agents;

public static partial class OpenAIAgents
{
    internal const string BaseUrl = $"{OpenAIAgentsHttp.ApiBaseUrl}/agents";

    [Description("Please confirm to delete: {0}")]
    public sealed class OpenAIDeleteAgent : IHasName
    {
        [JsonPropertyName("name")]
        [Description("Type the agent ID to confirm permanent deletion.")]
        public string Name { get; set; } = string.Empty;
    }

    [Description("Create a reusable OpenAI agent. Use the dedicated mutation tools to add metadata, structured text settings, multi-agent configuration, and tools.")]
    [McpServerTool(Title = "Create OpenAI Agent", Name = "openai_agents_create", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> OpenAIAgents_Create(
        [Description("OpenAI model name.")] string model,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional human-readable agent name.")] string? name = null,
        [Description("Optional instructions appended to the agent's default instructions.")] string? instructions = null,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            var (input, rejected, _) = await requestContext.Server.TryElicit(new AgentScalarRequest
            {
                Model = model,
                Name = name,
                Instructions = instructions
            }, cancellationToken);
            if (rejected is not null) return rejected;

            return await requestContext.WithStructuredContent(async () =>
            {
                ValidateRequired(input.Model, "model");
                var body = new JsonObject { ["model"] = input.Model };
                SetOptionalString(body, "name", input.Name);
                SetOptionalString(body, "instructions", input.Instructions);
                return await OpenAIAgentsHttp.SendAsync(serviceProvider, HttpMethod.Post, BaseUrl, body, cancellationToken);
            });
        });

    [Description("Update the scalar fields of a reusable OpenAI agent. Omitted fields remain unchanged; clearName and clearInstructions explicitly send null.")]
    [McpServerTool(Title = "Update OpenAI Agent", Name = "openai_agents_update", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> OpenAIAgents_Update(
        [Description("Agent ID.")] string agentId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Replacement model name. Omit to preserve.")] string? model = null,
        [Description("Replacement name. Omit to preserve.")] string? name = null,
        [Description("Replacement instructions. Omit to preserve.")] string? instructions = null,
        [Description("Set true to clear the agent name.")] bool clearName = false,
        [Description("Set true to clear custom instructions.")] bool clearInstructions = false,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            var (input, rejected, _) = await requestContext.Server.TryElicit(new AgentUpdateScalarRequest
            {
                AgentId = agentId,
                Model = model ?? string.Empty,
                Name = name,
                Instructions = instructions,
                ClearName = clearName,
                ClearInstructions = clearInstructions
            }, cancellationToken);
            if (rejected is not null) return rejected;

            return await requestContext.WithStructuredContent(async () =>
            {
                ValidateRequired(input.AgentId, "agentId");
                if (input.ClearName && input.Name is not null)
                    throw new ValidationException("name and clearName cannot be used together.");
                if (input.ClearInstructions && input.Instructions is not null)
                    throw new ValidationException("instructions and clearInstructions cannot be used together.");

                var body = new JsonObject();
                SetOptionalString(body, "model", input.Model);
                if (input.ClearName) body["name"] = null; else SetOptionalString(body, "name", input.Name);
                if (input.ClearInstructions) body["instructions"] = null; else SetOptionalString(body, "instructions", input.Instructions);
                EnsureMutation(body);
                return await UpdateAsync(serviceProvider, input.AgentId, body, cancellationToken);
            });
        });

    [Description("Permanently delete a reusable OpenAI agent after explicit typed confirmation.")]
    [McpServerTool(Title = "Delete OpenAI Agent", Name = "openai_agents_delete", ReadOnly = false, OpenWorld = false, Destructive = true)]
    public static async Task<CallToolResult?> OpenAIAgents_Delete(
        [Description("Agent ID.")] string agentId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            ValidateRequired(agentId, "agentId");
            return await requestContext.ConfirmAndDeleteAsync<OpenAIDeleteAgent>(
                agentId,
                async ct => await OpenAIAgentsHttp.SendAsync(
                    serviceProvider,
                    HttpMethod.Delete,
                    BuildAgentUrl(agentId),
                    null,
                    ct),
                $"OpenAI agent '{agentId}' deleted successfully.",
                cancellationToken);
        });
}
