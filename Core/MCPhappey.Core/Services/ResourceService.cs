using System.Text;
using MCPhappey.Common.Extensions;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Core.Services;

public class ResourceService(DownloadService downloadService, IServerDataProvider? dynamicDataService = null)
{
    public async Task<ListResourceTemplatesResult> GetServerResourceTemplates(ServerConfig serverConfig,
          CancellationToken cancellationToken = default) => serverConfig.SourceType switch
          {
              ServerSourceType.Static => await Task.FromResult(serverConfig?.ResourceTemplateList
                                                ?? new()),
              ServerSourceType.Dynamic => await dynamicDataService!.GetResourceTemplatesAsync(serverConfig.Server.ServerInfo.Name,
                cancellationToken),
              _ => await Task.FromResult(serverConfig?.ResourceTemplateList
                                                ?? new()),
          };

    public async Task<ListResourcesResult> GetServerResources(ServerConfig serverConfig,
        CancellationToken cancellationToken = default) => serverConfig.SourceType switch
        {
            ServerSourceType.Static => await Task.FromResult(serverConfig?.ResourceList
                                              ?? new()),
            ServerSourceType.Dynamic => await dynamicDataService!.GetResourcesAsync(serverConfig.Server.ServerInfo.Name, cancellationToken),
            _ => await Task.FromResult(serverConfig?.ResourceList
                                              ?? new()),
        };

    public async Task<ReadResourceResult> GetServerResource(IServiceProvider serviceProvider,
        McpServer mcpServer,
        string uri,
        CancellationToken cancellationToken = default)
    {
        var serverConfig = serviceProvider.GetServerConfig(mcpServer);

        if (uri.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            var resources = await GetServerResources(serverConfig!, cancellationToken);

            var widgetResource = resources.Resources
                .FirstOrDefault(a => a.MimeType?.Equals("text/html+skybridge", StringComparison.OrdinalIgnoreCase) == true
                    && a.Uri.Equals(uri, StringComparison.OrdinalIgnoreCase));

            if (widgetResource != null)
            {
                var download = await downloadService.DownloadContentAsync(serviceProvider, mcpServer, uri,
                               cancellationToken);

                if (!download.Any())
                {
                    throw new Exception($"Resource {uri} not found");
                }

                var item = download.First();

                var httpContextAccessor = serviceProvider.GetRequiredService<IHttpContextAccessor>();
                var request = httpContextAccessor.HttpContext?.Request;
                var baseUrl = request != null
                    ? $"{request.Scheme}://{request.Host.Value}"
                    : null;

                var html = Encoding.UTF8.GetString(item.Contents);

                return new ReadResourceResult()
                {
                    Contents = [new TextResourceContents() {
                    Text = html.Replace("%HOST_URL%", baseUrl),
                    MimeType = "text/html+skybridge",
                    Uri = uri,
                    Meta = new System.Text.Json.Nodes.JsonObject() {
                        ["openai/widgetDescription"] = widgetResource.Description
                    }
                }]
                };
            }
            else 
            //if (mcpServer.ServerOptions.ServerInfo?.Name == "OneDrive-HTMLCanvas")
            {
                var download = await downloadService.DownloadContentAsync(serviceProvider, mcpServer, uri,
                              cancellationToken);

                if (!download.Any())
                {
                    throw new Exception($"Resource {uri} not found");
                }

                var item = download.First();

                return new ReadResourceResult()
                {
                    Contents = [new TextResourceContents() {
                    Text = Encoding.UTF8.GetString(item.Contents),
                    MimeType = "text/html",
                    Uri = uri
                }]
                };
            }
        }

        var fileItem = await downloadService.ScrapeContentAsync(serviceProvider, mcpServer, uri,
                       cancellationToken);

        return fileItem.ToReadResourceResult();
    }
}
