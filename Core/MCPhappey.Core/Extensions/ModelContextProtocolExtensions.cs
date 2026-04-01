using MCPhappey.Common.Models;
using MCPhappey.Core.Services;
using MCPhappey.Core.Services.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Core.Extensions;

#pragma warning disable MCPEXP001
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

                 ConfigureTaskRuntime(server, opts, headers, ctx.RequestServices);

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
                           => await server.Server.ToToolsList(kernel, [.. finalIcons], headers,
                               request.Services, cancellationToken)
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

    private static void ConfigureTaskRuntime(
        ServerConfig server,
        McpServerOptions opts,
        Dictionary<string, string> headers,
        IServiceProvider requestServices)
    {
        var taskOptions = server.Server.Tasks;
        if (taskOptions == null || string.IsNullOrWhiteSpace(taskOptions.Provider))
        {
            return;
        }

        var pollInterval = TimeSpan.FromMilliseconds(Math.Max(100, taskOptions.PollIntervalMs ?? 1000));

        var runtimeContext = new ExternalTaskRuntimeContext(server, taskOptions, headers, pollInterval);
        var provider = ExternalTaskRuntimeProviderFactory.Create(requestServices, runtimeContext);
        opts.TaskStore = provider.CreateTaskStore(runtimeContext);

        opts.Filters.Message.IncomingFilters.Add(next => async (context, cancellationToken) =>
        {
            if (context.JsonRpcMessage is JsonRpcRequest request)
            {
                if (request.Method == RequestMethods.TasksCancel)
                {
                    await context.Server.SendMessageAsync(new JsonRpcError
                    {
                        Id = request.Id,
                        Error = new JsonRpcErrorDetail
                        {
                            Code = (int)McpErrorCode.MethodNotFound,
                            Message = "Method 'tasks/cancel' is not available."
                        }
                    }, cancellationToken);
                    return;
                }
            }

            await next(context, cancellationToken);
        });

        opts.Filters.Message.OutgoingFilters.Add(next => async (context, cancellationToken) =>
        {
            if (context.JsonRpcMessage is JsonRpcResponse response)
            {
                provider.TryMutateInitializeResult(response, runtimeContext);
            }

            await next(context, cancellationToken);
        });
    }
}
#pragma warning restore MCPEXP001

