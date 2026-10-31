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
        try
        {
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            if (!handler.CanReadToken(token))
                return null;
            // Unvalidated contents may only select between fixed, trusted issuers/key endpoints.
            var tokenIssuer = handler.ReadJwtToken(token).Issuer;
            var azureIssuers = new[] { $"https://sts.windows.net/{oAuthSettings.TenantId}/",
                $"https://login.microsoftonline.com/{oAuthSettings.TenantId}/v2.0" };
            var azure = azureIssuers.Contains(tokenIssuer, StringComparer.Ordinal);
            if (!azure && !string.Equals(tokenIssuer, issuer, StringComparison.Ordinal))
                return null;
            var keys = await JwksCache.GetAsync(azure
                ? $"https://login.microsoftonline.com/{oAuthSettings.TenantId}/discovery/v2.0/keys"
                : $"{issuer}/.well-known/jwks.json", httpClientFactory);
            if (keys is null || keys.Count == 0)
                return null;
            var result = await handler.ValidateTokenAsync(token, new TokenValidationParameters
            {
                ValidIssuers = azure ? azureIssuers : [issuer],
                ValidAudiences = azure ? [oAuthSettings.Audience, .. oAuthSettings.AdditionalAudiences]
                    : [audience, oAuthSettings.Audience, .. oAuthSettings.AdditionalAudiences],
                IssuerSigningKeys = keys,
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                RequireSignedTokens = true,
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256, SecurityAlgorithms.RsaSha384, SecurityAlgorithms.RsaSha512],
                ClockSkew = TimeSpan.FromMinutes(1)
            });
            // act/obo are assertions for downstream exchange, not sources of authorization claims.
            // Authorize only the validated subject's claims. Existing minting already copies subject roles/groups.
            return result.IsValid && result.ClaimsIdentity is { IsAuthenticated: true } identity
                ? new ClaimsPrincipal(identity) : null;
        }
        catch
        {
            return null;
        }
    }
}
