using MCPhappey.Common.Models;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using MCPhappey.Common;

namespace MCPhappey.Core.Extensions;

public static class ServiceExtensions
{  

    public static void WithHeaders(this IServiceProvider serviceProvider, Dictionary<string, string>? headers)
    {
        var provider = serviceProvider?.GetService<HeaderProvider>();

        if (provider != null)
        {
            provider!.Headers = headers;
        }
    }

    public static ServerConfig? GetServerConfig(this IServiceProvider serviceProvider,
           McpServer mcpServer)
    {
        var configs = serviceProvider.GetRequiredService<IReadOnlyList<ServerConfig>>();
        return configs.GetServerConfig(mcpServer);
    }

}