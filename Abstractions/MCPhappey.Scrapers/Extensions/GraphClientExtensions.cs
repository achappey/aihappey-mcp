using MCPhappey.Common.Models;
using Microsoft.Graph.Beta;
using MCPhappey.Auth.Models;
using MCPhappey.Auth.Extensions;
using MCPhappey.Common.Constants;
using MGraph = Microsoft.Graph.Beta;
using System.Net.Mime;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using MCPhappey.Common.Extensions;
using System.Web;
using Microsoft.Graph.Beta.Models;

namespace MCPhappey.Scrapers.Extensions;

public static class GraphClientExtensions
{

    public static async Task<string> ToGraphRestIdAsync(
         this GraphServiceClient graph,
         string owaItemId,
         string? mailbox,
         CancellationToken ct)
    {
        // Ensure not still URL-encoded (e.g. %3D)
        var clean = Uri.UnescapeDataString(owaItemId);

        object? response;

        if (!string.IsNullOrEmpty(mailbox))
        {
            var builder = new MGraph.Users.Item.TranslateExchangeIds.TranslateExchangeIdsRequestBuilder(
                $"https://graph.microsoft.com/beta/users/{Uri.EscapeDataString(mailbox)}/translateExchangeIds",
                graph.RequestAdapter);

            response = await builder.PostAsTranslateExchangeIdsPostResponseAsync(
                new MGraph.Users.Item.TranslateExchangeIds.TranslateExchangeIdsPostRequestBody
                {
                    InputIds = [clean],
                    SourceIdType = ExchangeIdFormat.EwsId,
                    TargetIdType = ExchangeIdFormat.RestId
                },
                cancellationToken: ct);
        }
        else
        {
            var builder = new MGraph.Me.TranslateExchangeIds.TranslateExchangeIdsRequestBuilder(
                $"https://graph.microsoft.com/beta/me/translateExchangeIds",
                graph.RequestAdapter);

            response = await builder.PostAsTranslateExchangeIdsPostResponseAsync(
                new MGraph.Me.TranslateExchangeIds.TranslateExchangeIdsPostRequestBody
                {
                    InputIds = [clean],
                    SourceIdType = ExchangeIdFormat.EwsId,
                    TargetIdType = ExchangeIdFormat.RestId
                },
                cancellationToken: ct);
        }

        // Extract Value[0].Id via reflection (types differ between /me and /users)
        string? restId = null;
        if (response?.GetType().GetProperty("Value")?.GetValue(response) is System.Collections.IEnumerable valueObj)
        {
            var enumerator = valueObj.GetEnumerator();
            if (enumerator.MoveNext())
            {
                var first = enumerator.Current;
                restId = first?.GetType().GetProperty("TargetId")?.GetValue(first)?.ToString();
            }
        }

        if (string.IsNullOrEmpty(restId))
            throw new InvalidOperationException("Could not translate OWA ItemID to Graph REST id.");

        return restId;
    }

    public static async Task<GraphServiceClient> GetOboGraphClient(this IHttpClientFactory httpClientFactory,
        string token,
        Server server,
        OAuthSettings oAuthSettings)
    {
        var delegated = await httpClientFactory.GetOboToken(token, Hosts.MicrosoftGraph, server, oAuthSettings);

        var authProvider = new StaticTokenAuthProvider(delegated!);
        return new GraphServiceClient(authProvider);
    }

    public static async Task<FileItem> GetFilesByUrl(this GraphServiceClient graphServiceClient,
        string url)
    {
        var result = await graphServiceClient.GetDriveItem(url);

        if (result?.Folder != null)
        {
            return await graphServiceClient.GetFilesByFolder(result);
        }
        else
        {
            var content = await graphServiceClient.GetDriveItemContentAsync(result?.ParentReference?.DriveId!, result?.Id!);

            if (content != null)
            {
                return content;
            }
        }

        throw new Exception("Something went wrong. Only valid shareId or sharing URL are valid.");
    }

    public static FileItem? ToFileItem(this SitePage sitePage)
    {
        var allInnerHtml = sitePage?.CanvasLayout?.HorizontalSections?
                  .SelectMany(hs => hs.Columns ?? [])
                  .SelectMany(c => c.Webparts ?? [])
                  .OfType<TextWebPart>()
                  .Select(wp => wp.InnerHtml)
                  .ToList();

        if (sitePage?.CanvasLayout?.VerticalSection != null)
        {
            allInnerHtml?.AddRange(sitePage?.CanvasLayout?.VerticalSection?.Webparts?
                .OfType<TextWebPart>()
                .Select(wp => wp.InnerHtml) ?? []);
        }

        var html = string.Join("", allInnerHtml?.Where(y => !string.IsNullOrEmpty(y)) ?? []);

        if (string.IsNullOrEmpty(html)) return null;

        var htmlString = @$"<html><head><meta name='author' content='{sitePage?.CreatedBy?.User?.DisplayName}'>
          <meta name='creation-date' content='{sitePage?.CreatedDateTime}'>
          <meta name='source-url' content='{sitePage?.WebUrl}'>
          <title>{sitePage?.Title}</title>
          </head>
          <body>
          <div>{html}</div>
          </body>
          </html>";

        return new()
        {
            Contents = BinaryData.FromString(htmlString),
            MimeType = MediaTypeNames.Text.Html,
            Filename = Path.ChangeExtension(sitePage?.Name, ".html"),
            Uri = sitePage?.WebUrl ?? string.Empty,
        };
    }


    public static async Task<SitePage?> GetSharePointPage(this GraphServiceClient client, string url)
    {
        var (Hostname, Path, PageName) = url.ExtractSharePointValues();

        var site = await client.Sites[$"{Hostname}:/sites/{Path}"].GetAsync();
        var pages = await client.Sites[site?.Id].Pages.GraphSitePage.GetAsync((config) =>
        {
            config.QueryParameters.Select = ["name", "id", "title", "webUrl", "createdDateTime", "createdBy"];
            config.QueryParameters.Top = 999;
        });

        var page = pages?.Value?.FirstOrDefault(t => t.Name == PageName);
        var sitePage = await client.Sites[site?.Id].Pages[page?.Id].GraphSitePage.GetAsync((config) =>
        {
            config.QueryParameters.Expand = ["canvasLayout"];
        });

        return sitePage;
    }

    public static (string Hostname, string Path, string PageName) ExtractSharePointValues(this string sharePointUrl)
    {
        // Extracting the hostname, site path, and page name from the given URL
        var uri = new Uri(sharePointUrl);
        string hostname = uri.Host;
        string[] pathSegments = uri.AbsolutePath.Split(['/'], StringSplitOptions.RemoveEmptyEntries);

        // Assuming the path segment after "sites" is the required path
        int siteIndex = Array.IndexOf(pathSegments, "sites");
        string path = siteIndex >= 0 && pathSegments.Length > siteIndex + 1 ? pathSegments[siteIndex + 1] : string.Empty;

        // Assuming the page name is the last segment in the URL
        string pageName = pathSegments.Length > 0 ? pathSegments[pathSegments.Length - 1] : string.Empty;

        return (Hostname: hostname, Path: path, PageName: pageName);
    }

    public static Task<DriveItem?> GetDriveItem(this GraphServiceClient client, string link,
           CancellationToken cancellationToken = default)
    {
        string base64Value = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(link));
        string encodedUrl = "u!" + base64Value.TrimEnd('=').Replace('/', '_').Replace('+', '-');

        return client.Shares[encodedUrl].DriveItem.GetAsync(cancellationToken: cancellationToken);
    }

    public static Resource ToResource(this DriveItem driveItem) =>
           new()
           {
               Name = driveItem?.Name!,
               Uri = driveItem?.WebUrl!,
               Size = driveItem?.Size,
               Description = driveItem?.Description,
               Annotations = new Annotations()
               {
                   LastModified = driveItem?.LastModifiedDateTime
               },
               MimeType = driveItem?.File?.MimeType ?? (driveItem?.Folder != null
                    ? MediaTypeNames.Application.Json : driveItem?.File?.MimeType)
           };

    private static async Task<FileItem> GetFilesByFolder(this GraphServiceClient graphServiceClient,
       DriveItem driveItem)
    {
        if (driveItem?.Folder != null)
        {
            var items = await graphServiceClient.Drives[driveItem?.ParentReference?.DriveId!].Items[driveItem?.Id!].Children.GetAsync();

            return JsonSerializer
                            .Serialize(items?.Value?.Select(t => t.ToResource()), ResourceExtensions.JsonSerializerOptions)
                            .ToJsonFileItem(driveItem?.WebUrl!);
        }

        throw new Exception("Only folders are supported");
    }

    public static async Task<FileItem> GetDriveItemContentAsync(this GraphServiceClient client, string driveId, string itemId)
    {
        var item = await client.Drives[driveId].Items[itemId].GetAsync();

        // Throw an exception if the item is not a file or ContentType is missing
        if (item?.File == null || item.File.MimeType == null)
            throw new InvalidOperationException("The item is not a file or MIME type unknown.");

        string contentType = item.File.MimeType;

        await using var stream = await client.Drives[driveId].Items[itemId].Content
            .GetAsync() ?? throw new InvalidOperationException("Stream cannot be null");

        var finalContentType = !string.IsNullOrEmpty(item.Name)
                      && Path
                      .GetExtension(item.Name)
                      .Equals(".csv", StringComparison.InvariantCultureIgnoreCase)
                        ? MediaTypeNames.Text.Csv : contentType;

        return new()
        {
            Contents = await BinaryData.FromStreamAsync(stream),
            Uri = item?.WebUrl!,
            Filename = item?.Name,
            MimeType = finalContentType,
        };

    }

    public static async Task<IEnumerable<FileItem>?> GetInputFileFromNewsPagesAsync(
        this GraphServiceClient graphClient,
        string fullUrl)
    {
        var uri = new Uri(fullUrl);
        var hostname = uri.Host;
        var queryParams = HttpUtility.ParseQueryString(uri.Query);

        var fullServerRelativeUrl = HttpUtility.UrlDecode(queryParams["serverRelativeUrl"]);
        var sitePath = fullServerRelativeUrl?.Split("/SitePages", StringSplitOptions.None)[0];
        var pagesListId = queryParams["pagesListId"];

        if (string.IsNullOrEmpty(sitePath) || string.IsNullOrEmpty(pagesListId))
            throw new ArgumentException("Missing site path or pages list ID.");

        // Get the site
        var site = await graphClient
            .Sites[$"{hostname}:{sitePath}"]
            .GetAsync();

        // Get the list items from Site Pages
        var listItems = await graphClient
            .Sites[site?.Id]
            .Lists[pagesListId]
            .Items
            .GetAsync((a) =>
            {
                a.QueryParameters.Expand = ["fields"];
                a.QueryParameters.Orderby = ["lastModifiedDateTime desc"];
            });

        // Extract title, description, webUrl from each item
        var pages = listItems?.Value?.Select(item =>
        {
            IDictionary<string, object> fields = item.Fields?.AdditionalData ?? new Dictionary<string, object>();

            fields.TryGetValue("Title", out var titleObj);
            fields.TryGetValue("Description", out var descObj);

            var title = titleObj?.ToString();
            var description = descObj?.ToString();

            return new
            {
                title,
                description,
                webUrl = item.WebUrl,
                lastModifiedDateTime = item.LastModifiedDateTime
            };
        });

        return [pages.ToFileItem(fullUrl, filename: "news.aspx")];
    }

}

