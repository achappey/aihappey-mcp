using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MCPhappey.Auth.Extensions;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Common.Models;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OpenAI;
using OpenAI.VectorStores;
using OAIV = OpenAI.VectorStores;
using OAIF = OpenAI.Files;

namespace MCPhappey.Tools.OpenAI.VectorStores;

public static partial class OpenAIVectorStores
{

    public static bool IsOwner(this VectorStore store, string? userId)
        => userId != null && store.Metadata.ContainsKey(VectorStoreExtensions.OWNERS_KEY)
            && store.Metadata[VectorStoreExtensions.OWNERS_KEY].Contains(userId);

    [Description("Update a vector store at OpenAI")]
    [McpServerTool(
       Title = "Update a vector store",
       OpenWorld = false)]
    public static async Task<CallToolResult?> OpenAIVectorStores_Update(
       [Description("The vector store id.")] string vectorStoreId,
       IServiceProvider serviceProvider,
       RequestContext<CallToolRequestParams> requestContext,
       [Description("New name (defaults to current).")] string? name = null,
       [Description("New description (defaults to current).")] string? description = null,
       CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await serviceProvider.WithVectorStoreOwnerClient<CallToolResult?>(vectorStoreId, async (client, current) =>
            {
                // Current values
                var currentName = current.Name;
                current.Metadata.TryGetValue(VectorStoreExtensions.BASE_URL, out var currentDescription);

                // Prepare elicitation payload with defaults from method params (fallback to current)
                var input = new OpenAIEditVectorStore
                {
                    Name = !string.IsNullOrWhiteSpace(name) ? name! : currentName,
                    Description = description ?? currentDescription
                };

                var (typed, notAccepted, result) = await requestContext.Server.TryElicit(input, cancellationToken);

                // Build update options; preserve existing metadata (Owners/Visibility/etc.)
                var newMetadata = new Dictionary<string, string>(current.Metadata);
                if (typed.Description != null)
                    newMetadata[VectorStoreExtensions.DESCRIPTION_KEY] = typed.Description;

                // SDK naming differs by version; both are common. Use the one your package exposes.
                var updateOptions = new VectorStoreModificationOptions
                {
                    Name = typed.Name,
                };

                if (!string.IsNullOrEmpty(typed.Description))
                    updateOptions.Metadata.Add(VectorStoreExtensions.DESCRIPTION_KEY, typed.Description);
                else
                    updateOptions.Metadata.Add(VectorStoreExtensions.DESCRIPTION_KEY, string.Empty);

                foreach (var i in current.Metadata
                    .Where(z => !updateOptions.Metadata.ContainsKey(z.Key)))
                {
                    updateOptions.Metadata.Add(i.Value, i.Key);
                }

                var updated = await client.ModifyVectorStoreAsync(vectorStoreId, updateOptions, cancellationToken);

                return updated?.ToJsonContentBlock($"{VectorStoreExtensions.BASE_URL}/{vectorStoreId}")
                    .ToCallToolResult();
            }));

    [Description("Add an owner to an OpenAI vector store")]
    [McpServerTool(
      Title = "Add vector store owner",
      Destructive = false,
      OpenWorld = false)]
    public static async Task<CallToolResult?> OpenAIVectorStores_AddOwner(
      [Description("The vector store id.")] string vectorStoreId,
      [Description("The user id of the new owner.")] string ownerId,
      IServiceProvider serviceProvider,
      RequestContext<CallToolRequestParams> requestContext,
      CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await serviceProvider.WithVectorStoreOwnerClient<CallToolResult?>(vectorStoreId, async (client, current) =>
            {
                var currentName = current.Name;
                current.Metadata.TryGetValue(VectorStoreExtensions.OWNERS_KEY, out var currentDescription);

                // Prepare elicitation payload with defaults from method params (fallback to current)
                var input = new OpenAIAddVectorStoreOwner
                {
                    UserId = ownerId
                };

                var (typed, notAccepted, result) = await requestContext.Server.TryElicit(input, cancellationToken);

                // Build update options; preserve existing metadata (Owners/Visibility/etc.)
                var updateOptions = new VectorStoreModificationOptions
                {

                };

                var currentOwners = currentDescription?.Split(",")?.ToList() ?? [];

                if (!string.IsNullOrEmpty(typed.UserId) && !currentOwners.Contains(typed.UserId))
                    currentOwners.Add(typed.UserId);

                updateOptions.Metadata.Add(VectorStoreExtensions.OWNERS_KEY, string.Join(",", currentOwners));

                foreach (var i in current.Metadata
                    .Where(z => !updateOptions.Metadata.ContainsKey(z.Key)))
                {
                    updateOptions.Metadata.Add(i.Value, i.Key);
                }

                var updated = await client.ModifyVectorStoreAsync(vectorStoreId, updateOptions, cancellationToken);

                return updated?.ToJsonContentBlock($"{VectorStoreExtensions.BASE_URL}/{vectorStoreId}")
                    .ToCallToolResult();

            }));

    [Description("Create a vector store at OpenAI")]
    [McpServerTool(Title = "Create a vector store", Destructive = false, OpenWorld = false)]
    public static async Task<CallToolResult?> OpenAIVectorStores_Create(
        [Description("The vector store name.")] string name,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        =>
        await requestContext.WithExceptionCheck(async () =>
        await serviceProvider.WithVectorStoreClient(async (client) =>
        {
            var userId = serviceProvider.GetUserId();

            var imageInput = new OpenAINewVectorStore
            {
                Name = name
            };

            var (typed, notAccepted, result) = await requestContext.Server.TryElicit(imageInput, cancellationToken);

            var options = new OAIV.VectorStoreCreationOptions()
            {
                Name = typed.Name
            };

            options.Metadata.Add(VectorStoreExtensions.OWNERS_KEY, userId);
            options.Metadata.Add("Visibility", VectorStoreExtensions.OWNERS_KEY);

            if (!string.IsNullOrEmpty(typed.Description))
                options.Metadata.Add(VectorStoreExtensions.DESCRIPTION_KEY, typed.Description);

            var item = await client.CreateVectorStoreAsync(options, cancellationToken);

            return item?.ToJsonContentBlock($"{VectorStoreExtensions.BASE_URL}/{item.Value.Id}").ToCallToolResult();
        }));

    [Description("Add a file to a vector store")]
    [McpServerTool(Title = "Add file to vector store", Destructive = true, OpenWorld = false)]
    public static async Task<CallToolResult?> OpenAIVectorStores_AddFile(
       [Description("The vector store name.")] string vectorStoreId,
       [Description("Url of the file to add.")] string fileUrl,
       IServiceProvider serviceProvider,
       RequestContext<CallToolRequestParams> requestContext,
       [Description("Wait until complete.")] bool? waitUntilCompleted = true,
       CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await serviceProvider.WithVectorStoreOwnerClient<CallToolResult>(vectorStoreId, async (client, current) =>
        {
            var openAiClient = serviceProvider.GetRequiredService<OpenAIClient>();
            var downloadService = serviceProvider.GetRequiredService<DownloadService>();
            var fileClient = openAiClient.GetOpenAIFileClient();
            var downloads = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);

            List<string> fileIds = [];

            foreach (var download in downloads)
            {
                var fileItem = await fileClient.UploadFileAsync(download.Contents, download.Filename, OAIF.FileUploadPurpose.UserData);

                fileIds.Add(fileItem.Value.Id);
            }

            var item = client.AddFileBatchToVectorStoreAsync(vectorStoreId, fileIds, cancellationToken);

            return item?.ToJsonContentBlock($"{VectorStoreExtensions.BASE_URL}/{vectorStoreId}/files").ToCallToolResult();
        }));

    [Description("Delete a vector store at OpenAI")]
    [McpServerTool(Title = "Delete a vector store at OpenAI",
        OpenWorld = false)]
    public static async Task<CallToolResult?> OpenAIVectorStores_Delete(
        [Description("The vector store id.")] string vectorStoreId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
         => await requestContext.WithExceptionCheck(async () =>
            await serviceProvider.WithVectorStoreOwnerClient<CallToolResult>(vectorStoreId, async (client, item) =>
            await requestContext.ConfirmAndDeleteAsync<OpenAIDeleteVectorStore>(
                    item?.Name!,
                    async _ => await client.DeleteVectorStoreAsync(vectorStoreId, cancellationToken),
                $"Vector store {item?.Name} deleted.",
                cancellationToken), cancellationToken));

    [Description("Please fill in the vector store details.")]
    public class OpenAINewVectorStore
    {
        [JsonPropertyName("name")]
        [Required]
        [Description("The vector store name.")]
        public string Name { get; set; } = default!;

        [JsonPropertyName("description")]
        [Description("The vector description.")]
        public string? Description { get; set; }
    }

    [Description("Please fill in the vector store details.")]
    public class OpenAIEditVectorStore
    {
        [JsonPropertyName("name")]
        [Required]
        [Description("The vector store name.")]
        public string Name { get; set; } = default!;

        [JsonPropertyName("description")]
        [Description("The vector description.")]
        public string? Description { get; set; }
    }

    [Description("Please fill in the vector store details.")]
    public class OpenAIDeleteVectorStore : IHasName
    {
        [JsonPropertyName("name")]
        [Required]
        [Description("The vector store name.")]
        public string Name { get; set; } = default!;
    }

    [Description("Please fill in the vector store owner details.")]
    public class OpenAIAddVectorStoreOwner
    {
        [JsonPropertyName("userId")]
        [Required]
        [Description("The user id of the new owner.")]
        public string UserId { get; set; } = string.Empty;
    }
}

