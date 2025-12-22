using MCPhappey.Auth.Models;

namespace MCPhappey.Auth.Controllers;

public static class AuthorizationServerMetadataController
{
    public static IResult Handle(HttpContext ctx, OAuthSettings oAuthSettings)
    {
        var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";

        var metadata = new
        {
            issuer = baseUrl,
            authorization_endpoint = $"{baseUrl}/authorize",
            token_endpoint = $"{baseUrl}/token",
            registration_endpoint = $"{baseUrl}/register",
            scopes_supported = oAuthSettings.Scopes.Split(" "),
            response_types_supported = new[] { "code" },
            grant_types_supported = new[] { "authorization_code", "refresh_token" },
            code_challenge_methods_supported = new[] { "S256" },
            token_endpoint_auth_methods_supported = new[] { "none" }
        };

        return Results.Json(metadata);
    }
}
