using System.IdentityModel.Tokens.Jwt;
using MCPhappey.Auth.Controllers;
using MCPhappey.Common;

namespace MCPhappey.Auth.Extensions;

public static class AuthEndpoints
{
    public static void MapAuth(this WebApplication app)
    {
        app.MapGet("/authorize", AuthorizationController.Handle);
        app.MapPost("/token", TokenController.Handle);
        app.MapGet("/callback", (Delegate)CallbackController.Handle);
        app.MapPost("/register", (Delegate)RegisterController.Handle);
        app.MapGet("/.well-known/jwks.json", JwksController.Handle);
        app.MapGet("/.well-known/oauth-authorization-server", AuthorizationServerMetadataController.Handle);
    }

    public static string? GetOidClaim(string jwt) => GetClaimValue(jwt, "oid");
    public static string? GetNameClaim(string jwt) => GetClaimValue(jwt, "name");

    public static string? GetClaimValue(string jwt, string claim)
    {
        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(jwt);

        return token.Claims.FirstOrDefault(c => c.Type == claim)?.Value;
    }

    public static string? GetNameClaim(this HeaderProvider? tokenProvider)
    {
        if (string.IsNullOrEmpty(tokenProvider?.Bearer))
            return null;

        return GetNameClaim(tokenProvider.Bearer);
    }

    public static string? GetOidClaim(this HeaderProvider? tokenProvider)
    {
        if (string.IsNullOrEmpty(tokenProvider?.Bearer))
            return null;

        return GetOidClaim(tokenProvider.Bearer);
    }
}
