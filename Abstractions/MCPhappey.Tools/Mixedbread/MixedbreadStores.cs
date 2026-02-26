using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common.Models;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Mixedbread;

public static class MixedbreadStores
{
    [Description("Create a new Mixedbread store.")]
    [McpServerTool(
        Title = "Mixedbread Create Store",
        Name = "mixedbread_stores_create",
        OpenWorld = true,
        ReadOnly = false,
        Destructive = false)]
    public static async Task<CallToolResult?> MixedbreadStores_Create(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Store name. Lowercase letters, numbers, periods, hyphens.")] string name,
        [Description("Optional store description.")] string? description = null,
        [Description("Whether the store is public. Default false.")] bool? isPublic = null,
        [Description("Optional expires_after JSON string.")] string? expiresAfterJson = null,
        [Description("Optional metadata JSON string.")] string? metadataJson = null,
        [Description("Optional file_ids JSON array string.")] string? fileIdsJson = null,
        [Description("Optional config JSON string.")] string? configJson = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(name);

                var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                    new MixedbreadStoreCreateRequest
                    {
                        Name = name,
                        Description = description,
                        IsPublic = isPublic,
                        ExpiresAfterJson = expiresAfterJson,
                        MetadataJson = metadataJson,
                        FileIdsJson = fileIdsJson,
                        ConfigJson = configJson
                    },
                    cancellationToken);

                var payload = new JsonObject
                {
                    ["name"] = typed.Name
                };

                if (!string.IsNullOrWhiteSpace(typed.Description)) payload["description"] = typed.Description;
                if (typed.IsPublic.HasValue) payload["is_public"] = typed.IsPublic.Value;
                AddJson(payload, "expires_after", typed.ExpiresAfterJson);
                AddJson(payload, "metadata", typed.MetadataJson);
                AddJson(payload, "file_ids", typed.FileIdsJson);
                AddJson(payload, "config", typed.ConfigJson);

                var settings = serviceProvider.GetRequiredService<MixedbreadSettings>();
                using var client = MixedbreadHttp.CreateClient(serviceProvider, settings);
                using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/stores")
                {
                    Content = MixedbreadHttp.CreateJsonContent(payload)
                };

                var json = await MixedbreadHttp.SendAsync(client, request, cancellationToken);
                return json;
            }));

    [Description("Update a Mixedbread store by ID or name.")]
    [McpServerTool(
        Title = "Mixedbread Update Store",
        Name = "mixedbread_stores_update",
        OpenWorld = true,
        ReadOnly = false,
        Destructive = false)]
    public static async Task<CallToolResult?> MixedbreadStores_Update(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Store identifier (ID or name).")]
        string storeIdentifier,
        [Description("Optional new store name.")] string? name = null,
        [Description("Optional store description.")] string? description = null,
        [Description("Whether the store is public.")] bool? isPublic = null,
        [Description("Optional expires_after JSON string.")] string? expiresAfterJson = null,
        [Description("Optional metadata JSON string.")] string? metadataJson = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(storeIdentifier);

                var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                    new MixedbreadStoreUpdateRequest
                    {
                        StoreIdentifier = storeIdentifier,
                        Name = name,
                        Description = description,
                        IsPublic = isPublic,
                        ExpiresAfterJson = expiresAfterJson,
                        MetadataJson = metadataJson
                    },
                    cancellationToken);

                var payload = new JsonObject();
                if (!string.IsNullOrWhiteSpace(typed.Name)) payload["name"] = typed.Name;
                if (!string.IsNullOrWhiteSpace(typed.Description)) payload["description"] = typed.Description;
                if (typed.IsPublic.HasValue) payload["is_public"] = typed.IsPublic.Value;
                AddJson(payload, "expires_after", typed.ExpiresAfterJson);
                AddJson(payload, "metadata", typed.MetadataJson);

                var settings = serviceProvider.GetRequiredService<MixedbreadSettings>();
                using var client = MixedbreadHttp.CreateClient(serviceProvider, settings);
                using var request = new HttpRequestMessage(HttpMethod.Put, $"/v1/stores/{Uri.EscapeDataString(storeIdentifier)}")
                {
                    Content = MixedbreadHttp.CreateJsonContent(payload)
                };

                var json = await MixedbreadHttp.SendAsync(client, request, cancellationToken);
                return json;
            }));

    [Description("Delete a Mixedbread store by ID or name.")]
    [McpServerTool(
        Title = "Mixedbread Delete Store",
        Name = "mixedbread_stores_delete",
        OpenWorld = true,
        ReadOnly = false,
        Destructive = true)]
    public static async Task<CallToolResult?> MixedbreadStores_Delete(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Store identifier (ID or name).")]
        string storeIdentifier,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                if (string.IsNullOrWhiteSpace(storeIdentifier))
                    throw new ArgumentException("storeIdentifier is required.");

                var settings = serviceProvider.GetRequiredService<MixedbreadSettings>();

                return await requestContext.ConfirmAndDeleteAsync<ConfirmDeleteMixedbreadStore>(
                    storeIdentifier,
                    async ct =>
                    {
                        using var client = MixedbreadHttp.CreateClient(serviceProvider, settings);
                        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/v1/stores/{Uri.EscapeDataString(storeIdentifier)}");
                        _ = await MixedbreadHttp.SendAsync(client, request, ct);
                    },
                    $"Store '{storeIdentifier}' deleted successfully.",
                    cancellationToken);
            }));

    private static void AddJson(JsonObject payload, string name, string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        var node = JsonNode.Parse(json);
        if (node != null) payload[name] = node;
    }
}

[Description("Please confirm the Mixedbread store creation.")]
public sealed class MixedbreadStoreCreateRequest
{
    [JsonPropertyName("name")]
    [Required]
    [Description("Store name.")]
    public string Name { get; set; } = default!;

    [JsonPropertyName("description")]
    [Description("Store description.")]
    public string? Description { get; set; }

    [JsonPropertyName("isPublic")]
    [Description("Whether the store is public.")]
    public bool? IsPublic { get; set; }

    [JsonPropertyName("expiresAfterJson")]
    [Description("Expires-after JSON string.")]
    public string? ExpiresAfterJson { get; set; }

    [JsonPropertyName("metadataJson")]
    [Description("Metadata JSON string.")]
    public string? MetadataJson { get; set; }

    [JsonPropertyName("fileIdsJson")]
    [Description("File IDs JSON array string.")]
    public string? FileIdsJson { get; set; }

    [JsonPropertyName("configJson")]
    [Description("Config JSON string.")]
    public string? ConfigJson { get; set; }
}

[Description("Please confirm the Mixedbread store update.")]
public sealed class MixedbreadStoreUpdateRequest
{
    [JsonPropertyName("storeIdentifier")]
    [Required]
    [Description("Store identifier (ID or name).")]
    public string StoreIdentifier { get; set; } = default!;

    [JsonPropertyName("name")]
    [Description("New store name.")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    [Description("Store description.")]
    public string? Description { get; set; }

    [JsonPropertyName("isPublic")]
    [Description("Whether the store is public.")]
    public bool? IsPublic { get; set; }

    [JsonPropertyName("expiresAfterJson")]
    [Description("Expires-after JSON string.")]
    public string? ExpiresAfterJson { get; set; }

    [JsonPropertyName("metadataJson")]
    [Description("Metadata JSON string.")]
    public string? MetadataJson { get; set; }
}

[Description("Please confirm deletion of the store identifier: {0}")]
public sealed class ConfirmDeleteMixedbreadStore : IHasName
{
    [JsonPropertyName("name")]
    [Required]
    [Description("The store identifier to delete (must match exactly).")]
    public string Name { get; set; } = default!;
}
