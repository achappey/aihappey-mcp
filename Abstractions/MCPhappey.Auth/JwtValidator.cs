using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MCPhappey.Auth.Cache;
using MCPhappey.Auth.Models;
using Microsoft.IdentityModel.Tokens;

namespace MCPhappey.Auth;

public interface IJwtValidator
{
    Task<ClaimsPrincipal?> ValidateAsync(string token, string issuer,
        string audience, OAuthSettings oAuthSettings);
}

public class JwtValidator(IHttpClientFactory httpClientFactory) : IJwtValidator
{

    public async Task<ClaimsPrincipal?> ValidateAsync(
      string token,
      string issuer,
      string audience,
      OAuthSettings oAuthSettings)
    {
        using var client = httpClientFactory.CreateClient();
        var handler = new JwtSecurityTokenHandler();

        // STEP 0: Decode token header/payload without validation to check issuer.
        JwtSecurityToken? jwt = handler.ReadJwtToken(token);
        string? tokenIssuer = jwt?.Issuer;

        // STEP 1: Pick correct JWKS endpoint based on issuer.
        string jwksUrl;
        if (tokenIssuer != null &&
            (tokenIssuer.StartsWith("https://sts.windows.net/") ||
             tokenIssuer.StartsWith("https://login.microsoftonline.com/")))
        {
            // Azure AD token
            jwksUrl = $"https://login.microsoftonline.com/{oAuthSettings.TenantId}/discovery/v2.0/keys";
        }
        else
        {
            // Your own tokens
            jwksUrl = $"{issuer}/.well-known/jwks.json";
        }

        var keys = await JwksCache.GetAsync(jwksUrl, httpClientFactory);
        if (keys == null || keys.Count == 0) return null;

        // STEP 2: Build validation parameters
        var outerValidationParameters = new TokenValidationParameters
        {
            ValidIssuers =
            [
            issuer, // your own issuer
            $"https://sts.windows.net/{oAuthSettings.TenantId}/",
            $"https://login.microsoftonline.com/{oAuthSettings.TenantId}/v2.0"
        ],
            IssuerSigningKeys = keys,
            ValidateIssuer = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            ValidateAudience = true,
            AudienceValidator = (tokenAudiences, _, _) =>
                tokenAudiences.Contains(audience, StringComparer.OrdinalIgnoreCase)
        };

        try
        {
            var outerResult = await handler.ValidateTokenAsync(token, outerValidationParameters);

            if (!outerResult.IsValid)
            {

            }

            var outerIdentity = outerResult.ClaimsIdentity;
            var principal = new ClaimsPrincipal(outerIdentity);

            // STEP 2: Check if token has embedded Azure token in `act` claim
            var actToken = outerIdentity.FindFirst("act")?.Value;

            if (!string.IsNullOrEmpty(actToken))
            {
                var innerJwt = handler.ReadJwtToken(actToken);
                var innerIssuer = innerJwt.Issuer;

                var innerKeys = await JwksCache.GetAsync($"https://login.microsoftonline.com/{oAuthSettings.TenantId}/discovery/v2.0/keys", httpClientFactory);
                if (innerKeys == null || innerKeys.Count == 0) return null;

                var innerValidation = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuers =
                    [
                        $"https://sts.windows.net/{oAuthSettings.TenantId}/",
                        $"https://login.microsoftonline.com/{oAuthSettings.TenantId}/v2.0"
                    ],
                    IssuerSigningKeys = innerKeys,
                    ValidateLifetime = true,
                    //ValidateAudience = true,
                    ValidateAudience = false,
                    // OR, if you want a soft allow-list:
                    //    AudienceValidator = (audiences, _, _) =>
                    //       audiences.Any(aud =>
                    //          aud.Equals(oAuthSettings.Audience, StringComparison.OrdinalIgnoreCase) ||
                    //         aud.Equals("api://e1fc4277-83bd-43c6-b3a8-83377e942f2f", StringComparison.OrdinalIgnoreCase))
                    //AudienceValidator = (tokenAudiences, _, _) =>
                    //   tokenAudiences.Contains(oAuthSettings.Audience, StringComparer.OrdinalIgnoreCase) == true
                };

                var innerResult = await handler.ValidateTokenAsync(actToken, innerValidation);
                var innerIdentity = innerResult.ClaimsIdentity;

                outerIdentity.AddClaims(innerIdentity.Claims
                    .Where(c => !outerIdentity.HasClaim(c.Type, c.Value))); // avoid duplicates
            }

            return principal;

        }
        catch
        {
            return null;
        }
    }
}
