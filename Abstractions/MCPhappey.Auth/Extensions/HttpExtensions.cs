using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Text.Json;
using MCPhappey.Auth.Models;
using MCPhappey.Common;
using MCPhappey.Common.Extensions;
using MCPhappey.Common.Models;

namespace MCPhappey.Auth.Extensions;

public static class HttpExtensions
{

  public static string? GetBearerToken(this HttpContext httpContext)
    => httpContext.Request.Headers.Authorization.ToString()?.GetBearerToken();

  public static string? GetBearerToken(this string value)
      => value.Split(" ").LastOrDefault();

  public static string? GetActClaimFromMcpToken(string rawMcpToken)
  {
    var handler = new JwtSecurityTokenHandler();

    // Decode the token (without validating signature)
    var jwt = handler.ReadJwtToken(rawMcpToken);

    // Extract the 'act' claim
    return jwt.Claims.FirstOrDefault(c => c.Type == "act")?.Value;
  }

  public static string? GetOboClaimFromMcpToken(string rawMcpToken)
  {
    var handler = new JwtSecurityTokenHandler();

    // Decode the token (without validating signature)
    var jwt = handler.ReadJwtToken(rawMcpToken);

    // Extract the 'act' claim
    return jwt.Claims.FirstOrDefault(c => c.Type == "obo")?.Value;
  }

  public static async Task<(string accessToken, int expiresIn)> ExchangeOnBehalfOfTokenAsync(this IHttpClientFactory httpClientFactory,
     string incomingAccessToken,
     string clientId,
     string clientSecret,
     string tokenEndpoint,
     string[] scopes)
  {
    using var http = httpClientFactory.CreateClient();
    string? azureAccessToken = GetActClaimFromMcpToken(incomingAccessToken);
    string? oboToken = GetOboClaimFromMcpToken(incomingAccessToken);

    var assertion = oboToken ?? azureAccessToken;

    if (string.IsNullOrEmpty(assertion))
      throw new Exception("No assertion claim found in MCP token");

    var body = new Dictionary<string, string>
            {
                { "client_id", clientId },
                { "client_secret", clientSecret },
                { "grant_type", "urn:ietf:params:oauth:grant-type:jwt-bearer" },
                { "requested_token_use", "on_behalf_of" },
                { "assertion", assertion },
                { "scope", string.Join(" ", scopes) }
            };

    var response = await http.PostAsync(tokenEndpoint, new FormUrlEncodedContent(body));

    if (!response.IsSuccessStatusCode)
    {
      var error = await response.Content.ReadAsStringAsync();
      throw new InvalidOperationException($"OBO token exchange failed: {error}");
    }

    var json = await response.Content.ReadFromJsonAsync<JsonElement>();

    return (
            json.GetProperty("access_token").GetString()!,
            json.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600
        );
  }

  public static string? GetUserId(this IServiceProvider serviceProvider)
  {
    var token = serviceProvider.GetRequiredService<HeaderProvider>();
    if (string.IsNullOrEmpty(token.Bearer)) return null;
    var outerJwt = new JwtSecurityTokenHandler().ReadJwtToken(token.Bearer);

    return outerJwt.Claims.GetUserOid();
  }

  public static async Task<HttpClient> GetOboHttpClient(this IHttpClientFactory httpClientFactory,
     string token,
     string host,
     Server server,
     OAuthSettings oAuthSettings)
  {
    var delegated = await httpClientFactory.GetOboToken(token, host, server, oAuthSettings);

    var httpClient = httpClientFactory.CreateClient();
    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", delegated);
    return httpClient;
  }

  public static async Task<string> GetOboToken(this IHttpClientFactory httpClientFactory,
      string token,
      string host,
      Server server,
      OAuthSettings oAuthSettings)
  {
    if (string.IsNullOrWhiteSpace(host))
      throw new ArgumentNullException(nameof(host));

    // Match obo key by suffix
    var oboKey = server.OBO?.Keys
        .FirstOrDefault(k => host.EndsWith(k, StringComparison.OrdinalIgnoreCase));

    if (oboKey == null || server.OBO == null || !server.OBO.TryGetValue(oboKey, out var rawScopeString))
      throw new UnauthorizedAccessException($"No OBO config for host: {host}");

    var scopes = rawScopeString
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .ToArray();

    // Build cache key per (user/token + host + scope)
    var cacheKey = BuildCacheKey(token, host, scopes);

    // Check static cache
    if (_cache.TryGetValue(cacheKey, out var entry))
    {
      if (DateTime.UtcNow < entry.ExpiresAt)
        return entry.Token; // still valid
      else
        _cache.TryRemove(cacheKey, out _); // expired → remove
    }

    var (accessToken, expiresIn) = await httpClientFactory.ExchangeOnBehalfOfTokenAsync(
         token,
         oAuthSettings.ClientId,
         oAuthSettings.ClientSecret,
         $"https://login.microsoftonline.com/{oAuthSettings.TenantId}/oauth2/v2.0/token",
         scopes);

    var expiresAt = DateTime.UtcNow.AddSeconds(expiresIn - 60); // refresh 1 min early
    _cache[cacheKey] = (accessToken, expiresAt);

    return accessToken ?? throw new UnauthorizedAccessException("Failed to get delegated token.");
  }

  private static string BuildCacheKey(string token, string host, string[] scopes)
  {
    var tokenHash = GetTokenHash(token);
    return $"{host}::{string.Join('+', scopes)}::{tokenHash}";
  }

  private static string GetTokenHash(string token)
  {
    using var sha = System.Security.Cryptography.SHA256.Create();
    var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(token));
    return Convert.ToBase64String(bytes)[..12]; // short hash
  }

  private static readonly ConcurrentDictionary<string, (string Token, DateTime ExpiresAt)> _cache
          = new();

  /*
    public static async Task<string> GetOboToken2(this IHttpClientFactory httpClientFactory,
      string token,
      string host,
      Server server,
      OAuthSettings oAuthSettings)
    {
      if (server.OBO?.ContainsKey(host) == true)
      {
        var delegated = await httpClientFactory.ExchangeOnBehalfOfTokenAsync(token,
                    oAuthSettings.ClientId, oAuthSettings.ClientSecret,
                    $"https://login.microsoftonline.com/{oAuthSettings.TenantId}/oauth2/v2.0/token",
                    server.OBO.GetScopes(host)?.ToArray() ?? []);

        return delegated ?? throw new UnauthorizedAccessException();
      }

      throw new UnauthorizedAccessException();
    }
  */
  public static string? GetObjectId(this HttpContext context)
  {
    return context.User?.FindFirst("oid")?.Value;
  }

  public static IEnumerable<string>? GetScopes(this Dictionary<string, string>? metadata, string host)
       => metadata?.ContainsKey(host) == true ? metadata[host].ToString().Split(" ") : null;

}