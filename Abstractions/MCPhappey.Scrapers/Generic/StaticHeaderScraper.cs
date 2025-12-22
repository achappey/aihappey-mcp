using MCPhappey.Common;
using MCPhappey.Common.Models;
using MCPhappey.Scrapers.Extensions;
using ModelContextProtocol.Server;

namespace MCPhappey.Scrapers.Generic;

public class StaticHeaderScraper(IHttpClientFactory httpClientFactory, string hostName,
    IDictionary<string, string> headers) : IContentScraper
{
    public bool SupportsHost(ServerConfig serverConfig, string url) => new Uri(url).Host == hostName;

    public async Task<IEnumerable<FileItem>?> GetContentAsync(McpServer mcpServer, IServiceProvider serviceProvider, string url, CancellationToken cancellationToken = default)
    {
        var httpClient = httpClientFactory.CreateClient();

        foreach (var item in headers)
        {
            httpClient.DefaultRequestHeaders.Add(item.Key, [item.Value]);
        }

        using var result = await httpClient.GetWithContentExceptionAsync(url, cancellationToken);

        return [await result.ToFileItem(url, cancellationToken)];
    }
}
