using MCPhappey.Common.Models;
using MCPhappey.Core.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using ModelContextProtocol.Protocol;

namespace MCPhappey.Core.Extensions;

public static class ModelContextProtocolExtensions
{
    public static IMcpServerBuilder WithConfigureSessionOptions(this IMcpServerBuilder mcpBuilder,
        IEnumerable<ServerConfig> servers) => mcpBuilder.WithHttpTransport(http =>
        {
            http.ConfigureSessionOptions = async (ctx, opts, cancellationToken) =>
             {
                 var kernel = ctx.RequestServices.GetRequiredService<Kernel>();
                 var serverIcons = ctx.RequestServices.GetService<List<ServerIcon>>();
                 var completionService = ctx.RequestServices.GetRequiredService<CompletionService>();
                 var serverName = ctx.Request.Path.Value!.GetServerNameFromUrl();
                 var server = servers.First(a => a.Server.ServerInfo?.Name.Equals(serverName, StringComparison.OrdinalIgnoreCase) == true);

                 if (server.SourceType == ServerSourceType.Dynamic)
                 {
                     var dataService = ctx.RequestServices.GetService<IServerDataProvider>();
                     if (dataService != null)
                     {
                         var dbItem = await dataService.GetServer(serverName, cancellationToken);

                         if (dbItem != null)
                         {
                             server = dbItem;
                         }
                     }
                 }

                 var headers = ctx.Request.Headers.Where(a => server.Server.Headers?.ContainsKey(a.Key) == true ||
                    (server.Server.OBO?.Count > 0 && a.Key == "Authorization"))
                        .ToDictionary(h => h.Key, h => h.Value.ToString());

                 if (completionService.CanComplete(server, cancellationToken))
                 {
                     opts.Handlers.CompleteHandler = async (request, cancellationToken)
                            => await request.ToCompleteResult(server, completionService, headers,
                                cancellationToken: cancellationToken)
                                ?? new CompleteResult();
                 }

                 var defaultIcons = serverIcons?.Select(a => new Icon()
                 {
                     Source = a.Source,
                     Sizes = a.Sizes?.ToList(),
                     MimeType = a.MimeType,
                     Theme = a.Theme
                 }).ToList() ?? [];

                 var finalIcons = server.SourceType == ServerSourceType.Static ?
                                          server.Server.ServerInfo.Icons?.ToList() ?? [] :
                                          server.Server.ServerInfo.Icons?.Any() == true ?
                                          server.Server.ServerInfo.Icons : defaultIcons;

                 if (server.Server.Capabilities.Tools != null || server.Server.McpExtension != null)
                 {
                     opts.Handlers.ListToolsHandler = async (request, cancellationToken)
                           => await server.Server.ToToolsList(kernel, [.. finalIcons], headers)
                            ?? new();

                     opts.Handlers.CallToolHandler = async (request, cancellationToken)
                                => await request.ToCallToolResult(server.Server, kernel,
                                    [.. finalIcons], headers, cancellationToken: cancellationToken)
                                    ?? new();
                 }

                 if (server.Server.Capabilities.Prompts != null)
                 {
                     opts.Handlers.ListPromptsHandler = async (request, cancellationToken)
                            => (await server.ToListPromptsResult(request, cancellationToken))?.WithIcons(finalIcons)
                                ?? new();

                     opts.Handlers.GetPromptHandler = async (request, cancellationToken)
                             => await request.ToGetPromptResult(headers, cancellationToken)!
                                 ?? new();
                 }

                 if (server.Server.Capabilities.Resources != null)
                 {
                     opts.Handlers.ListResourcesHandler = async (request, cancellationToken) =>
                         (await server.ToListResourcesResult(request, headers, cancellationToken))?.WithIcons(finalIcons)
                             ?? new();

                     opts.Handlers.ListResourceTemplatesHandler = async (request, cancellationToken) =>
                         (await server.ToListResourceTemplatesResult(request, headers, cancellationToken))?.WithIcons(finalIcons)
                             ?? new();

                     opts.Handlers.ReadResourceHandler = async (request, cancellationToken) =>
                         await request.ToReadResourceResult(headers, cancellationToken)
                             ?? new();
                 }

                 opts.ServerInfo = server.Server.ToServerInfo();
                 opts.ServerInstructions = server.Server.Instructions;
             };
        });
}
