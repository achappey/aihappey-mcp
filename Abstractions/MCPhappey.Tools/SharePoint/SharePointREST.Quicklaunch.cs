using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Auth.Extensions;
using MCPhappey.Auth.Models;
using MCPhappey.Common;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.SharePoint;

public static partial class SharePointREST
{
    [Description(
    "Add a link to the Quick Launch navigation of a SharePoint site. " +
    "The link can optionally be positioned after an existing link. " +
    "If a link with the same URL already exists, no duplicate is created.")]
    [McpServerTool(
    Title = "Add SharePoint Quick Launch link",
    Name = "sharepoint_add_quick_launch_link",
    Destructive = true,
    Idempotent = true,
    OpenWorld = false)]
    public static async Task<CallToolResult?>
    SharePointRest_AddQuickLaunchLink(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,

        [Description(
            "Exact SharePoint web URL, e.g. " +
            "https://contoso.sharepoint.com/sites/project.")]
        string? siteUrl = null,

        [Description(
            "Display title of the new Quick Launch link.")]
        string? name = null,

        [Description(
            "URL of the new Quick Launch link. " +
            "May be an absolute URL or a server-relative SharePoint URL.")]
        string? link = null,

        [Description(
            "Optional exact title of an existing Quick Launch link. " +
            "The new link will be placed immediately after this link. " +
            "Leave empty to add the link at the end.")]
        string? previousLinkName = null,

        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent<object>(async () =>
        {
            var (typed, notAccepted, _) =
                await requestContext.Server.TryElicit(
                    new SharePointAddQuickLaunchLinkInput
                    {
                        SiteUrl = siteUrl,
                        Name = name,
                        Link = link,
                        PreviousLinkName = previousLinkName
                    },
                    cancellationToken);

            if (notAccepted is not null)
            {
                throw new Exception(
                    JsonSerializer.Serialize(notAccepted));
            }

            typed!.Validate();

            var tokenService =
                serviceProvider.GetRequiredService<HeaderProvider>();

            if (string.IsNullOrWhiteSpace(tokenService.Bearer))
            {
                throw new UnauthorizedAccessException(
                    "Missing bearer token.");
            }

            var httpClientFactory =
                serviceProvider.GetRequiredService<IHttpClientFactory>();

            var oauthSettings =
                serviceProvider.GetRequiredService<OAuthSettings>();

            var serverConfig =
                serviceProvider.GetServerConfig(
                    requestContext.Server);

            var siteUri = new Uri(typed.SiteUrl!);

            using var client =
                await httpClientFactory.GetOboHttpClient(
                    tokenService.Bearer,
                    siteUri.Host,
                    serverConfig!.Server,
                    oauthSettings);

            var existingNodes =
                await GetQuickLaunchNodesAsync(
                    client,
                    typed.SiteUrl!,
                    cancellationToken);

            var existingLink =
                existingNodes.FirstOrDefault(node =>
                    UrlsAreEquivalent(
                        typed.SiteUrl!,
                        node.Url,
                        typed.Link!));

            if (existingLink is not null)
            {
                return new
                {
                    typed.SiteUrl,
                    RequestedName = typed.Name,
                    RequestedLink = typed.Link,
                    ExistingNodeId = existingLink.Id,
                    ExistingNodeTitle = existingLink.Title,
                    ExistingNodeUrl = existingLink.Url,
                    Added = false,
                    Moved = false,
                    Status =
                        "A Quick Launch link with the same URL already exists."
                };
            }

            SharePointNavigationNode? previousNode = null;

            if (!string.IsNullOrWhiteSpace(
                    typed.PreviousLinkName))
            {
                previousNode = existingNodes.FirstOrDefault(node =>
                    string.Equals(
                        node.Title,
                        typed.PreviousLinkName.Trim(),
                        StringComparison.OrdinalIgnoreCase));

                if (previousNode is null)
                {
                    throw new ValidationException(
                        $"No Quick Launch link named " +
                        $"'{typed.PreviousLinkName}' was found.");
                }
            }

            var createdNode =
                await AddQuickLaunchNodeAsync(
                    client,
                    typed.SiteUrl!,
                    typed.Name!,
                    typed.Link!,
                    cancellationToken);

            var moved = false;

            if (previousNode is not null)
            {
                await MoveQuickLaunchNodeAfterAsync(
                    client,
                    typed.SiteUrl!,
                    createdNode.Id,
                    previousNode.Id,
                    cancellationToken);

                moved = true;
            }

            return new
            {
                typed.SiteUrl,
                NodeId = createdNode.Id,
                Title = createdNode.Title,
                Url = createdNode.Url,
                IsExternal = createdNode.IsExternal,
                PreviousLinkName = typed.PreviousLinkName,
                PreviousNodeId = previousNode?.Id,
                Added = true,
                Moved = moved,
                Status = moved
                    ? "Added the Quick Launch link and positioned it after the requested link."
                    : "Added the Quick Launch link at the end of the navigation."
            };
        }));

    private static async Task MoveQuickLaunchNodeAfterAsync(
        HttpClient client,
        string siteUrl,
        int nodeId,
        int previousNodeId,
        CancellationToken cancellationToken)
    {
        var url =
            $"{siteUrl.TrimEnd('/')}" +
            "/_api/web/navigation/quicklaunch" +
            $"/MoveAfter({nodeId},{previousNodeId})";

        using var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                url);

        request.Headers.Accept.ParseAdd(
            "application/json;odata=nometadata");

        request.Content = new ByteArrayContent([]);

        using var response = await client.SendAsync(
            request,
            cancellationToken);

        var content = await response.Content.ReadAsStringAsync(
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"The Quick Launch link was created, but could not be moved " +
                $"after navigation node {previousNodeId} " +
                $"({(int)response.StatusCode} {response.ReasonPhrase}). " +
                $"Created node ID: {nodeId}. Response: {content}");
        }
    }

    private static async Task<List<SharePointNavigationNode>>
        GetQuickLaunchNodesAsync(
            HttpClient client,
            string siteUrl,
            CancellationToken cancellationToken)
    {
        var url =
            $"{siteUrl.TrimEnd('/')}" +
            "/_api/web/navigation/quicklaunch" +
            "?$select=Id,Title,Url,IsExternal";

        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                url);

        request.Headers.Accept.ParseAdd(
            "application/json;odata=nometadata");

        using var response = await client.SendAsync(
            request,
            cancellationToken);

        var content = await response.Content.ReadAsStringAsync(
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Could not retrieve SharePoint Quick Launch links " +
                $"({(int)response.StatusCode} {response.ReasonPhrase}). " +
                $"Response: {content}");
        }

        using var document = JsonDocument.Parse(content);

        var root = UnwrapSharePointResponse(
            document.RootElement);

        JsonElement values;

        if (root.ValueKind == JsonValueKind.Array)
        {
            values = root;
        }
        else if (
            root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("value", out var value) &&
            value.ValueKind == JsonValueKind.Array)
        {
            values = value;
        }
        else if (
            root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("results", out var results) &&
            results.ValueKind == JsonValueKind.Array)
        {
            values = results;
        }
        else
        {
            return [];
        }

        var nodes = new List<SharePointNavigationNode>();

        foreach (var item in values.EnumerateArray())
        {
            if (!TryGetInt32Property(
                    item,
                    "Id",
                    out var id))
            {
                continue;
            }

            nodes.Add(
                new SharePointNavigationNode(
                    id,
                    GetOptionalStringProperty(item, "Title")
                        ?? string.Empty,
                    GetOptionalStringProperty(item, "Url")
                        ?? string.Empty,
                    item.TryGetProperty(
                        "IsExternal",
                        out var isExternalElement) &&
                    isExternalElement.ValueKind is
                        JsonValueKind.True or
                        JsonValueKind.False &&
                    isExternalElement.GetBoolean()));
        }

        return nodes;
    }

    private static async Task<SharePointNavigationNode>
        AddQuickLaunchNodeAsync(
            HttpClient client,
            string siteUrl,
            string title,
            string link,
            CancellationToken cancellationToken)
    {
        var isExternal =
            IsExternalNavigationUrl(
                siteUrl,
                link);

        var url =
            $"{siteUrl.TrimEnd('/')}" +
            "/_api/web/navigation/quicklaunch";

        using var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                url);

        request.Headers.Accept.ParseAdd(
            "application/json;odata=nometadata");

        request.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                Title = title.Trim(),
                Url = link.Trim(),
                IsExternal = isExternal
            }),
            System.Text.Encoding.UTF8,
            "application/json");

        using var response = await client.SendAsync(
            request,
            cancellationToken);

        var content = await response.Content.ReadAsStringAsync(
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Could not add the SharePoint Quick Launch link " +
                $"({(int)response.StatusCode} {response.ReasonPhrase}). " +
                $"Title: {title}. Link: {link}. Response: {content}");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException(
                "SharePoint created the Quick Launch link " +
                "but returned no navigation node.");
        }

        using var document = JsonDocument.Parse(content);

        var root = UnwrapSharePointResponse(
            document.RootElement);

        if (!TryGetInt32Property(
                root,
                "Id",
                out var nodeId))
        {
            throw new InvalidOperationException(
                "SharePoint returned no ID for the new Quick Launch link.");
        }

        return new SharePointNavigationNode(
            nodeId,
            GetOptionalStringProperty(root, "Title")
                ?? title.Trim(),
            GetOptionalStringProperty(root, "Url")
                ?? link.Trim(),
            root.TryGetProperty(
                "IsExternal",
                out var externalElement) &&
            externalElement.ValueKind is
                JsonValueKind.True or
                JsonValueKind.False
                ? externalElement.GetBoolean()
                : isExternal);
    }

    private sealed record SharePointNavigationNode(
        int Id,
        string Title,
        string Url,
        bool IsExternal);

    [Description(
    "Confirm adding a link to the Quick Launch navigation of a SharePoint site.")]
    public sealed class SharePointAddQuickLaunchLinkInput
    {
        [JsonPropertyName("siteUrl")]
        [Required]
        public string? SiteUrl { get; set; }

        [JsonPropertyName("name")]
        [Required]
        public string? Name { get; set; }

        [JsonPropertyName("link")]
        [Required]
        public string? Link { get; set; }

        [JsonPropertyName("previousLinkName")]
        public string? PreviousLinkName { get; set; }

        public void Validate()
        {
            if (!Uri.TryCreate(
                    SiteUrl,
                    UriKind.Absolute,
                    out var siteUri) ||
                siteUri.Scheme != Uri.UriSchemeHttps ||
                !siteUri.Host.EndsWith(
                    ".sharepoint.com",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException(
                    "siteUrl must be an absolute SharePoint Online HTTPS URL.");
            }

            if (string.IsNullOrWhiteSpace(Name))
            {
                throw new ValidationException(
                    "name is required.");
            }

            if (string.IsNullOrWhiteSpace(Link))
            {
                throw new ValidationException(
                    "link is required.");
            }

            if (!Uri.TryCreate(
                    Link,
                    UriKind.Absolute,
                    out _) &&
                !Uri.TryCreate(
                    Link,
                    UriKind.Relative,
                    out _))
            {
                throw new ValidationException(
                    "link must be an absolute or relative URL.");
            }

            if (Name.Length > 255)
            {
                throw new ValidationException(
                    "name may not exceed 255 characters.");
            }
        }
    }
    private static bool IsExternalNavigationUrl(
        string siteUrl,
        string link)
    {
        if (!Uri.TryCreate(
                link,
                UriKind.Absolute,
                out var linkUri))
        {
            // Server-relative of web-relative URL.
            return false;
        }

        var siteUri = new Uri(siteUrl);

        return !string.Equals(
            siteUri.Host,
            linkUri.Host,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool UrlsAreEquivalent(
        string siteUrl,
        string existingUrl,
        string requestedUrl)
    {
        var existing =
            NormalizeNavigationUrl(
                siteUrl,
                existingUrl);

        var requested =
            NormalizeNavigationUrl(
                siteUrl,
                requestedUrl);

        return string.Equals(
            existing,
            requested,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeNavigationUrl(
        string siteUrl,
        string value)
    {
        var trimmed = value.Trim();

        if (Uri.TryCreate(
                trimmed,
                UriKind.Absolute,
                out var absoluteUri))
        {
            return absoluteUri
                .GetComponents(
                    UriComponents.SchemeAndServer |
                    UriComponents.PathAndQuery,
                    UriFormat.Unescaped)
                .TrimEnd('/');
        }

        var siteUri = new Uri(
            siteUrl.TrimEnd('/') + "/");

        var resolvedUri = trimmed.StartsWith('/')
            ? new Uri(
                siteUri.GetLeftPart(UriPartial.Authority) +
                trimmed)
            : new Uri(siteUri, trimmed);

        return resolvedUri
            .GetComponents(
                UriComponents.SchemeAndServer |
                UriComponents.PathAndQuery,
                UriFormat.Unescaped)
            .TrimEnd('/');
    }

}