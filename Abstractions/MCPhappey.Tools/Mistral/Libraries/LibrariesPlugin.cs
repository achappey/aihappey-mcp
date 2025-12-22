using System.ComponentModel;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;
using MCPhappey.Common.Models;

namespace MCPhappey.Tools.Mistral.Libraries;

public static partial class LibrariesPlugin
{
    private const string BaseUrl = "https://api.mistral.ai/v1/libraries";

    [Description("Please confirm deletion of the library with this exact ID: {0}")]
    public class DeleteLibrary : IHasName
    {
        [JsonPropertyName("name")]
        [Description("ID of the library.")]
        public string Name { get; set; } = default!;
    }

    [Description("Delete a Mistral library by ID.")]
    [McpServerTool(
        IconSource = MistralConstants.ICON_SOURCE,
        Title = "Delete Mistral library",
        Name = "mistral_libraries_delete",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> MistralLibraries_Delete(
        [Description("The ID of the library to delete.")] string libraryId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            var mistral = serviceProvider.GetRequiredService<MistralClient>();

            return await requestContext.ConfirmAndDeleteAsync<DeleteLibrary>(
                expectedName: libraryId,
                deleteAction: async _ =>
                {
                    var resp = await mistral.DeleteLibraryAsync(libraryId, cancellationToken);
                    var json = await resp.Content.ReadAsStringAsync(cancellationToken);

                    if (!resp.IsSuccessStatusCode)
                        throw new Exception($"{resp.StatusCode}: {json}");
                },
                successText: $"Library '{libraryId}' deleted successfully!",
                ct: cancellationToken);
        }));

    [Description("Create a new Mistral library that can be used in conversations.")]
    [McpServerTool(
        IconSource = MistralConstants.ICON_SOURCE,
        Title = "Create Mistral library",
        Name = "mistral_libraries_create",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = false)]
    public static async Task<CallToolResult?> MistralLibraries_Create(
        [Description("Name of the new library.")] string name,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional description of the library.")] string? description = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new CreateLibrary
                {
                    Name = name,
                    Description = description
                },
                cancellationToken
            );

            var body = new Dictionary<string, object?>
            {
                ["name"] = typed.Name,
                ["description"] = typed.Description
            };

            var mistral = serviceProvider.GetRequiredService<MistralClient>();
            var resp = await mistral.CreateLibraryAsync(body, cancellationToken);
            var json = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"{resp.StatusCode}: {json}");

            return json.ToJsonCallToolResponse($"{BaseUrl}");
        }));

    public class CreateLibrary
    {
        [Required]
        [JsonPropertyName("name")]
        [Description("The name of the library.")]
        public string Name { get; set; } = default!;

        [JsonPropertyName("description")]
        [Description("Optional description of the library.")]
        public string? Description { get; set; }
    }
}
