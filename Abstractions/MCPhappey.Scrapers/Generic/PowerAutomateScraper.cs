using System.Net.Mime;
using MCPhappey.Auth.Extensions;
using MCPhappey.Auth.Models;
using MCPhappey.Common;
using MCPhappey.Common.Models;
using MCPhappey.Scrapers.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace MCPhappey.Scrapers.Generic;

public class PowerAutomateScraper(IHttpClientFactory httpClientFactory, ServerConfig serverConfig,
    OAuthSettings oAuthSettings) : IContentScraper
{
    private static readonly string[] ValidHosts =
    [
        "emea.api.flow.microsoft.com",
        "us.api.flow.microsoft.com",
        "apac.api.flow.microsoft.com",
        "gcc.api.flow.microsoft.us",
        "service.flow.microsoft.com"
    ];

    public bool SupportsHost(ServerConfig currentConfig, string url)
    {
        if (currentConfig.Server.ServerInfo.Name != serverConfig.Server.ServerInfo.Name)
            return false;

        var host = new Uri(url).Host;

        // Power Automate OBO audience != API host
        return ValidHosts.Any(h =>
            host.Equals(h, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(h, StringComparison.OrdinalIgnoreCase) ||
            serverConfig.Server.OBO?.Keys.Any(k => h.EndsWith(k, StringComparison.OrdinalIgnoreCase)) == true);
    }

    public async Task<IEnumerable<FileItem>?> GetContentAsync(McpServer mcpServer, IServiceProvider serviceProvider,
         string url, CancellationToken cancellationToken = default)
    {
        var tokenService = serviceProvider.GetService<HeaderProvider>();

        if (string.IsNullOrEmpty(tokenService?.Bearer))
        {
            return null;
        }

     //   var uri = new Uri(url);

        var httpClient = await httpClientFactory.GetOboHttpClient(tokenService.Bearer, serverConfig.Server.OBO?.FirstOrDefault().Key!,
                serverConfig.Server, oAuthSettings);

        httpClient.DefaultRequestHeaders.Accept.Clear();
        httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue(MediaTypeNames.Application.Json));

        /*  if (
              uri.Host.Contains(Hosts.MicrosoftGraph, StringComparison.OrdinalIgnoreCase) &&
              System.Text.RegularExpressions.Regex.IsMatch(url, @"[\?&]\$search=")
          )
          {
              httpClient.DefaultRequestHeaders.Remove("ConsistencyLevel");
              httpClient.DefaultRequestHeaders.Add("ConsistencyLevel", "eventual");
          }*/

        using var result = await httpClient.GetWithContentExceptionAsync(url, cancellationToken);

        return [await result.ToFileItem(url, cancellationToken: cancellationToken)];
    }
}
