using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Mixedbread;

public static class MixedbreadStoreFiles
{
    [Description("Add a file to a Mixedbread store.")]
    [McpServerTool(
        Title = "Mixedbread Add File To Store",
        Name = "mixedbread_storefiles_add",
        OpenWorld = true,
        ReadOnly = false,
        Destructive = false)]
    public static async Task<CallToolResult?> MixedbreadStoreFiles_Add(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Store identifier (ID or name).")]
        string storeIdentifier,
        [Description("File ID to add.")]
        string fileId,
        [Description("Optional external_id for the file.")] string? externalId = null,
        [Description("Whether to overwrite existing file with the same external_id. Default true.")]
        bool? overwrite = null,
        [Description("Optional metadata JSON string.")] string? metadataJson = null,
        [Description("Optional config JSON string (e.g., {\"parsing_strategy\":\"fast\"}).")]
        string? configJson = null,
        [Description("Optional deprecated experimental JSON string.")] string? experimentalJson = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(storeIdentifier);
                ArgumentException.ThrowIfNullOrWhiteSpace(fileId);

                var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                    new MixedbreadStoreFileAddRequest
                    {
                        StoreIdentifier = storeIdentifier,
                        FileId = fileId,
                        ExternalId = externalId,
                        Overwrite = overwrite,
                        MetadataJson = metadataJson,
                        ConfigJson = configJson,
                        ExperimentalJson = experimentalJson
                    },
                    cancellationToken);          

                var payload = new JsonObject
                {
                    ["file_id"] = typed.FileId
                };

                if (!string.IsNullOrWhiteSpace(typed.ExternalId)) payload["external_id"] = typed.ExternalId;
                if (typed.Overwrite.HasValue) payload["overwrite"] = typed.Overwrite.Value;
                AddJson(payload, "metadata", typed.MetadataJson);
                AddJson(payload, "config", typed.ConfigJson);
                AddJson(payload, "experimental", typed.ExperimentalJson);

                var settings = serviceProvider.GetRequiredService<MixedbreadSettings>();
                using var client = MixedbreadHttp.CreateClient(serviceProvider, settings);
                using var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/stores/{Uri.EscapeDataString(storeIdentifier)}/files")
                {
                    Content = MixedbreadHttp.CreateJsonContent(payload)
                };

                var json = await MixedbreadHttp.SendAsync(client, request, cancellationToken);
                return json;
            }));

    [Description("Delete a file from a Mixedbread store.")]
    [McpServerTool(
        Title = "Mixedbread Delete Store File",
        Name = "mixedbread_storefiles_delete",
        OpenWorld = true,
        ReadOnly = false,
        Destructive = true)]
    public static async Task<CallToolResult?> MixedbreadStoreFiles_Delete(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Store identifier (ID or name).")]
        string storeIdentifier,
        [Description("File identifier (ID or external_id).")]
        string fileIdentifier,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                if (string.IsNullOrWhiteSpace(storeIdentifier))
                    throw new ArgumentException("storeIdentifier is required.");
                if (string.IsNullOrWhiteSpace(fileIdentifier))
                    throw new ArgumentException("fileIdentifier is required.");

                var settings = serviceProvider.GetRequiredService<MixedbreadSettings>();
                var expectedName = $"{storeIdentifier}/{fileIdentifier}";

                return await requestContext.ConfirmAndDeleteAsync<ConfirmDeleteMixedbreadStoreFile>(
                    expectedName,
                    async ct =>
                    {
                        using var client = MixedbreadHttp.CreateClient(serviceProvider, settings);
                        using var request = new HttpRequestMessage(HttpMethod.Delete,
                            $"/v1/stores/{Uri.EscapeDataString(storeIdentifier)}/files/{Uri.EscapeDataString(fileIdentifier)}");
                        _ = await MixedbreadHttp.SendAsync(client, request, ct);
                    },
                    $"Store file '{fileIdentifier}' deleted successfully from '{storeIdentifier}'.",
                    cancellationToken);
            }));

    private static void AddJson(JsonObject payload, string name, string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        var node = JsonNode.Parse(json);
        if (node != null) payload[name] = node;
    }
}

[Description("Please confirm the Mixedbread store file add.")]
public sealed class MixedbreadStoreFileAddRequest
{
    [JsonPropertyName("storeIdentifier")]
    [Required]
    [Description("Store identifier (ID or name).")]
    public string StoreIdentifier { get; set; } = default!;

    [JsonPropertyName("fileId")]
    [Required]
    [Description("File ID to add.")]
    public string FileId { get; set; } = default!;

    [JsonPropertyName("externalId")]
    [Description("External ID for the file.")]
    public string? ExternalId { get; set; }

    [JsonPropertyName("overwrite")]
    [Description("Overwrite existing file with same external_id.")]
    public bool? Overwrite { get; set; }

    [JsonPropertyName("metadataJson")]
    [Description("Metadata JSON string.")]
    public string? MetadataJson { get; set; }

    [JsonPropertyName("configJson")]
    [Description("Config JSON string.")]
    public string? ConfigJson { get; set; }

    [JsonPropertyName("experimentalJson")]
    [Description("Experimental JSON string.")]
    public string? ExperimentalJson { get; set; }

}

[Description("Please confirm deletion of the store/file: {0}")]
public sealed class ConfirmDeleteMixedbreadStoreFile : IHasName
{
    [JsonPropertyName("name")]
    [Required]
    [Description("Store/file identifier to delete (must match exactly).")]
    public string Name { get; set; } = default!;
}
