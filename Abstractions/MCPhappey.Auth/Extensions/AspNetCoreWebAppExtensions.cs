using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MCPhappey.Auth.Models;
using MCPhappey.Common.Extensions;
using MCPhappey.Common.Models;

namespace MCPhappey.Auth.Extensions;

public static class AspNetCoreWebAppExtensions
{
    public static void MapServerOAuth(
              this WebApplication webApp,
              string serverUrl,
              string[] scopes)
    {
        webApp.MapGet($"/.well-known/oauth-protected-resource{serverUrl}", (HttpContext context) =>
        {
            var request = context.Request;
            var baseUri = $"{request.Scheme}://{request.Host.Value}";

            return Results.Json(new
            {
                authorization_servers = new[]
                {
                     $"{baseUri}/.well-known/oauth-authorization-server"
                },
                resource = $"{baseUri}{serverUrl}",
                scopes_supported = scopes,
            });
        });
    }

    public static WebApplication MapOAuth(
            this WebApplication webApp,
            List<ServerConfig> servers,
            OAuthSettings oauthSettings)
    {
        webApp.UseAuthorization();
        webApp.MapAuth();

        webApp.Use(async (context, next) =>
        {
            var path = (context.Request.Path.Value ?? "").TrimStart('/');

            if (path.Contains(".well-known", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/widget-"))
            {
                await next();
                return;
            }

            var validator = context.RequestServices.GetRequiredService<IJwtValidator>();
            var oAuthSettings = context.RequestServices.GetRequiredService<OAuthSettings>();
            var matchedServer = servers.FirstOrDefault(a =>
                path.StartsWith($"{a.Server.ServerInfo.Name.ToLowerInvariant()}", StringComparison.OrdinalIgnoreCase));

            if (matchedServer is null)
            {
                await next();
                return;
            }

            var token = context.GetBearerToken();

            if (string.IsNullOrEmpty(token) && matchedServer.Server.OBO?.Count > 0)
            {
                await WriteUnauthorized(context, oAuthSettings);
                return;
            }

            var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
            var jwt = new JwtSecurityToken(token);
            var principal = await validator.ValidateAsync(token!, baseUrl,
                           string.Join(", ", jwt.Audiences), oAuthSettings);

            var roleClaims =
                principal?.FindAll("roles").Select(c => c.Value)
                .Concat(principal.FindAll(ClaimTypes.Role).Select(c => c.Value))
                .Concat(principal.FindAll("role").Select(c => c.Value)) // just in case
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? [];

            var scopeClaims = (principal?.FindFirst("scp")?.Value ?? "")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var userPermissions = roleClaims
                .Union(scopeClaims, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (principal is null && matchedServer.IsAuthorized(context.Request.Headers.ToDictionary(k => k.Key, v => v.Value.ToString()), userPermissions))
            {
                await next();
                return;
            }

            if (principal is null)
            {
                await WriteUnauthorized(context, oAuthSettings);
                return;
            }

            if (matchedServer.SourceType == ServerSourceType.Dynamic && !IsOwnerOrGroupAuthorized(matchedServer, principal))
            {
                context.Response.StatusCode = 403;
                await context.Response.WriteAsync("Forbidden: user is not authorized for this server");
                return;
            }

            var requiredRoles = matchedServer.Server.Roles?.ToList() ?? [];
            if (requiredRoles.Count != 0)
            {
                // Check if the user has at least one required role
                if (!requiredRoles.Intersect(userPermissions, StringComparer.OrdinalIgnoreCase).Any())
                {
                    context.Response.StatusCode = 403;
                    await context.Response.WriteAsync("Forbidden: user lacks required role(s)");
                    return;
                }
            }

            context.User = principal;

            await next();
        });

        foreach (var server in servers)
        {
            webApp.MapServerOAuth("/" + server.Server.ServerInfo.Name.ToLowerInvariant(),
                oauthSettings.Scopes?.Split(" ") ?? []);
        }

        return webApp;
    }

    public static HashSet<string> GetGroupClaims(this ClaimsPrincipal claimsPrincipal)
        => claimsPrincipal.Claims
              .Where(c => c.Type == "groups")
              .Select(c => c.Value)
              .ToHashSet(StringComparer.OrdinalIgnoreCase);

    static bool IsOwnerOrGroupAuthorized(ServerConfig matchedServer, ClaimsPrincipal principal)
    {
        var ownersValid = false;
        var groupsValid = false;

        if (matchedServer.Server.Owners?.Any() == true)
        {
            var oid = principal.Claims.GetUserOid();

            ownersValid = !string.IsNullOrEmpty(oid)
                && matchedServer.Server.Owners.Contains(oid, StringComparer.OrdinalIgnoreCase);
        }

        if (matchedServer.Server.Groups?.Any() == true)
        {
            var userGroups = principal.GetGroupClaims();

            groupsValid = matchedServer.Server.Groups.Any(g => userGroups.Contains(g));
        }

        return ownersValid || groupsValid;
    }

    static async Task WriteUnauthorized(HttpContext context, OAuthSettings oAuthSettings)
    {
        var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
        var resourceMetadata = $"{baseUrl}/.well-known/oauth-protected-resource{context.Request.Path}";
        var authorizationUri = $"{baseUrl}/authorize";

        context.Response.StatusCode = 401;
        context.Response.Headers.Append("WWW-Authenticate",
            $"Bearer resource_metadata=\"{resourceMetadata}\", " +
            $"authorization_uri=\"{authorizationUri}\", " +
            $"resource=\"{oAuthSettings.Audience}\"");

        await context.Response.WriteAsync("Invalid or missing token");
    }
}
