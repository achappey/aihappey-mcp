using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using MCPhappey.Auth.Extensions;
using MCPhappey.Common.Extensions;
using MCPhappey.Tools.Anthropic;
using ModelContextProtocol.Protocol;

namespace MCPhappey.Tools.Anthropic.MemoryStores;

public static partial class AnthropicMemoryStores
{
    internal const string OwnersKey = "Owners";
    internal const string BaseUrl = $"{AnthropicManagedAgentsHttp.ApiBaseUrl}/v1/memory_stores";

    internal static async Task<JsonObject> GetMemoryStoreAsync(
        IServiceProvider serviceProvider,
        string memoryStoreId,
        string? anthropicBetaCsv,
        CancellationToken cancellationToken)
        => await AnthropicManagedAgentsHttp.GetJsonObjectAsync(
            serviceProvider,
            $"{BaseUrl}/{Uri.EscapeDataString(NormalizeMemoryStoreId(memoryStoreId))}",
            anthropicBetaCsv,
            cancellationToken);

    internal static async Task<JsonObject> GetOwnerMemoryStoreAsync(
        IServiceProvider serviceProvider,
        string memoryStoreId,
        string? anthropicBetaCsv,
        CancellationToken cancellationToken)
    {
        var current = await GetMemoryStoreAsync(serviceProvider, memoryStoreId, anthropicBetaCsv, cancellationToken);
        if (!current.IsOwner(serviceProvider.GetUserId()))
            throw new UnauthorizedAccessException("Only owners can access this Anthropic memory store.");

        return current;
    }

    internal static bool IsOwner(this JsonObject memoryStore, string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return false;

        return GetOwners(memoryStore)
            .Any(owner => string.Equals(owner, userId, StringComparison.OrdinalIgnoreCase));
    }

    internal static List<string> GetOwners(JsonObject memoryStore)
    {
        var metadata = memoryStore["metadata"] as JsonObject;
        if (metadata is null || metadata[OwnersKey] is null)
            return [];

        return AnthropicManagedAgentsHttp.ParseDelimited(metadata[OwnersKey]?.GetValue<string>());
    }

    internal static JsonObject CloneMetadata(JsonObject memoryStore)
        => AnthropicManagedAgentsHttp.CloneObject(memoryStore["metadata"]);

    internal static void SetOwners(JsonObject metadata, IEnumerable<string> owners)
        => metadata[OwnersKey] = string.Join(',', owners
            .Where(static owner => !string.IsNullOrWhiteSpace(owner))
            .Distinct(StringComparer.OrdinalIgnoreCase));

    internal static string NormalizeMemoryStoreId(string memoryStoreId)
    {
        if (string.IsNullOrWhiteSpace(memoryStoreId))
            throw new ValidationException("memoryStoreId is required.");

        return memoryStoreId.Trim();
    }

    internal static string NormalizeId(string value, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ValidationException($"{propertyName} is required.");

        return value.Trim();
    }

    internal static void SetStringIfProvided(JsonObject body, string propertyName, string? value)
    {
        if (value is not null)
            body[propertyName] = value;
    }

    internal static CallToolResult ToAnthropicJsonCallToolResult(this JsonNode node, string uri)
        => node.ToJsonString().ToJsonCallToolResponse(uri);

    internal static async Task<CallToolResult?> WithOwnerMemoryStoreAsync(
        IServiceProvider serviceProvider,
        string memoryStoreId,
        string? anthropicBetaCsv,
        Func<JsonObject, Task<CallToolResult?>> func,
        CancellationToken cancellationToken)
    {
        var current = await GetOwnerMemoryStoreAsync(serviceProvider, memoryStoreId, anthropicBetaCsv, cancellationToken);
        return await func(current);
    }
}

