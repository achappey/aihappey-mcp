using MCPhappey.Auth;
using MCPhappey.Core.Services;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Core.Extensions;

public static class ImageOutputExtensions
{
    public static Task<CallToolResult> ToImageCallToolResult(this McpServer server, IServiceProvider services,
        IEnumerable<ImageOutput> images, CancellationToken cancellationToken = default)
        => ImageOutputService.CreateAsync(images, services.GetEffectiveAccessMode(server) == ServerAccessMode.Delegated,
            (image, token) => server.Upload(services, image.Filename, image.Data, token), cancellationToken);
}
