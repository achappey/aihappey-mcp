using System.Net;
using System.Net.Mime;
using System.Text.Json;
using MCPhappey.Auth.Extensions;
using MCPhappey.Auth.Models;
using MCPhappey.Common;
using MCPhappey.Common.Extensions;
using MCPhappey.Common.Models;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace MCPhappey.Scrapers.Microsoft;

public class SharePointRESTScraper(IHttpClientFactory httpClientFactory, ServerConfig serverConfig,
    OAuthSettings oAuthSettings) : IContentScraper
{
    public bool SupportsHost(ServerConfig currentConfig, string url)
        => new Uri(url).Host.EndsWith(".sharepoint.com", StringComparison.OrdinalIgnoreCase)
            && serverConfig.Server.OBO?.Values.Any(a => a.EndsWith(".sharepoint.com/.default")) == true
            && url.Contains("/_api/");

    public async Task<IEnumerable<FileItem>?> GetContentAsync(
           McpServer mcpServer,
           IServiceProvider serviceProvider,
           string url,
           CancellationToken cancellationToken = default)
    {
        var tokenService = serviceProvider.GetService<HeaderProvider>();
        if (string.IsNullOrEmpty(tokenService?.Bearer))
        {
            return null;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }


        using var client = await httpClientFactory.GetOboHttpClient(
            tokenService.Bearer,
            uri.Host,
            serverConfig.Server,
            oAuthSettings);

        var lower = url.ToLowerInvariant();

        // 1) DIRECT FILE DOWNLOADS (attachments or library files)
        //
        // Examples:
        // - .../AttachmentFiles('file.eml')/$value
        // - .../GetFileByServerRelativeUrl('/sites/.../file.docx')/$value
        // - .../GetFileById(guid'...')/$value
        //
        // Rule: end with /$value OR obvious file content endpoint.
        var isDirectValue =
            lower.EndsWith("/$value") ||
            lower.Contains("/attachmentfiles(") ||
            lower.Contains("/getfilebyserverrelativeurl(") ||
            lower.Contains("/getfilebyid(");

        if (isDirectValue)
        {
            var response = await client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var contents = await BinaryData.FromStreamAsync(stream, cancellationToken);

            // Derive filename from URL
            var fileName = GetFileNameFromUrl(uri);
            var extension = Path.GetExtension(fileName);

            // Use your default extension → mimetype converter
            var mimeType = !string.IsNullOrWhiteSpace(extension)
                ? (extension.ResolveMimeFromExtension()
                   ?? response.Content.Headers.ContentType?.MediaType
                   ?? MediaTypeNames.Application.Octet)
                : (response.Content.Headers.ContentType?.MediaType
                   ?? MediaTypeNames.Application.Octet);

            return new[]
            {
                new FileItem
                {
                    Contents = contents,
                    MimeType = mimeType,
                    Uri = url,
                    Filename = fileName
                }
            };
        }

        // 2) LIST ITEM WITH ATTACHMENTS (expand=AttachmentFiles) OR FILE METADATA
        //
        // Example response (simplified):
        // {
        //   "@odata.context": "...",
        //   "AttachmentFiles": [
        //      {
        //        "FileName": "something.eml",
        //        "ServerRelativeUrl": "/sites/DAS/Lists/Aanvragen/Attachments/55910/something.eml"
        //      }
        //   ],
        //   "File": {
        //      "ServerRelativeUrl": "/sites/DAS/Shared Documents/foo.docx"
        //   },
        //   ...
        // }
        //
        // Here we:
        //  - ignore the JSON itself
        //  - follow AttachmentFiles / File
        //  - download each file with correct mimetype.

        var looksLikeAttachmentsMeta =
            lower.Contains("attachmentfiles") ||
            lower.Contains("/items(") && lower.Contains("$expand=attachmentfiles");

        var looksLikeFileMeta =
            lower.EndsWith("/file") ||
            lower.Contains("/file?");

        if (looksLikeAttachmentsMeta || looksLikeFileMeta)
        {
            var response = await client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var results = new List<FileItem>();

            // 2a) Attachments: AttachmentFiles[]
            if (root.TryGetProperty("AttachmentFiles", out var attachmentsElement) &&
                attachmentsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var attachment in attachmentsElement.EnumerateArray())
                {
                    var serverRelativeUrl =
                        attachment.TryGetProperty("ServerRelativeUrl", out var srvProp)
                            ? srvProp.GetString()
                            : null;

                    var fileName =
                        attachment.TryGetProperty("FileName", out var nameProp)
                            ? nameProp.GetString()
                            : null;

                    if (string.IsNullOrEmpty(serverRelativeUrl))
                        continue;

                    var absoluteUrl = BuildAbsoluteUrl(uri, serverRelativeUrl);
                    var attachmentFileName = fileName ?? Path.GetFileName(serverRelativeUrl);

                    var extension = Path.GetExtension(attachmentFileName);
                    var mimeType = !string.IsNullOrWhiteSpace(extension)
                        ? (extension.ResolveMimeFromExtension() ?? MediaTypeNames.Application.Octet)
                        : MediaTypeNames.Application.Octet;

                    using var attachmentResponse = await client.GetAsync(absoluteUrl, cancellationToken);
                    attachmentResponse.EnsureSuccessStatusCode();

                    await using var attachmentStream =
                        await attachmentResponse.Content.ReadAsStreamAsync(cancellationToken);

                    var data = await BinaryData.FromStreamAsync(attachmentStream, cancellationToken);

                    results.Add(new FileItem
                    {
                        Contents = data,
                        MimeType = mimeType,
                        Uri = absoluteUrl
                    });
                }
            }

            // 2b) Associated file in a document library: "File": { ServerRelativeUrl: ... }
            if (root.TryGetProperty("File", out var fileElement) &&
                fileElement.ValueKind == JsonValueKind.Object &&
                fileElement.TryGetProperty("ServerRelativeUrl", out var fileSrvProp))
            {
                var serverRelativeUrl = fileSrvProp.GetString();
                if (!string.IsNullOrEmpty(serverRelativeUrl))
                {
                    var absoluteUrl = BuildAbsoluteUrl(uri, serverRelativeUrl);
                    var fileName = Path.GetFileName(serverRelativeUrl);
                    var extension = Path.GetExtension(fileName);

                    var mimeType = !string.IsNullOrWhiteSpace(extension)
                        ? (extension.ResolveMimeFromExtension() ?? MediaTypeNames.Application.Octet)
                        : MediaTypeNames.Application.Octet;

                    using var fileResponse = await client.GetAsync(absoluteUrl, cancellationToken);
                    fileResponse.EnsureSuccessStatusCode();

                    await using var fileStream =
                        await fileResponse.Content.ReadAsStreamAsync(cancellationToken);

                    var data = await BinaryData.FromStreamAsync(fileStream, cancellationToken);

                    results.Add(new FileItem
                    {
                        Contents = data,
                        MimeType = mimeType,
                        Uri = absoluteUrl,
                        Filename = fileName
                    });
                }
            }

            // If we found attachments/files, return ONLY those (no JSON)
            if (results.Count > 0)
            {
                return results;
            }

            // No attachments/file metadata found → not our business
            //  return null;
        }

        var content = await client.GetAsync(url, cancellationToken);

        return [await content.ToFileItem(url, cancellationToken)];
    }
    
    private static string GetFileNameFromUrl(Uri uri)
    {
        if (uri == null)
            return string.Empty;

        var segments = uri.Segments;
        if (segments.Length == 0)
            return string.Empty;

        var last = segments[^1].TrimEnd('/');

        // If last is "$value", take previous
        if (string.Equals(last, "$value", StringComparison.OrdinalIgnoreCase) && segments.Length >= 2)
        {
            last = segments[^2].TrimEnd('/');
        }

        var decoded = WebUtility.UrlDecode(last);

        // Strip query part if present (for safety)
        var queryIndex = decoded.IndexOf('?');
        if (queryIndex >= 0)
            decoded = decoded[..queryIndex];

        // Handle formats like AttachmentFiles('file.eml')
        if (decoded.Contains('(') && decoded.Contains('\'') && decoded.EndsWith(')'))
        {
            var firstQuote = decoded.IndexOf('\'');
            var lastQuote = decoded.LastIndexOf('\'');

            if (firstQuote >= 0 && lastQuote > firstQuote)
            {
                decoded = decoded.Substring(firstQuote + 1, lastQuote - firstQuote - 1);
            }
        }

        // Clean leftover closing parenthesis or quotes (e.g. ".eml')")
        decoded = decoded.Trim('\'', ')', '"', ' ');

        // In some SharePoint URLs, the file is nested in a path: /sites/.../Attachments/123/file.eml
        // If still contains a slash, only take the last segment
        if (decoded.Contains('/'))
            decoded = Path.GetFileName(decoded);

        return decoded;
    }

    private static string BuildAbsoluteUrl(Uri baseUri, string serverRelativeUrl)
    {
        var authority = baseUri.GetLeftPart(UriPartial.Authority);
        return new Uri(new Uri(authority), serverRelativeUrl).ToString();
    }
}