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
        webApp.MapAuth();
        // Transport authorization is enforced by the shared configuration-driven core policy.

        return webApp;
    }

    public static HashSet<string> GetGroupClaims(this ClaimsPrincipal claimsPrincipal)
        => claimsPrincipal.Claims
              .Where(c => c.Type == "groups")
              .Select(c => c.Value)
              .ToHashSet(StringComparer.OrdinalIgnoreCase);

}
