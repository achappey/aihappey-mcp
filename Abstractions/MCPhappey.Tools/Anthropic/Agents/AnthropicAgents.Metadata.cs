using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Anthropic.Agents;

public static partial class AnthropicAgents
{
    [Description("Set or replace a single metadata key on an Anthropic Managed Agent using a flat form.")]
    [McpServerTool(
        Title = "Set Anthropic Agent metadata value",
        Name = "anthropic_agents_set_metadata_value",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = false)]
    public static async Task<CallToolResult?> AnthropicAgents_SetMetadataValue(
        [Description("Agent ID.")] string agentId,
        [Description("Metadata key. Maximum 64 characters.")] string key,
        [Description("Metadata value. Maximum 512 characters.")] string value,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional extra anthropic-beta values as comma, semicolon, or newline separated strings.")] string? anthropicBetaCsv = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var (typed, _, _) = await requestContext.Server.TryElicit(new AnthropicAgentMetadataMutationRequest
                {
                    AgentId = agentId,
                    Key = key,
                    Value = value,
                    AnthropicBetaCsv = anthropicBetaCsv
                }, cancellationToken);

                if (string.IsNullOrWhiteSpace(typed.AgentId))
                    throw new ValidationException("agentId is required.");

                ValidateMetadataKey(typed.Key);
                ValidateMetadataValue(typed.Value);

                var current = await GetAgentAsync(serviceProvider, typed.AgentId, typed.AnthropicBetaCsv, cancellationToken);
                var body = CreateVersionedUpdateBody(current);
                body["metadata"] = new JsonObject
                {
                    [typed.Key] = typed.Value
                };

                return await UpdateAgentAsync(serviceProvider, typed.AgentId, typed.AnthropicBetaCsv, body, cancellationToken);
            }));

    [Description("Remove a single metadata key from an Anthropic Managed Agent after explicit typed confirmation.")]
    [McpServerTool(
        Title = "Remove Anthropic Agent metadata value",
        Name = "anthropic_agents_remove_metadata_value",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> AnthropicAgents_RemoveMetadataValue(
        [Description("Agent ID.")] string agentId,
        [Description("Metadata key to remove.")] string key,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional extra anthropic-beta values as comma, semicolon, or newline separated strings.")] string? anthropicBetaCsv = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ValidateMetadataKey(key);

                var expected = $"{agentId}:{key}";
                await AnthropicManagedAgentsHttp.ConfirmDeleteAsync<AnthropicDeleteAgentItem>(requestContext.Server, expected, cancellationToken);

                var current = await GetAgentAsync(serviceProvider, agentId, anthropicBetaCsv, cancellationToken);
                var body = CreateVersionedUpdateBody(current);
                var metadataPatch = new JsonObject();
                metadataPatch[key] = null;
                body["metadata"] = metadataPatch;

                return await UpdateAgentAsync(serviceProvider, agentId, anthropicBetaCsv, body, cancellationToken);
            }));
}
