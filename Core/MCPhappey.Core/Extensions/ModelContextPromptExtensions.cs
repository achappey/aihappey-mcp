using System.Text.Json;
using MCPhappey.Common.Extensions;
using MCPhappey.Common.Models;
using MCPhappey.Core.Services;
using MCPhappey.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;

namespace MCPhappey.Core.Extensions;

public static partial class ModelContextPromptExtensions
{
    public static async Task<ListPromptsResult?> ToListPromptsResult(this ServerConfig serverConfig,
       ModelContextProtocol.Server.RequestContext<ListPromptsRequestParams> request,
        CancellationToken cancellationToken = default)
    {
        var service = request.Services!.GetRequiredService<PromptService>();

        return serverConfig.Server.Capabilities.Prompts != null ?
            await service.GetServerPrompts(serverConfig, cancellationToken)
          : null;
    }

    public static int GetJsonSizeInBytes<T>(this T obj)
        => JsonSerializer.SerializeToUtf8Bytes(obj).Length;

    public static async Task<GetPromptResult>? ToGetPromptResult(
        this ModelContextProtocol.Server.RequestContext<GetPromptRequestParams> request,
        Dictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default)
    {
        var service = request.Services!.GetRequiredService<PromptService>();
        var telemtry = request.Services!.GetService<IMcpTelemetryService>();
        var userId = request.User?.Claims.GetUserOid();
        var startTime = DateTime.UtcNow;

        request.Services!.WithHeaders(headers);

        var prompt = await service.GetServerPrompt(request.Services!, request.Server,
            request.Params?.Name!,
            request.Params?.Arguments?.AsReadOnly() ?? new Dictionary<string, JsonElement>().AsReadOnly(),
            request,
            cancellationToken);

        var endTime = DateTime.UtcNow;

        if (telemtry != null)
        {
            await telemtry.TrackPromptRequestAsync(request.Server.ServerOptions.ServerInfo?.Name!,
                request.Server.SessionId!,
                request.Server.ClientInfo?.Name!,
                request.Server.ClientInfo?.Version!,
                prompt.GetJsonSizeInBytes(), startTime, endTime, userId, request.User?.Identity?.Name, cancellationToken);
        }

        return prompt;

    }
}