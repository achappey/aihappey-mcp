using System.ComponentModel;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Anthropic.Sessions;

public static partial class AnthropicSessions
{
    [Description("Delete an Anthropic session after explicit typed confirmation. Only owners can delete.")]
    [McpServerTool(Title = "Delete Anthropic Session", Name = "anthropic_sessions_delete", ReadOnly = false, OpenWorld = false, Destructive = true)]
    public static async Task<CallToolResult?> AnthropicSessions_Delete(
        [Description("Session ID to delete.")] string sessionId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional extra anthropic-beta values as comma, semicolon, or newline separated strings.")] string? anthropicBetaCsv = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var normalizedSessionId = NormalizeSessionId(sessionId);
                await GetOwnerSessionAsync(serviceProvider, normalizedSessionId, anthropicBetaCsv, cancellationToken);
                await AnthropicManagedAgentsHttp.ConfirmDeleteAsync<AnthropicDeleteSessionItem>(requestContext.Server, normalizedSessionId, cancellationToken);

                return await AnthropicManagedAgentsHttp.SendAsync(
                    serviceProvider,
                    HttpMethod.Delete,
                    $"{BaseUrl}/{Uri.EscapeDataString(normalizedSessionId)}",
                    null,
                    anthropicBetaCsv,
                    cancellationToken);
            }));
}
