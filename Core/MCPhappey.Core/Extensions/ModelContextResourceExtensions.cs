using MCPhappey.Common.Extensions;
using MCPhappey.Common.Models;
using MCPhappey.Core.Services;
using MCPhappey.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;

namespace MCPhappey.Core.Extensions;

public static partial class ModelContextResourceExtensions
{
    public static ListResourceTemplatesResult WithIcons(this ListResourceTemplatesResult result, IEnumerable<Icon>? defaultIcons)
    {
        foreach (var r in result.ResourceTemplates)
        {
            r.Icons = r.Icons != null && r.Icons.Any() ? r.Icons : (defaultIcons?.ToList() ?? []);
        }

        return result;
    }

    public static ListResourcesResult WithIcons(this ListResourcesResult result, IEnumerable<Icon>? defaultIcons)
    {
        foreach (var r in result.Resources)
        {
            r.Icons = r.Icons != null && r.Icons.Any() ? r.Icons : (defaultIcons?.ToList() ?? []);
        }

        return result;
    }

    public static async Task<ListResourcesResult?> ToListResourcesResult(
       this ServerConfig serverConfig,
       ModelContextProtocol.Server.RequestContext<ListResourcesRequestParams> request,
       Dictionary<string, string>? headers = null,
       CancellationToken cancellationToken = default)
    {
        var service = request.Services!.GetRequiredService<ResourceService>();

        return serverConfig.Server.Capabilities.Resources != null
            ? await service.GetServerResources(serverConfig, cancellationToken)
            : null;
    }

    public static async Task<ListResourceTemplatesResult?> ToListResourceTemplatesResult(
        this ServerConfig serverConfig,
        ModelContextProtocol.Server.RequestContext<ListResourceTemplatesRequestParams> request,
        Dictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default)
    {
        var service = request.Services!.GetRequiredService<ResourceService>();
        return serverConfig.Server.Capabilities.Resources != null
            ? await service.GetServerResourceTemplates(serverConfig, cancellationToken)
            : null;
    }

    public static async Task<ReadResourceResult?> ToReadResourceResult(
        this ModelContextProtocol.Server.RequestContext<ReadResourceRequestParams> request,
        Dictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default)
    {

        var telemtry = request.Services!.GetService<IMcpTelemetryService>();
        var userId = request.User?.Claims.GetUserOid();
        var startTime = DateTime.UtcNow;

        var service = request.Services!.GetRequiredService<ResourceService>();

        request.Services!.WithHeaders(headers);

        try
        {
            var finalResult = await service.GetServerResource(
                request.Services!,
                request.Server,
                request.Params?.Uri!,
                cancellationToken);

            var endTime = DateTime.UtcNow;

            if (telemtry != null)
            {
                await telemtry.TrackResourceRequestAsync(request.Server.ServerOptions.ServerInfo?.Name!,
                    request.Server.SessionId!,
                    request.Server.ClientInfo?.Name!,
                    request.Server.ClientInfo?.Version!,
                    request.Params?.Uri!,
                    finalResult.GetJsonSizeInBytes(),
                    startTime, endTime, userId, request.User?.Identity?.Name, cancellationToken);
            }

            return finalResult;
        }
        catch (Exception e)
        {
            var fileMarkdown =
                $"<details><summary><a href=\"{request.Params?.Uri}\" target=\"blank\">ERROR ReadResource {new Uri(request.Params?.Uri!).Host}</a></summary>\n\n```\n{e.Message}\n```\n</details>";

            await request.Server.SendMessageNotificationAsync(
                fileMarkdown, LoggingLevel.Error, CancellationToken.None);

            return new ReadResourceResult
            {
                Contents =
                [
                    new TextResourceContents
                    {
                        MimeType = "text/plain",
                        Text = e.Message,
                        Uri = request.Params?.Uri!
                    }
                ]
            };
        }
    }
}