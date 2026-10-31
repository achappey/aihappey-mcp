using MCPhappey.Auth;
using MCPhappey.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace MCPhappey.Core.Extensions;

public static class ProviderCredentialExtensions
{
    public static ServerAccessMode GetEffectiveAccessMode(this IServiceProvider services, McpServer server)
    {
        var config = services.GetServerConfig(server) ?? throw new InvalidOperationException("Unknown MCP server.");
        var policy = services.GetRequiredService<ServerAccessPolicy>();
        var mode = policy.GetMode(config.Server);
        if (mode == ServerAccessMode.Unavailable)
            throw new UnauthorizedAccessException("This server's required access capabilities are unavailable.");
        if (mode == ServerAccessMode.Delegated)
        {
            var context = services.GetService<IHttpContextAccessor>()?.HttpContext;
            if (context?.Items[typeof(ServerRequestAccess)] is not ServerRequestAccess { Mode: ServerAccessMode.Delegated } access
                || !access.Config.Server.ServerInfo.Name.Equals(config.Server.ServerInfo.Name, StringComparison.OrdinalIgnoreCase)
                || context.User.Identity?.IsAuthenticated != true)
                throw new UnauthorizedAccessException("This operation requires validated delegated authentication.");
        }
        return mode;
    }

    /// <summary>Extracts a caller input using the server's registry header template. Never falls back to operator credentials.</summary>
    public static string GetProviderCredential(this IServiceProvider services, McpServer server, string variableName)
    {
        if (services.GetEffectiveAccessMode(server) != ServerAccessMode.Headers)
            throw new UnauthorizedAccessException("Caller-supplied provider credentials are not active for this request.");
        var config = services.GetServerConfig(server)!;
        var context = services.GetService<IHttpContextAccessor>()?.HttpContext;
        var headers = context?.Items[typeof(ServerRequestAccess)] is ServerRequestAccess access
            ? access.Headers : services.GetService<HeaderProvider>()?.Headers;
        if (headers is null)
            throw new UnauthorizedAccessException("No provider credential headers are available.");
        return services.GetRequiredService<ServerAccessPolicy>().GetCredential(config.Server, headers, variableName);
    }
}
