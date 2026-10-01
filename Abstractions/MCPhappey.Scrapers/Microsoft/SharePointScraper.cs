using MCPhappey.Auth.Models;
using MCPhappey.Common;
using MCPhappey.Common.Constants;
using MCPhappey.Common.Models;
using MCPhappey.Scrapers.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace MCPhappey.Scrapers.Microsoft;

public class SharePointScraper(IHttpClientFactory httpClientFactory, ServerConfig serverConfig,
    OAuthSettings oAuthSettings) : IContentScraper, IContentDirectoryScraper
{
    public bool SupportsDirectory(ServerConfig currentConfig, string url)
        => SupportsHost(currentConfig, url);

    public async Task<IReadOnlyDictionary<string, byte[]>> GetDirectoryAsync(McpServer? mcpServer,
        IServiceProvider serviceProvider, string url, CancellationToken cancellationToken)
    {
        var bearer = serviceProvider.GetService<HeaderProvider>()?.Bearer;
        if (string.IsNullOrEmpty(bearer))
            throw new UnauthorizedAccessException("SharePoint skills require delegated authorization.");

        using var graph = await httpClientFactory.GetOboGraphClient(bearer, serverConfig.Server, oAuthSettings);
        var root = await graph.GetDriveItem(url, cancellationToken);
        if (root?.Folder == null || root.ParentReference?.DriveId == null || root.Id == null)
            throw new InvalidOperationException("The skill source must be a SharePoint folder.");

        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        async Task Visit(global::Microsoft.Graph.Beta.Models.DriveItem folder, string prefix)
        {
            var driveId = root.ParentReference.DriveId;
            var page = await graph.Drives[driveId].Items[folder.Id].Children.GetAsync(cancellationToken: cancellationToken);
            while (page != null)
            {
                foreach (var item in page.Value ?? [])
                {
                    if (item.Name == null || item.Id == null || item.Name is "." or ".." ||
                        item.Name.Contains('/') || item.Name.Contains('\\'))
                        throw new InvalidOperationException("Invalid SharePoint skill file name.");
                    var path = prefix + item.Name;
                    if (item.Folder != null)
                        await Visit(item, path + "/");
                    else if (item.File != null)
                    {
                        if (files.Count >= 512 || item.Size > 16 * 1024 * 1024)
                            throw new InvalidOperationException("Skill exceeds the supported size limit.");
                        await using var stream = await graph.Drives[driveId].Items[item.Id].Content.GetAsync(cancellationToken: cancellationToken)
                            ?? throw new InvalidOperationException("Missing SharePoint file content.");
                        using var buffer = new MemoryStream();
                        await stream.CopyToAsync(buffer, cancellationToken);
                        if (buffer.Length > 16 * 1024 * 1024 || files.Values.Sum(b => (long)b.Length) + buffer.Length > 16 * 1024 * 1024 || !files.TryAdd(path, buffer.ToArray()))
                            throw new InvalidOperationException("Invalid or oversized SharePoint skill folder.");
                    }
                }
                if (page.OdataNextLink == null) break;
                page = await graph.Drives[driveId].Items[folder.Id].Children.WithUrl(page.OdataNextLink).GetAsync(cancellationToken: cancellationToken);
            }
        }
        await Visit(root, "");
        return files;
    }
    public bool SupportsHost(ServerConfig currentConfig, string url)
        => new Uri(url).Host.EndsWith(".sharepoint.com", StringComparison.OrdinalIgnoreCase)
            && serverConfig.Server.OBO?.ContainsKey(Hosts.MicrosoftGraph) == true
            && !url.Contains("/_api/");

    public async Task<IEnumerable<FileItem>?> GetContentAsync(McpServer mcpServer, IServiceProvider serviceProvider,
         string url, CancellationToken cancellationToken = default)
    {
        var tokenService = serviceProvider.GetService<HeaderProvider>();

        if (string.IsNullOrEmpty(tokenService?.Bearer))
        {
            return null;
        }

        using var graphClient = await httpClientFactory.GetOboGraphClient(tokenService.Bearer,
                serverConfig.Server, oAuthSettings);

        if (url.Contains("/_layouts/15/news.aspx?"))
        {
            var newsFile = await graphClient
                .GetInputFileFromNewsPagesAsync(url);

            return newsFile ?? [];
        }
        else
        {
            try
            {
                return [await graphClient.GetFilesByUrl(url)];
            }
            catch (Exception e)
            {
                if (e.Message == "Site Pages cannot be accessed as a drive item")
                {

                    var pageResult = await graphClient.GetSharePointPage(url);
                    if (pageResult != null)
                    {
                        var inputFile = pageResult?.ToFileItem();

                        return inputFile != null && !inputFile.Contents.IsEmpty
                            ? [inputFile] : [];
                    }

                    return [];
                }

                throw;

            }
        }
    }
}
