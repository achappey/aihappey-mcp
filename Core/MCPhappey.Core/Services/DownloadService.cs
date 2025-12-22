
using System.Text.RegularExpressions;
using MCPhappey.Core.Extensions;
using MCPhappey.Common.Models;
using Microsoft.KernelMemory.DataFormats.WebPages;
using MCPhappey.Common;
using ModelContextProtocol.Protocol;
using MCPhappey.Common.Extensions;

namespace MCPhappey.Core.Services;

public partial class DownloadService(WebScraper webScraper,
    TransformService transformService,
    IEnumerable<IContentScraper> scrapers) : IWebScraper
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

        var supportedScrapers = scrapers
            .Where(a => a.SupportsHost(serverConfig, url));

        IEnumerable<FileItem>? fileContent = null;

        var domain = new Uri(url).Host; // e.g., "example.com"
        var markdown = $"GET [{domain}]({url})";

        await mcpServer.SendMessageNotificationAsync(markdown, LoggingLevel.Info, CancellationToken.None);

        foreach (var decoder in supportedScrapers)
        {
            fileContent = await decoder.GetContentAsync(mcpServer, serviceProvider, url, cancellationToken);

            if (fileContent != null)
            {
                if (mcpServer.LoggingLevel == LoggingLevel.Debug)
                {
                    foreach (var file in fileContent)
                    {
                        var fileMarkdown =
                        $"<details><summary><a href=\"{file.Uri}\" target=\"blank\">GET ScrapeContentAsync {new Uri(file.Uri).Host}</a></summary>\n\n```\n{(file.Contents).ToString()}\n```\n</details>";

                        await mcpServer.SendMessageNotificationAsync(fileMarkdown, LoggingLevel.Debug, CancellationToken.None);
                    }
                }

                var decodeTasks = fileContent.Select(a => transformService.DecodeAsync(url,
                    a.Contents,
                    a.MimeType, a.Filename, cancellationToken: cancellationToken));

                var decoded = await Task.WhenAll(decodeTasks);

                if (mcpServer.LoggingLevel == LoggingLevel.Debug)
                {
                    foreach (var file in decoded)
                    {
                        var fileMarkdown =
                        $"<details><summary><a href=\"{file.Uri}\" target=\"blank\">DECODE ScrapeContentAsync {new Uri(file.Uri).Host}</a></summary>\n\n```\n{((file.Contents)).ToString()}\n```\n</details>";

                        await mcpServer.SendMessageNotificationAsync(fileMarkdown, LoggingLevel.Debug, CancellationToken.None);
                    }
                }

                return decoded;
            }
        }

        var defaultScraper = await webScraper.GetContentAsync(url, cancellationToken);

        if (!defaultScraper.Success)
        {
            var fileMarkdown =
                      $"<details><summary><a href=\"{url}\" target=\"blank\">ERROR ScrapeContentAsync {new Uri(url).Host}</a></summary>\n\n```\n{defaultScraper.Error}\n```\n</details>";

            await mcpServer.SendMessageNotificationAsync(fileMarkdown, LoggingLevel.Error, CancellationToken.None);

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
    {
        Uri uri = new(url);
        var serverConfig = serviceProvider.GetServerConfig(mcpServer)
            ?? throw new Exception();

        var supportedScrapers = scrapers
            .Where(a => a.SupportsHost(serverConfig, url));

        IEnumerable<FileItem>? fileContent = null;

        var domain = new Uri(url).Host; // e.g., "example.com"
        var markdown = $"GET [{domain}]({url})";

        await mcpServer.SendMessageNotificationAsync(markdown, LoggingLevel.Info);

        foreach (var decoder in supportedScrapers)
        {
            fileContent = await decoder.GetContentAsync(mcpServer, serviceProvider, url, cancellationToken);

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

    [GeneratedRegex(@"^[^.]+\.crm\d+\.dynamics\.com$", RegexOptions.IgnoreCase | RegexOptions.Compiled, "nl-NL")]
    private static partial Regex DynamicsHostPattern();

}
