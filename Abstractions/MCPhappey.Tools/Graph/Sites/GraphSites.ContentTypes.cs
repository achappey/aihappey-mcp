using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.Sites;

public static partial class GraphSites
{
    [Description(
        "Enable or disable content types on a SharePoint list or document library.")]
    [McpServerTool(
        Title = "Configure SharePoint list content types",
        Name = "graph_sites_set_list_content_types_enabled",
        Destructive = true,
        Idempotent = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?>
        GraphSites_SetListContentTypesEnabled(
            RequestContext<CallToolRequestParams> requestContext,

            [Description(
            "Microsoft Graph site ID containing the list or document library.")]
        string? siteId = null,

            [Description(
            "Microsoft Graph list ID of the SharePoint list or document library.")]
        string? listId = null,

            [Description(
            "True to enable content types; false to disable them.")]
        bool enabled = true,

            CancellationToken cancellationToken = default) =>
            await ModelContextToolExtensions.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async client =>
            await requestContext.WithStructuredContent<object>(async () =>
            {
                var (typed, notAccepted, _) =
                    await requestContext.Server.TryElicit(
                        new GraphSetListContentTypesEnabledInput
                        {
                            SiteId = siteId,
                            ListId = listId,
                            Enabled = enabled
                        },
                        cancellationToken);

                if (notAccepted is not null)
                {
                    throw new Exception(
                        JsonSerializer.Serialize(notAccepted));
                }

                typed!.Validate();

                var currentList = await client
                    .Sites[typed.SiteId!]
                    .Lists[typed.ListId!]
                    .GetAsync(
                        configuration =>
                        {
                            configuration.QueryParameters.Select =
                            [
                                "id",
                            "displayName",
                            "list"
                            ];
                        },
                        cancellationToken);

                if (currentList is null)
                {
                    throw new InvalidOperationException(
                        "Microsoft Graph returned no SharePoint list.");
                }

                var currentValue =
                    currentList.ListProp?.ContentTypesEnabled == true;

                if (currentValue == typed.Enabled)
                {
                    return new
                    {
                        typed.SiteId,
                        typed.ListId,
                        currentList.DisplayName,
                        ContentTypesEnabled = currentValue,
                        Updated = false,
                        Status = typed.Enabled == true
                            ? "Content types are already enabled."
                            : "Content types are already disabled."
                    };
                }

                var update = new Microsoft.Graph.Beta.Models.List
                {
                    ListProp = new Microsoft.Graph.Beta.Models.ListInfo
                    {
                        ContentTypesEnabled = typed.Enabled
                    }
                };

                var updatedList = await client
                    .Sites[typed.SiteId!]
                    .Lists[typed.ListId!]
                    .PatchAsync(
                        update,
                        cancellationToken: cancellationToken);

                var finalValue =
                    updatedList?.ListProp?.ContentTypesEnabled
                    ?? typed.Enabled ?? false;

                return new
                {
                    typed.SiteId,
                    typed.ListId,
                    DisplayName =
                        updatedList?.DisplayName
                        ?? currentList.DisplayName,

                    PreviousContentTypesEnabled = currentValue,
                    ContentTypesEnabled = finalValue,
                    Updated = true,

                    Status = finalValue
                        ? "Enabled content types on the SharePoint list successfully."
                        : "Disabled content types on the SharePoint list successfully."
                };
            })));


    [Description(
    "Set an existing content type as the default content type of a SharePoint list or document library.")]
    [McpServerTool(
    Title = "Set default SharePoint list content type",
    Name = "graph_sharepoint_set_default_content_type",
    Destructive = true,
    Idempotent = true,
    OpenWorld = false)]
    public static async Task<CallToolResult?>
    GraphSharePoint_SetDefaultContentType(
        RequestContext<CallToolRequestParams> requestContext,

        [Description("Microsoft Graph site ID containing the target list.")]
        string? siteId = null,

        [Description("Microsoft Graph list ID of the target list or document library.")]
        string? listId = null,

        [Description(
            "ID of the content type already available on the target list.")]
        string? contentTypeId = null,

        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent<object>(async () =>
        {
            var (typed, notAccepted, _) =
                await requestContext.Server.TryElicit(
                    new GraphSetDefaultListContentTypeInput
                    {
                        SiteId = siteId,
                        ListId = listId,
                        ContentTypeId = contentTypeId
                    },
                    cancellationToken);

            if (notAccepted is not null)
                throw new Exception(JsonSerializer.Serialize(notAccepted));

            typed!.Validate();

            var listContentTypes = await client
                .Sites[typed.SiteId!]
                .Lists[typed.ListId!]
                .ContentTypes
                .GetAsync(
                    configuration =>
                    {
                        configuration.QueryParameters.Select =
                        [
                            "id",
                            "name",
                            "order"
                        ];
                    },
                    cancellationToken);

            var targetContentType = listContentTypes?.Value?
                .FirstOrDefault(item =>
                    string.Equals(
                        item.Id,
                        typed.ContentTypeId,
                        StringComparison.OrdinalIgnoreCase));

            if (targetContentType is null)
            {
                throw new ValidationException(
                    $"Content type {typed.ContentTypeId} is not available on list {typed.ListId}. " +
                    "Add it to the list first.");
            }

            if (targetContentType.Order?.Default == true)
            {
                throw new Exception("The requested content type is already the default.");
            }

            var update = new Microsoft.Graph.Beta.Models.ContentType
            {
                Order = new Microsoft.Graph.Beta.Models.ContentTypeOrder
                {
                    Default = true,
                    Position = 0
                }
            };

            return await client
                .Sites[typed.SiteId!]
                .Lists[typed.ListId!]
                .ContentTypes[typed.ContentTypeId!]
                .PatchAsync(
                    update,
                    cancellationToken: cancellationToken);
        })));

    [Description(
        "Remove a content type from a SharePoint list or document library.")]
    [McpServerTool(
        Title = "Remove content type from SharePoint list",
        Name = "graph_sharepoint_remove_content_type_from_list",
        Destructive = true,
        Idempotent = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?>
        GraphSharePoint_RemoveContentTypeFromList(
            RequestContext<CallToolRequestParams> requestContext,

            [Description("Microsoft Graph site ID containing the target list.")]
        string? siteId = null,

            [Description("Microsoft Graph list ID of the target list or document library.")]
        string? listId = null,

            [Description(
            "ID of the list content type to remove. Use the ID returned by the list's contentTypes collection.")]
        string? contentTypeId = null,

            CancellationToken cancellationToken = default) =>
            await ModelContextToolExtensions.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async client =>
            await requestContext.WithStructuredContent<object>(async () =>
            {
                var (typed, notAccepted, _) =
                    await requestContext.Server.TryElicit(
                        new GraphRemoveListContentTypeInput
                        {
                            SiteId = siteId,
                            ListId = listId,
                            ContentTypeId = contentTypeId
                        },
                        cancellationToken);

                if (notAccepted is not null)
                    throw new Exception(JsonSerializer.Serialize(notAccepted));

                typed!.Validate();

                var listContentTypes = await client
                    .Sites[typed.SiteId!]
                    .Lists[typed.ListId!]
                    .ContentTypes
                    .GetAsync(
                        configuration =>
                        {
                            configuration.QueryParameters.Select =
                            [
                                "id",
                            "name",
                            "order",
                            "readOnly",
                            "sealed"
                            ];
                        },
                        cancellationToken);

                var contentType = listContentTypes?.Value?
                    .FirstOrDefault(item =>
                        string.Equals(
                            item.Id,
                            typed.ContentTypeId,
                            StringComparison.OrdinalIgnoreCase));

                if (contentType is null)
                {
                    return new
                    {
                        typed.SiteId,
                        typed.ListId,
                        typed.ContentTypeId,
                        Removed = false,
                        AlreadyAbsent = true,
                        Status = "The content type is not present on the target list."
                    };
                }

                if (contentType.Order?.Default == true)
                {
                    throw new ValidationException(
                        $"Content type '{contentType.Name}' is currently the default content type. " +
                        "Set another default content type before removing it.");
                }


                await client
                    .Sites[typed.SiteId!]
                    .Lists[typed.ListId!]
                    .ContentTypes[typed.ContentTypeId!]
                    .DeleteAsync(
                        cancellationToken: cancellationToken);

                return new
                {
                    typed.SiteId,
                    typed.ListId,
                    typed.ContentTypeId,
                    contentType.Name,
                    Removed = true,
                    AlreadyAbsent = false,
                    Status = "Removed the content type from the list successfully."
                };
            })));

    [Description(
        "Confirm removing a content type from a SharePoint list or document library.")]
    public sealed class GraphRemoveListContentTypeInput
    {
        [JsonPropertyName("siteId")]
        [Required]
        public string? SiteId { get; set; }

        [JsonPropertyName("listId")]
        [Required]
        public string? ListId { get; set; }

        [JsonPropertyName("contentTypeId")]
        [Required]
        public string? ContentTypeId { get; set; }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(SiteId))
                throw new ValidationException("siteId is required.");

            if (string.IsNullOrWhiteSpace(ListId))
                throw new ValidationException("listId is required.");

            if (string.IsNullOrWhiteSpace(ContentTypeId) ||
                !ContentTypeId.StartsWith(
                    "0x",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException(
                    "contentTypeId must be a SharePoint content type ID starting with '0x'.");
            }
        }
    }

    [Description(
        "Confirm setting the default content type of a SharePoint list or document library.")]
    public sealed class GraphSetDefaultListContentTypeInput
    {
        [JsonPropertyName("siteId")]
        [Required]
        public string? SiteId { get; set; }

        [JsonPropertyName("listId")]
        [Required]
        public string? ListId { get; set; }

        [JsonPropertyName("contentTypeId")]
        [Required]
        public string? ContentTypeId { get; set; }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(SiteId))
                throw new ValidationException("siteId is required.");

            if (string.IsNullOrWhiteSpace(ListId))
                throw new ValidationException("listId is required.");

            if (string.IsNullOrWhiteSpace(ContentTypeId) ||
                !ContentTypeId.StartsWith(
                    "0x",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException(
                    "contentTypeId must be a SharePoint content type ID starting with '0x'.");
            }
        }
    }

    [Description(
    "Confirm enabling or disabling content types on a SharePoint list or document library.")]
    public sealed class GraphSetListContentTypesEnabledInput
    {
        [JsonPropertyName("siteId")]
        [Required]
        public string? SiteId { get; set; }

        [JsonPropertyName("listId")]
        [Required]
        public string? ListId { get; set; }

        [JsonPropertyName("enabled")]
        public bool? Enabled { get; set; }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(SiteId))
            {
                throw new ValidationException(
                    "siteId is required.");
            }

            if (string.IsNullOrWhiteSpace(ListId))
            {
                throw new ValidationException(
                    "listId is required.");
            }

            if (Enabled is null)
            {
                throw new ValidationException(
                    "enabled is required.");
            }
        }
    }
}
