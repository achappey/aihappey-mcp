
using System.Text.RegularExpressions;
using MCPhappey.Core.Extensions;
using MCPhappey.Common.Models;
using Microsoft.KernelMemory.DataFormats.WebPages;
using MCPhappey.Common;

namespace MCPhappey.Core.Services;

public partial class DownloadService(WebScraper webScraper,
    TransformService transformService,
    IEnumerable<IContentScraper> scrapers,
    IEnumerable<IRequestContentDownloader>? requestDownloaders = null) : IWebScraper
{
    public async Task<WebScraperResult> GetContentAsync(string url, CancellationToken cancellationToken = default)
        => await webScraper.GetContentAsync(url, cancellationToken);

    public async Task<IEnumerable<FileItem>> ScrapeContentAsync(IServiceProvider serviceProvider,
        ModelContextProtocol.Server.McpServer mcpServer,
        string url,
        CancellationToken cancellationToken = default)
    {
        Uri uri = new(url);
        var serverConfig = serviceProvider.GetServerConfig(mcpServer)
            ?? throw new Exception();

        var requestContent = await DownloadForRequestAsync(serviceProvider, serverConfig, url, cancellationToken);
        if (requestContent is not null)
            return await Task.WhenAll(requestContent.Select(file => transformService.DecodeAsync(url,
                file.Contents, file.MimeType, file.Filename, cancellationToken: cancellationToken)));

        var supportedScrapers = scrapers
            .Where(a => a.SupportsHost(serverConfig, url));

        IEnumerable<FileItem>? fileContent = null;

        var domain = new Uri(url).Host; // e.g., "example.com"
        var markdown = $"GET [{domain}]({url})";

      
        foreach (var decoder in supportedScrapers)
        {
            fileContent = await decoder.GetContentAsync(mcpServer, serviceProvider, url, cancellationToken);

            if (fileContent != null)
            {                

                var decodeTasks = fileContent.Select(a => transformService.DecodeAsync(url,
                    a.Contents,
                    a.MimeType, a.Filename, cancellationToken: cancellationToken));

                var decoded = await Task.WhenAll(decodeTasks);            

                return decoded;
            }
        }

        var defaultScraper = await webScraper.GetContentAsync(url, cancellationToken);

        if (!defaultScraper.Success)
        {
            throw new Exception(defaultScraper.Error);
        }

        return [await transformService.DecodeAsync(url,
                          defaultScraper.Content,
                          defaultScraper.ContentType, cancellationToken: cancellationToken)];
    }

    public async Task<IEnumerable<FileItem>> DownloadContentAsync(IServiceProvider serviceProvider,
        ModelContextProtocol.Server.McpServer mcpServer,
        string url,
        CancellationToken cancellationToken = default)
        => await DownloadContentAsync(serviceProvider,
            serviceProvider.GetServerConfig(mcpServer) ?? throw new InvalidOperationException("Unknown server."),
            mcpServer, url, cancellationToken);

    public async Task<IEnumerable<FileItem>> DownloadContentAsync(IServiceProvider serviceProvider,
        ServerConfig serverConfig,
        ModelContextProtocol.Server.McpServer? mcpServer,
        string url,
        CancellationToken cancellationToken = default)
    {
        var requestContent = await DownloadForRequestAsync(serviceProvider, serverConfig, url, cancellationToken);
        if (requestContent is not null)
            return requestContent;

        var supportedScrapers = scrapers
            .Where(a => a.SupportsHost(serverConfig, url));

        IEnumerable<FileItem>? fileContent = null;

        foreach (var decoder in supportedScrapers)
        {
            fileContent = await decoder.GetContentAsync(mcpServer!, serviceProvider, url, cancellationToken);

            if (fileContent != null)
            {
                return fileContent;
            }
        }

        var defaultScraper = await webScraper.GetContentAsync(url, cancellationToken);

        if (!defaultScraper.Success)
        {
            throw new Exception(defaultScraper.Error);
        }

        return [new FileItem() {
            MimeType = defaultScraper?.ContentType!,
           // Stream = defaultScraper?.Content.ToStream(),
            Contents = defaultScraper?.Content!,
            Uri = url,
        }];
    }

    private async Task<IEnumerable<FileItem>?> DownloadForRequestAsync(IServiceProvider services,
        ServerConfig config, string url, CancellationToken cancellationToken)
    {
        foreach (var downloader in requestDownloaders ?? [])
            if (downloader.Supports(config))
                return await downloader.DownloadAsync(services, config, url, cancellationToken);
        return null;
    }

    [GeneratedRegex(@"^[^.]+\.crm\d+\.dynamics\.com$", RegexOptions.IgnoreCase | RegexOptions.Compiled, "nl-NL")]
    private static partial Regex DynamicsHostPattern();

}
