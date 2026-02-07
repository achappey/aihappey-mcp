using System.Net.Mime;
using MCPhappey.Auth.Extensions;
using MCPhappey.Auth.Models;
using MCPhappey.Common;
using MCPhappey.Common.Constants;
using MCPhappey.Common.Models;
using MCPhappey.Scrapers.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace MCPhappey.Scrapers.Generic;

public class OboClientScraper(IHttpClientFactory httpClientFactory, ServerConfig serverConfig,
    OAuthSettings oAuthSettings) : IContentScraper
{
    public bool SupportsHost(ServerConfig currentConfig, string url)
        =>
            currentConfig.Server.ServerInfo.Name == serverConfig.Server.ServerInfo.Name
            &&
            serverConfig.Server.OBO?.Keys.Any(a => a == new Uri(url).Host
                || new Uri(url).Host.EndsWith(a)) == true;

    public async Task<IEnumerable<FileItem>?> GetContentAsync(McpServer mcpServer, IServiceProvider serviceProvider,
         string url, CancellationToken cancellationToken = default)
    {
        var tokenService = serviceProvider.GetService<HeaderProvider>();

        if (string.IsNullOrEmpty(tokenService?.Bearer))
        {
            return null;
        }

        var uri = new Uri(url);

        var httpClient = await httpClientFactory.GetOboHttpClient(tokenService.Bearer, uri.Host,
                serverConfig.Server, oAuthSettings);

        httpClient.DefaultRequestHeaders.Accept.Clear();
        httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue(MediaTypeNames.Application.Json));

        if (uri.Host.Contains(Hosts.MicrosoftGraph, StringComparison.OrdinalIgnoreCase))
        {
            httpClient.DefaultRequestHeaders.Remove("ConsistencyLevel");
            httpClient.DefaultRequestHeaders.Add("ConsistencyLevel", "eventual");
        }

        using var result = await httpClient.GetWithContentExceptionAsync(url, cancellationToken);

        return [await result.ToFileItem(url, cancellationToken: cancellationToken)];
    }
}
