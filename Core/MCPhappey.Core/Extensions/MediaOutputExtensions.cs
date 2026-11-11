using MCPhappey.Auth;
using MCPhappey.Core.Services;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Core.Extensions;

public static class MediaOutputExtensions
{
    public static Task<CallToolResult> ToMediaCallToolResult(this McpServer server, IServiceProvider services,
        IEnumerable<MediaOutput> outputs, CancellationToken cancellationToken = default)
        => MediaOutputService.CreateAsync(outputs, services.GetEffectiveAccessMode(server) == ServerAccessMode.Delegated,
            (media, token) => server.Upload(services, media.Filename, media.Data, token), cancellationToken);
}
