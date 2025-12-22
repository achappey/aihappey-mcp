using Microsoft.IdentityModel.Tokens;

namespace MCPhappey.Auth.Controllers;

public static class JwksController
{
    public static IResult Handle(RsaSecurityKey key)
    {
        // Export public RSA parameters
        var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(key);

        // Return in standard JWKS format
        return Results.Json(new { keys = new[] { jwk } });
    }
}
