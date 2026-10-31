using MCPhappey.Common.Models;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using MCPhappey.Common;
using MCPhappey.Auth;
using Microsoft.AspNetCore.Http;

namespace MCPhappey.Core.Extensions;

public static class ServiceExtensions
{  

    public static void WithHeaders(this IServiceProvider serviceProvider, Dictionary<string, string>? headers)
    {
        var provider = serviceProvider?.GetService<HeaderProvider>();

        if (provider != null)
        {
            var context = serviceProvider!.GetService<IHttpContextAccessor>()?.HttpContext;
            provider.Headers = context?.Items[typeof(ServerRequestAccess)] is ServerRequestAccess access
                ? new Dictionary<string, string>(access.Headers, StringComparer.OrdinalIgnoreCase) : headers;
        }
    }

    public static ServerConfig? GetServerConfig(this IServiceProvider serviceProvider,
           McpServer mcpServer)
    {
        var configs = serviceProvider.GetRequiredService<IReadOnlyList<ServerConfig>>();
        return configs.GetServerConfig(mcpServer);
    }

}
