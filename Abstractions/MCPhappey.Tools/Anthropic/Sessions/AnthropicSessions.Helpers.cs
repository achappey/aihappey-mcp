using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using MCPhappey.Auth.Extensions;
using MCPhappey.Tools.Anthropic;

namespace MCPhappey.Tools.Anthropic.Sessions;

public static partial class AnthropicSessions
{
    internal const string OwnersKey = "Owners";
    internal const string BaseUrl = $"{AnthropicManagedAgentsHttp.ApiBaseUrl}/v1/sessions";

    internal static async Task<JsonObject> GetSessionAsync(
        IServiceProvider serviceProvider,
        string sessionId,
        string? anthropicBetaCsv,
        CancellationToken cancellationToken)
        => await AnthropicManagedAgentsHttp.GetJsonObjectAsync(
            serviceProvider,
            $"{BaseUrl}/{Uri.EscapeDataString(NormalizeSessionId(sessionId))}",
            anthropicBetaCsv,
            cancellationToken);

    internal static async Task<JsonObject> GetOwnerSessionAsync(
        IServiceProvider serviceProvider,
        string sessionId,
        string? anthropicBetaCsv,
        CancellationToken cancellationToken)
    {
        var current = await GetSessionAsync(serviceProvider, sessionId, anthropicBetaCsv, cancellationToken);
        if (!current.IsOwner(serviceProvider.GetUserId()))
            throw new UnauthorizedAccessException("Only owners can access this Anthropic session.");

        return current;
    }

    internal static bool IsOwner(this JsonObject session, string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return false;

        return GetOwners(session)
            .Any(owner => string.Equals(owner, userId, StringComparison.OrdinalIgnoreCase));
    }

    internal static List<string> GetOwners(JsonObject session)
    {
        var metadata = session["metadata"] as JsonObject;
        if (metadata is null || metadata[OwnersKey] is null)
            return [];

        return AnthropicManagedAgentsHttp.ParseDelimited(metadata[OwnersKey]?.GetValue<string>());
    }

    internal static string NormalizeSessionId(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ValidationException("sessionId is required.");

        return sessionId.Trim();
    }
}
