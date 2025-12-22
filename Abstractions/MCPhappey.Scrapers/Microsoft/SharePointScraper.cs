using MCPhappey.Auth.Models;
using MCPhappey.Common;
using MCPhappey.Common.Constants;
using MCPhappey.Common.Models;
using MCPhappey.Scrapers.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace MCPhappey.Scrapers.Microsoft;

public class SharePointScraper(IHttpClientFactory httpClientFactory, ServerConfig serverConfig,
    OAuthSettings oAuthSettings) : IContentScraper
{
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
