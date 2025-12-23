using MCPhappey.Common;
using MCPhappey.Common.Models;
using MCPhappey.Scrapers.Extensions;
using ModelContextProtocol.Server;

namespace MCPhappey.Scrapers.Generic;

public class NominatimScraper(IHttpClientFactory httpClientFactory) : IContentScraper
{
    private const string SupportedHost = "nominatim.openstreetmap.org";
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(2);

    // fallback User-Agent if none provided
    private readonly string _userAgent = "aihappey-Nominatim/1.0 (https://github.com/achappey/aihappey-mcp)";

    public bool SupportsHost(ServerConfig serverConfig, string url)
    {
        var host = new Uri(url).Host;
        return host.Equals(SupportedHost, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<IEnumerable<FileItem>?> GetContentAsync(
        McpServer mcpServer,
        IServiceProvider serviceProvider,
        string url,
        CancellationToken cancellationToken = default)
    {
        // Respect public usage policy: max 1 request per second
        await Task.Delay(Delay, cancellationToken);

        var httpClient = httpClientFactory.CreateClient();

        // Required by OpenStreetMap Foundation usage policy
        httpClient.DefaultRequestHeaders.UserAgent.Clear();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(_userAgent);

        using var result = await httpClient.GetWithContentExceptionAsync(url, cancellationToken);

        return [await result.ToFileItem(url, cancellationToken)];
    }
}

