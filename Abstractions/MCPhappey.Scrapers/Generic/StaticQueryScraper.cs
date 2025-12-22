using MCPhappey.Common;
using MCPhappey.Common.Models;
using MCPhappey.Scrapers.Extensions;
using ModelContextProtocol.Server;

namespace MCPhappey.Scrapers.Generic;

public class StaticQueryScraper(IHttpClientFactory httpClientFactory, string hostName,
    IDictionary<string, string> headers) : IContentScraper
{
    public bool SupportsHost(ServerConfig serverConfig, string url) => new Uri(url).Host == hostName;

    public async Task<IEnumerable<FileItem>?> GetContentAsync(McpServer mcpServer,
        IServiceProvider serviceProvider, string url, CancellationToken cancellationToken = default)
    {
        var httpClient = httpClientFactory.CreateClient();

        var uriBuilder = new UriBuilder(url);
        var query = System.Web.HttpUtility.ParseQueryString(uriBuilder.Query);

        foreach (var item in headers)
        {
            query[item.Key] = item.Value;
        }

        uriBuilder.Query = query.ToString();
        var finalUrl = uriBuilder.ToString();

        using var result = await httpClient.GetWithContentExceptionAsync(finalUrl, cancellationToken);

        return [await result.ToFileItem(url, cancellationToken)];
    }
}
