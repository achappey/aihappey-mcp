using MCPhappey.Common;
using MCPhappey.Common.Extensions;
using MCPhappey.Common.Models;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace MCPhappey.Scrapers.Generic;

public class McpStatisticsScraper(IReadOnlyList<ServerConfig> serverConfigs) : IContentScraper
{
    public bool SupportsHost(ServerConfig serverConfig, string url)
        => new Uri(url).Scheme.Equals("mcp-servers", StringComparison.OrdinalIgnoreCase);

    public async Task<IEnumerable<FileItem>?> GetContentAsync(McpServer mcpServer,
        IServiceProvider serviceProvider, string url, CancellationToken cancellationToken = default)
    {
        var tokenService = serviceProvider.GetService<HeaderProvider>();
        if (string.IsNullOrEmpty(tokenService?.Bearer))
        {
            return null;
        }

        if (url.Equals("mcp-servers://statistics"))
        {
            var servers = serverConfigs.Where(a => a.SourceType == ServerSourceType.Static);

            var totalServers = servers.Count();
            var totalPrompts = servers.Sum(a => a.PromptList?.Prompts.Count ?? 0);
            var totalResources = servers.Sum(a => a.ResourceList?.Resources.Count ?? 0);
            var totalResourceTemplates = servers.Sum(a => a.ResourceTemplateList?.ResourceTemplates.Count ?? 0);

            // Avoid division by zero, obviously
            var avgPromptsPerServer = totalServers == 0 ? 0 : (double)totalPrompts / totalServers;
            var avgResourcesPerServer = totalServers == 0 ? 0 : (double)totalResources / totalServers;
            var avgTemplatesPerServer = totalServers == 0 ? 0 : (double)totalResourceTemplates / totalServers;

            var stats = new
            {
                TotalServers = totalServers,
                TotalPrompts = totalPrompts,
                TotalResources = totalResources,
                TotalResourceTemplates = totalResourceTemplates,

                AveragePromptsPerServer = avgPromptsPerServer,
                AverageResourcesPerServer = avgResourcesPerServer,
                AverageResourceTemplatesPerServer = avgTemplatesPerServer
            };

            return await Task.FromResult<IEnumerable<FileItem>>([stats.ToFileItem(url)]);
        }

        throw new Exception("Uri not supported");
    }

}
