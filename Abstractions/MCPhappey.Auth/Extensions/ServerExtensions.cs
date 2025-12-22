using MCPhappey.Common.Constants;
using MCPhappey.Common.Models;

namespace MCPhappey.Auth.Extensions;

public static class ServerExtensions
{
    private static readonly StringComparison Cmp = StringComparison.OrdinalIgnoreCase;

    private static bool IsAuthHost(string host)
        => host.Equals(Hosts.MicrosoftGraph, Cmp)
           || host.EndsWith(".dynamics.com", Cmp)
           || host.EndsWith(".sharepoint.com", Cmp)
           || host.EndsWith(".vault.azure.net", Cmp);

    /// <summary>
    /// Returns <c>true</c> when the <see cref="Server"/> contains metadata entries that require OAuth.
    /// </summary>
    public static bool HasAuth(this Server server)
        => server?.OBO?.Keys.Any() == true ? true : false;

    /// <summary>
    /// Returns a new dictionary that excludes entries that require OAuth.
    /// </summary>
    public static IDictionary<string, string> WithoutAuth(this IDictionary<string, string> metadata)
        => metadata?.Where(kvp => !IsAuthHost(kvp.Key))
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
           ?? [];

    public static bool IsAuthorized(this ServerConfig serverConfig, Dictionary<string, string> headers, IEnumerable<string> userRoles)
    {
        // Header check
        bool headersOk = serverConfig.Server.Headers?.Any() != true ||
                         serverConfig.Server.Headers.All(a => headers.ContainsKey(a.Key));

        // Role check
        var requiredRoles = serverConfig.Server.Roles?.ToList() ?? [];
        bool rolesOk = !requiredRoles.Any() ||
                       (userRoles != null && requiredRoles.Intersect(userRoles, StringComparer.OrdinalIgnoreCase).Any());

        return headersOk && rolesOk;
    }


}
