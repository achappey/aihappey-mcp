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
    "Add an existing SharePoint site content type to a list or document library.")]
    [McpServerTool(
    Title = "Add site content type to SharePoint list",
    Name = "graph_sharepoint_add_site_content_type_to_list",
    Destructive = true,
    Idempotent = true,
    OpenWorld = false)]
    public static async Task<CallToolResult?>
    GraphSharePoint_AddSiteContentTypeToList(
        RequestContext<CallToolRequestParams> requestContext,

        [Description(
            "Microsoft Graph site ID containing the list and site content type.")]
        string? siteId = null,

        [Description(
            "Microsoft Graph list ID of the target SharePoint list or document library.")]
        string? listId = null,

        [Description(
            "ID of the content type already available on the SharePoint site.")]
        string? contentTypeId = null,

        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent<object>(async () =>
        {
            var (typed, notAccepted, _) =
                await requestContext.Server.TryElicit(
                    new GraphAddSiteContentTypeToListInput
                    {
                        SiteId = siteId,
                        ListId = listId,
                        ContentTypeId = contentTypeId
                    },
                    cancellationToken);

            if (notAccepted is not null)
            {
                throw new Exception(
                    JsonSerializer.Serialize(notAccepted));
            }

            typed!.Validate();

            var siteContentTypes =
                await client
                    .Sites[typed.SiteId!]
                    .ContentTypes
                    .GetAsync(
                        requestConfiguration =>
                        {
                            requestConfiguration.QueryParameters.Select =
                            [
                                "id",
                                "name",
                                "group",
                                "description"
                            ];
                        },
                        cancellationToken);

            var siteContentType =
                siteContentTypes?.Value?
                    .FirstOrDefault(contentType =>
                        string.Equals(
                            contentType.Id,
                            typed.ContentTypeId,
                            StringComparison.OrdinalIgnoreCase));

            if (siteContentType is null)
            {
                throw new ValidationException(
                    $"Content type {typed.ContentTypeId} is not available on site {typed.SiteId}. " +
                    "Add it to the site first using graph_sharepoint_add_hub_content_type_to_site.");
            }

            var listContentTypes =
                await client
                    .Sites[typed.SiteId!]
                    .Lists[typed.ListId!]
                    .ContentTypes
                    .GetAsync(
                        requestConfiguration =>
                        {
                            requestConfiguration.QueryParameters.Select =
                            [
                                "id",
                                "name",
                                "group"
                            ];
                        },
                        cancellationToken);

            var existingListContentType =
                listContentTypes?.Value?
                    .FirstOrDefault(contentType =>
                        string.Equals(
                            contentType.Id,
                            typed.ContentTypeId,
                            StringComparison.OrdinalIgnoreCase));

            if (existingListContentType is not null)
            {
                return new
                {
                    typed.SiteId,
                    typed.ListId,
                    typed.ContentTypeId,
                    existingListContentType.Name,
                    existingListContentType.Group,
                    Added = false,
                    AlreadyAvailable = true,
                    Status =
                        "The content type is already available on the target list."
                };
            }

            var canonicalContentTypeUrl =
                $"https://graph.microsoft.com/beta/sites/" +
                $"{typed.SiteId}/contentTypes/" +
                $"{typed.ContentTypeId}";

            var requestBody =
                new Microsoft.Graph.Beta.Sites.Item.Lists.Item.ContentTypes.AddCopy.AddCopyPostRequestBody
                {
                    ContentType =
                        canonicalContentTypeUrl
                };

            var addedContentType =
                await client
                    .Sites[typed.SiteId!]
                    .Lists[typed.ListId!]
                    .ContentTypes
                    .AddCopy
                    .PostAsync(
                        requestBody,
                        cancellationToken: cancellationToken);

            if (addedContentType is null)
            {
                throw new InvalidOperationException(
                    "Microsoft Graph returned no content type after adding it to the list.");
            }

            return new
            {
                typed.SiteId,
                typed.ListId,

                RequestedContentTypeId =
                    typed.ContentTypeId,

                ContentTypeId =
                    addedContentType.Id,

                addedContentType.Name,
                addedContentType.Group,
                addedContentType.Description,

                CanonicalContentTypeUrl =
                    canonicalContentTypeUrl,

                Added = true,
                AlreadyAvailable = false,

                Status =
                    "Added the site content type to the target list successfully."
            };
        })));

    [Description(
    "Add or synchronize a published content type from the SharePoint Content Type Hub to a target SharePoint site.")]
    [McpServerTool(
    Title = "Add hub content type to SharePoint site",
    Name = "graph_sharepoint_add_hub_content_type_to_site",
    Destructive = true,
    Idempotent = true,
    OpenWorld = false)]
    public static async Task<CallToolResult?>
    GraphSharePoint_AddHubContentTypeToSite(
        RequestContext<CallToolRequestParams> requestContext,

        [Description(
            "Microsoft Graph site ID of the target SharePoint site.")]
        string? siteId = null,

        [Description(
            "Content type ID from the SharePoint Content Type Hub, e.g. 0x010100...")]
        string? contentTypeId = null,

        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent<object>(async () =>
        {
            var (typed, notAccepted, _) =
                await requestContext.Server.TryElicit(
                    new GraphAddHubContentTypeToSiteInput
                    {
                        SiteId = siteId,
                        ContentTypeId = contentTypeId
                    },
                    cancellationToken);

            if (notAccepted is not null)
            {
                throw new Exception(
                    JsonSerializer.Serialize(notAccepted));
            }

            typed!.Validate();

            var existingContentTypes =
                await client
                    .Sites[typed.SiteId!]
                    .ContentTypes
                    .GetAsync(
                        requestConfiguration =>
                        {
                            requestConfiguration.QueryParameters.Select =
                            [
                                "id",
                                "name",
                                "group",
                                "description"
                            ];
                        },
                        cancellationToken);

            var existingContentType =
                existingContentTypes?.Value?
                    .FirstOrDefault(contentType =>
                        string.Equals(
                            contentType.Id,
                            typed.ContentTypeId,
                            StringComparison.OrdinalIgnoreCase));

            if (existingContentType is not null)
            {
                throw new Exception("The content type is already available on the target site.");
            }

            var requestBody =
                new Microsoft.Graph.Beta.Sites.Item.ContentTypes.AddCopyFromContentTypeHub.AddCopyFromContentTypeHubPostRequestBody
                {
                    ContentTypeId = typed.ContentTypeId
                };

            return
                await client
                    .Sites[typed.SiteId!]
                    .ContentTypes
                    .AddCopyFromContentTypeHub
                    .PostAsync(
                        requestBody,
                        cancellationToken: cancellationToken);

        })));

    [Description(
        "Confirm adding a published Content Type Hub content type to a SharePoint site.")]
    public sealed class GraphAddHubContentTypeToSiteInput
    {
        [JsonPropertyName("siteId")]
        [Required]
        public string? SiteId { get; set; }

        [JsonPropertyName("contentTypeId")]
        [Required]
        public string? ContentTypeId { get; set; }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(SiteId))
            {
                throw new ValidationException(
                    "siteId is required.");
            }

            if (string.IsNullOrWhiteSpace(ContentTypeId))
            {
                throw new ValidationException(
                    "contentTypeId is required.");
            }

            if (!ContentTypeId.StartsWith(
                    "0x",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException(
                    "contentTypeId must be a valid SharePoint content type ID starting with '0x'.");
            }
        }
    }

    [Description(
    "Confirm adding an existing site content type to a SharePoint list or document library.")]
    public sealed class GraphAddSiteContentTypeToListInput
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
            {
                throw new ValidationException(
                    "siteId is required.");
            }

            if (string.IsNullOrWhiteSpace(ListId))
            {
                throw new ValidationException(
                    "listId is required.");
            }

            if (string.IsNullOrWhiteSpace(ContentTypeId))
            {
                throw new ValidationException(
                    "contentTypeId is required.");
            }

            if (!ContentTypeId.StartsWith(
                    "0x",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException(
                    "contentTypeId must be a valid SharePoint content type ID starting with '0x'.");
            }
        }
    }
}
