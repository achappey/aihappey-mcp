using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using MCPhappey.Auth;
using MCPhappey.Auth.Extensions;
using MCPhappey.Common.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace MCPhappey.Core.Services;

/// <summary>Enforces the effective configuration before any MCP transport/session request is processed.</summary>
public sealed class ServerAccessMiddleware(RequestDelegate next, ServerAccessPolicy policy)
{
    public async Task InvokeAsync(HttpContext context, IReadOnlyList<ServerConfig> servers)
    {
        var name = context.Request.Path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        var config = servers.FirstOrDefault(server => server.Server.ServerInfo.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (config is null)
        {
            await next(context);
            return;
        }

        if (config.SourceType == ServerSourceType.Dynamic)
        {
            var provider = context.RequestServices.GetService<IServerDataProvider>();
            if (provider is not null)
            {
                config = await provider.GetServer(config.Server.ServerInfo.Name, context.RequestAborted);
                if (config is null)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }
            }
        }

        var server = config.Server;
        var mode = policy.GetMode(server);
        var incoming = context.Request.Headers.ToDictionary(pair => pair.Key, pair => pair.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        if (mode == ServerAccessMode.Unavailable)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsync("This server's required access capabilities are not configured.");
            return;
        }

        if (mode == ServerAccessMode.Delegated)
        {
            var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
            var resource = $"{baseUrl}/{server.ServerInfo.Name.ToLowerInvariant()}";
            if (!AuthenticationHeaderValue.TryParse(context.Request.Headers.Authorization, out var authorization)
                || !authorization.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(authorization.Parameter))
            {
                await Unauthorized(context, resource);
                return;
            }

            var validator = context.RequestServices.GetRequiredService<IJwtValidator>();
            var principal = await validator.ValidateAsync(authorization.Parameter, baseUrl, resource, policy.OAuth!);
            if (principal is null)
            {
                await Unauthorized(context, resource);
                return;
            }

            var permissions = principal.FindAll("roles").Concat(principal.FindAll(ClaimTypes.Role))
                .Select(claim => claim.Value).Concat(principal.FindAll("role").Select(claim => claim.Value))
                .Concat((principal.FindFirst("scp")?.Value ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var rolesAllowed = server.Roles?.Any() != true || server.Roles.Any(permissions.Contains);
            var ownershipAllowed = config.SourceType != ServerSourceType.Dynamic || IsOwnerOrGroupAuthorized(server, principal);
            if (!rolesAllowed || !ownershipAllowed)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Forbidden: the authenticated subject does not have access to this server.");
                return;
            }
            context.User = principal;
        }
        else if (mode == ServerAccessMode.Headers)
        {
            try
            {
                if (!policy.HasRequiredHeaders(server, incoming))
                    throw new UnauthorizedAccessException();
                var identityMaterial = string.Join('\n', (server.Headers ?? []).OrderBy(header => header.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(header => incoming.GetValueOrDefault(header.Name, "")));
                // Bind MCP sessions to a credential identity, without retaining the raw key as a claim.
                var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(server.ServerInfo.Name + "\n" + identityMaterial)));
                context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, identity)], "McpProviderCredential"));
            }
            catch (UnauthorizedAccessException)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Missing or invalid provider credential headers.");
                return;
            }
        }

        var allowed = mode == ServerAccessMode.Delegated ? new HashSet<string>(["Authorization"], StringComparer.OrdinalIgnoreCase)
            : (server.Headers ?? []).Select(header => header.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var headers = incoming.Where(pair => allowed.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        context.Items[typeof(ServerRequestAccess)] = new ServerRequestAccess(config, mode, headers);
        await next(context);
    }

    public static bool IsOwnerOrGroupAuthorized(Server server, ClaimsPrincipal principal)
    {
        var oid = principal.FindFirst("oid")?.Value ?? principal.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
        var owners = server.Owners?.Contains(oid ?? "", StringComparer.OrdinalIgnoreCase) == true;
        var groups = principal.GetGroupClaims();
        return owners || server.Groups?.Any(groups.Contains) == true;
    }

    private static async Task Unauthorized(HttpContext context, string resource)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        var uri = new Uri(resource);
        context.Response.Headers.WWWAuthenticate = $"Bearer resource_metadata=\"{uri.GetLeftPart(UriPartial.Authority)}/.well-known/oauth-protected-resource{uri.AbsolutePath}\"";
        await context.Response.WriteAsync("Invalid or missing token");
    }
}
