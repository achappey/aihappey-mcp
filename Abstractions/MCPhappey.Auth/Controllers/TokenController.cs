using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using MCPhappey.Auth.Cache;
using MCPhappey.Auth.Models;
using Microsoft.IdentityModel.Tokens;

namespace MCPhappey.Auth.Controllers;

public static class TokenController
{

    public static string CreateJwt(string issuer, string subject, string audience, IEnumerable<string> scopes,
        SigningCredentials signingCredentials,
        IDictionary<string, object>? additionalClaims = null,
        DateTime? expires = null,
        IEnumerable<string>? roles = null
    )
    {
        var handler = new JwtSecurityTokenHandler();
        var claims = new List<Claim>
        {
            new("sub", subject),
            new("iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()),
        };

        if (scopes != null && scopes.Any())
            claims.Add(new Claim("scp", string.Join(" ", scopes)));

        if (roles != null)
        {
            foreach (var r in roles.Where(r => !string.IsNullOrWhiteSpace(r)))
                claims.Add(new Claim("roles", r));
        }


        if (additionalClaims != null)
        {
            foreach (var pair in additionalClaims)
            {
                claims.Add(new Claim(pair.Key, pair.Value.ToString()!));
            }
        }

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expires ?? DateTime.UtcNow.AddHours(1),
            signingCredentials: signingCredentials

        );

        return handler.WriteToken(token);
    }

    public static async Task<IResult> Handle(
        HttpContext ctx,
        IHttpClientFactory httpClientFactory,
        OAuthSettings oauth,
        SigningCredentials signingCredentials)
    {
        var form = await ctx.Request.ReadFormAsync();

        var code = form["code"].ToString();
        var codeVerifier = form["code_verifier"].ToString();
        var grantType = form["grant_type"].ToString();
        var resource = form["resource"].ToString();
        var subjectTok = form["subject_token"].ToString();
        var subjectType = form["subject_token_type"].ToString();
        var actTok = form["act_token"].ToString();

        if (grantType == "urn:ietf:params:oauth:grant-type:token-exchange")
        {
            if (string.IsNullOrEmpty(subjectTok) ||
                subjectType != "urn:ietf:params:oauth:token-type:access_token")
                return Results.BadRequest("Missing or wrong subject_token(_type)");

            var handler111 = new JwtSecurityTokenHandler();

            // STEP 0: Decode token header/payload without validation to check issuer.
            JwtSecurityToken? jwt111 = handler111.ReadJwtToken(subjectTok);

            string? tokenIssuer = jwt111?.Issuer;

            var outerKeys = await JwksCache.GetAsync(
                 $"https://login.microsoftonline.com/{oauth.TenantId}/discovery/v2.0/keys",
                 httpClientFactory);

            var tvp = new TokenValidationParameters
            {
                ValidIssuers =
                    [
                //   issuer, // your own issuer
                    $"https://sts.windows.net/{oauth.TenantId}/",
                    $"https://login.microsoftonline.com/{oauth.TenantId}/v2.0"
                ],
                IssuerSigningKeys = outerKeys,
                //   ValidIssuers = $"https://login.microsoftonline.com/{oauth.TenantId}/v2.0",
                ValidateIssuerSigningKey = true,
                ValidateAudience = false,          // you may want to lock this down
                ValidateLifetime = true
            };

            var handler1 = new JwtSecurityTokenHandler();
            handler1.ValidateToken(subjectTok, tvp, out var validated);
            var jwt1 = (JwtSecurityToken)validated;

            var sub1 = jwt1.Subject;
            var oid1 = jwt1.Claims.First(c => c.Type == "oid").Value;
            var exp1 = jwt1.ValidTo.AddMinutes(-2); // clip a bit

            // 0-b) Choose scopes:  “scope” parameter wins, otherwise default set
            var scopes = form["scope"].ToString();
            var scopeList = string.IsNullOrEmpty(scopes)
                            ? oauth.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                            : scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var roles = jwt1.Claims.Where(c => c.Type == "roles").Select(c => c.Value);
            // 0-c) Mint the MCP token, embedding the inbound token as act
            var mcpToken1 = CreateJwt($"{ctx.Request.Scheme}://{ctx.Request.Host}",
                                     sub1!, resource,
                                     scopeList,
                                     signingCredentials,
                                     new Dictionary<string, object>
                                     {
                                         ["obo"] = subjectTok,   // aud = MCP (for hop-3 OBO)
                                         ["act"] = actTok,
                                         ["oid"] = oid1
                                     }, expires: exp1, roles: roles);

            return Results.Json(new
            {
                access_token = mcpToken1,
                token_type = "Bearer",
                resource,
                expires_in = (int)(exp1 - DateTime.UtcNow).TotalSeconds
            });
        }

        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(codeVerifier))
        {
            return Results.BadRequest("Missing required parameters.");
        }

        // Look up original redirect_uri (saved in /callback)
        var redirectUri = CodeCache.Retrieve(code);
        if (string.IsNullOrEmpty(redirectUri))
        {
            return Results.BadRequest("Unknown or expired code");
        }


        if (grantType == "client_credentials")
        {
            var clientId = form["client_id"].ToString();
            var clientSecret = form["client_secret"].ToString();
            var scope = form["scope"].ToString();

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
                return Results.BadRequest("Missing client_id or client_secret");

            // ✅ Look up client in your confidential clients registry
            if (!oauth.ConfidentialClients?.TryGetValue(clientId, out var conf) == true)
                return Results.BadRequest("Unknown confidential client");

            return Results.Json(new
            {
                access_token = CreateJwt($"{ctx.Request.Scheme}://{ctx.Request.Host}", clientId,
                  //oauth.Audience,
                  resource,
                   oauth.Scopes.Split(" ") ?? [], signingCredentials,
                additionalClaims: new Dictionary<string, object>()),
                token_type = "Bearer",
                resource,
                expires_in = 3600
            });
        }

        // Must match redirect_uri used during /authorize
        var tokenRequest = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = oauth.ClientId,
            ["client_secret"] = oauth.ClientSecret,
            ["code"] = code,
            ["redirect_uri"] = $"{ctx.Request.Scheme}://{ctx.Request.Host}/callback",
            ["code_verifier"] = codeVerifier,
            ["scope"] = string.Join(" ", oauth.Scopes.Split(" ") ?? [])
        };

        var client = httpClientFactory.CreateClient();
        var response = await client.PostAsync(
            $"https://login.microsoftonline.com/{oauth.TenantId}/oauth2/v2.0/token",
            new FormUrlEncodedContent(tokenRequest));

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            return Results.BadRequest(errorBody);
        }

        var azureToken = await response.Content.ReadFromJsonAsync<JsonElement>();
        var azureAccessToken = azureToken.GetProperty("access_token").GetString();

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(azureAccessToken);
        var sub = jwt.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
        var azureExp = jwt.ValidTo;
        var oid = jwt.Claims.FirstOrDefault(c => c.Type == "oid")?.Value;
        // ⏱ Optional: subtract a few minutes to be safe
        var exp = azureExp.AddMinutes(-2);

        var baseUri = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
        var rolesFromSubject = jwt.Claims.Where(c => c.Type == "roles").Select(c => c.Value);
        var mcpToken = CreateJwt(baseUri, sub!, resource, scopes: oauth.Scopes?.Split(" ") ?? [], signingCredentials,
            additionalClaims: new Dictionary<string, object>
            {
                ["act"] = azureAccessToken!,
                ["oid"] = oid!
            }, expires: exp, roles: rolesFromSubject);

        return Results.Json(new
        {
            access_token = mcpToken,
            token_type = "Bearer",
            expires_in = 3600
        });
    }
}