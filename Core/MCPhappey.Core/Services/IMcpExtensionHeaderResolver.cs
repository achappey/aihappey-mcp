using MCPhappey.Common.Models;

namespace MCPhappey.Core.Services;

public interface IMcpExtensionHeaderResolver
{
    Task<Dictionary<string, string>?> ResolveHeadersAsync(
        IServiceProvider serviceProvider,
        Server server,
        Dictionary<string, string>? requestHeaders,
        CancellationToken cancellationToken = default);
}

