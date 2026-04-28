using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using MCPhappey.Auth.Extensions;
using ModelContextProtocol.Protocol;

namespace MCPhappey.Tools.Anthropic.Vaults;

public static partial class AnthropicVaults
{
    internal const string OwnersKey = "Owners";
    internal const string BaseUrl = $"{AnthropicManagedAgentsHttp.ApiBaseUrl}/v1/vaults";

    internal static async Task<JsonObject> GetVaultAsync(
        IServiceProvider serviceProvider,
        string vaultId,

        CancellationToken cancellationToken)
        => await AnthropicManagedAgentsHttp.GetJsonObjectAsync(
            serviceProvider,
            $"{BaseUrl}/{Uri.EscapeDataString(NormalizeVaultId(vaultId))}",

            cancellationToken);

    internal static async Task<JsonObject> GetOwnerVaultAsync(
        IServiceProvider serviceProvider,
        string vaultId,

        CancellationToken cancellationToken)
    {
        var current = await GetVaultAsync(serviceProvider, vaultId, cancellationToken);
        if (!current.IsOwner(serviceProvider.GetUserId()))
            throw new UnauthorizedAccessException("Only owners can access this Anthropic vault.");

        return current;
    }

    internal static bool IsOwner(this JsonObject vault, string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return false;

        return GetOwners(vault)
            .Any(owner => string.Equals(owner, userId, StringComparison.OrdinalIgnoreCase));
    }

    internal static List<string> GetOwners(JsonObject vault)
    {
        var metadata = vault["metadata"] as JsonObject;
        if (metadata is null || metadata[OwnersKey] is null)
            return [];

        return AnthropicManagedAgentsHttp.ParseDelimited(metadata[OwnersKey]?.GetValue<string>());
    }

    internal static JsonObject CloneMetadata(JsonObject vault)
        => AnthropicManagedAgentsHttp.CloneObject(vault["metadata"]);

    internal static void SetOwners(JsonObject metadata, IEnumerable<string> owners)
        => metadata[OwnersKey] = string.Join(',', owners
            .Where(static owner => !string.IsNullOrWhiteSpace(owner))
            .Distinct(StringComparer.OrdinalIgnoreCase));

    internal static string NormalizeVaultId(string vaultId)
    {
        if (string.IsNullOrWhiteSpace(vaultId))
            throw new ValidationException("vaultId is required.");

        return vaultId.Trim();
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

    internal static JsonObject ParseMetadataJsonOrEmpty(string? metadataJson, string propertyName)
        => AnthropicManagedAgentsHttp.ParseJsonObjectOrNull(metadataJson, propertyName) ?? new JsonObject();

    internal static JsonObject? ParseMetadataPatchJsonOrNull(string? metadataJson, string propertyName)
        => AnthropicManagedAgentsHttp.ParseJsonObjectOrNull(metadataJson, propertyName);

    internal static JsonObject ParseJsonObject(string json, string propertyName)
        => AnthropicManagedAgentsHttp.ParseJsonObjectOrNull(json, propertyName)
           ?? throw new ValidationException($"{propertyName} is required.");

    internal static CallToolResult ToAnthropicJsonCallToolResult(this JsonNode node, string uri)
        => node.ToJsonString().ToJsonCallToolResponse(uri);
}

