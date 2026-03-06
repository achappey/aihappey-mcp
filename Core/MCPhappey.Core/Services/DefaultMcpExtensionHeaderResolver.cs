using MCPhappey.Common.Models;

namespace MCPhappey.Core.Services;

public sealed class DefaultMcpExtensionHeaderResolver : IMcpExtensionHeaderResolver
{
    public Task<Dictionary<string, string>?> ResolveHeadersAsync(
        IServiceProvider serviceProvider,
        Server server,
        Dictionary<string, string>? requestHeaders,
        CancellationToken cancellationToken = default)
    {
        var staticHeaders = server.McpExtension?.Headers;
        return Task.FromResult(staticHeaders == null
            ? null
            : new Dictionary<string, string>(staticHeaders, StringComparer.OrdinalIgnoreCase));
    }
}

