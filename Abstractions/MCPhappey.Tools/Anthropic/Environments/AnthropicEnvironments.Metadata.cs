using System.ComponentModel;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Anthropic.Environments;

public static partial class AnthropicEnvironments
{
    [Description("Set or replace a single metadata key on an Anthropic environment using a flat form.")]
    [McpServerTool(
        Title = "Set Anthropic Environment metadata value",
        Name = "anthropic_environments_set_metadata_value",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = false)]
    public static async Task<CallToolResult?> AnthropicEnvironments_SetMetadataValue(
        [Description("Environment ID.")] string environmentId,
        [Description("Metadata key. Maximum 64 characters using letters, numbers, dot, underscore, colon, or hyphen.")] string key,
        [Description("Metadata value. Maximum 512 characters.")] string value,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var (typed, _, _) = await requestContext.Server.TryElicit(new AnthropicEnvironmentMetadataMutationRequest
                {
                    EnvironmentId = environmentId,
                    Key = key,
                    Value = value
                }, cancellationToken);

                var normalizedEnvironmentId = NormalizeEnvironmentId(typed.EnvironmentId);
                var normalizedKey = NormalizeMetadataKey(typed.Key);
                var normalizedValue = NormalizeMetadataValue(typed.Value);

                var body = new JsonObject
                {
                    ["metadata"] = new JsonObject
                    {
                        [normalizedKey] = normalizedValue
                    }
                };

                return await UpdateEnvironmentAsync(
                    serviceProvider,
                    normalizedEnvironmentId,

                    body,
                    cancellationToken);
            }));

    [Description("Remove a single metadata key from an Anthropic environment after explicit typed confirmation.")]
    [McpServerTool(
        Title = "Remove Anthropic Environment metadata value",
        Name = "anthropic_environments_remove_metadata_value",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> AnthropicEnvironments_RemoveMetadataValue(
        [Description("Environment ID.")] string environmentId,
        [Description("Metadata key to remove.")] string key,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,

        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var normalizedEnvironmentId = NormalizeEnvironmentId(environmentId);
                var normalizedKey = NormalizeMetadataKey(key);
                await AnthropicManagedAgentsHttp.ConfirmDeleteAsync<AnthropicDeleteEnvironment>(requestContext.Server, $"{normalizedEnvironmentId}:{normalizedKey}", cancellationToken);

                var body = new JsonObject
                {
                    ["metadata"] = new JsonObject
                    {
                        [normalizedKey] = null
                    }
                };

                return await UpdateEnvironmentAsync(
                    serviceProvider,
                    normalizedEnvironmentId,

                    body,
                    cancellationToken);
            }));
}
