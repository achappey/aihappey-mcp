using MCPhappey.Auth.Extensions;
using MCPhappey.Common;
using MCPhappey.Servers.SQL.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace MCPhappey.Servers.SQL.Extensions;

public static class McpServerEditorExtensions
{
    public static double? GetPriority(this float? value, int digits)
        => value.HasValue ? Math.Round(value.Value, digits) : null;

    public static async Task<IEnumerable<Models.Server>> GetStatistics(this IServiceProvider serviceProvider, CancellationToken ct = default)
    {
        var tokenService = serviceProvider.GetService<HeaderProvider>();
        var userId = serviceProvider.GetUserId();
        var serverRepository = serviceProvider.GetRequiredService<ServerRepository>();

        var servers = await serverRepository.GetServers(ct);
        return servers.Where(a => a.CanEdit(userId));
    }

    public static async Task<bool> ServerExists(this IServiceProvider serviceProvider,
     string serverName,
     CancellationToken ct = default)
    {
        var serverRepository = serviceProvider.GetRequiredService<ServerRepository>();
        var server = await serverRepository.GetServer(serverName, ct);
        return server != null;
    }

    public static async Task<IEnumerable<Models.Server>> GetServers(this IServiceProvider serviceProvider, CancellationToken ct = default)
    {
        var tokenService = serviceProvider.GetService<HeaderProvider>();

        if (string.IsNullOrEmpty(tokenService?.Bearer))
        {
            throw new UnauthorizedAccessException();
        }

        var userId = serviceProvider.GetUserId();
        var serverRepository = serviceProvider.GetRequiredService<ServerRepository>();

        var servers = await serverRepository.GetServers(ct);
        return servers.Where(a => a.CanEdit(userId));
    }

    public static async Task<Models.Server> GetServer(this IServiceProvider serviceProvider, string name, CancellationToken ct = default)
    {
        var tokenService = serviceProvider.GetService<HeaderProvider>();
        if (string.IsNullOrEmpty(tokenService?.Bearer))
        {
            throw new UnauthorizedAccessException();
        }

        var userId = serviceProvider.GetUserId();
        var serverRepository = serviceProvider.GetRequiredService<ServerRepository>();
        var server = await serverRepository.GetServer(name, ct) ?? throw new ArgumentException();

        if (!server.CanEdit(userId))
        {
            throw new UnauthorizedAccessException();
        }

        return server;
    }

    private static bool CanEdit(this Models.Server server, string? userId)
        => !string.IsNullOrEmpty(userId) && server?.Owners.Select(a => a.Id).Contains(userId) == true;
}
