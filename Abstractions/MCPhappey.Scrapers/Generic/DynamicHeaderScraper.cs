using MCPhappey.Common;
using MCPhappey.Common.Models;
using MCPhappey.Scrapers.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace MCPhappey.Scrapers.Generic;

public class DynamicHeaderScraper(IHttpClientFactory httpClientFactory) : IContentScraper
{
    private static readonly List<string> SupportedHosts = ["app.declaree.com", "api.applicationinsights.io"];

    public bool SupportsHost(ServerConfig serverConfig, string url) => SupportedHosts.Contains(new Uri(url).Host);

    public async Task<IEnumerable<FileItem>?> GetContentAsync(McpServer mcpServer, IServiceProvider serviceProvider, string url, CancellationToken cancellationToken = default)
    {
        var tokenService = serviceProvider.GetService<HeaderProvider>();
        var httpClient = httpClientFactory.CreateClient();

        foreach (var item in tokenService?.Headers ?? [])
        {
            httpClient.DefaultRequestHeaders.Add(item.Key, [item.Value]);
        }

        using var result = await httpClient.GetWithContentExceptionAsync(url, cancellationToken);

        return [await result.ToFileItem(url, cancellationToken)];
    }
}
