using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using MCPhappey.Auth;
using MCPhappey.Auth.Models;
using Microsoft.IdentityModel.Tokens;

namespace MCPhappey.Tests;

public sealed class JwtValidationTests
{
    [Theory]
    [InlineData("valid", true)]
    [InlineData("wrong-audience", false)]
    [InlineData("expired", false)]
    [InlineData("wrong-signature", false)]
    [InlineData("wrong-issuer", false)]
    [InlineData("malformed", false)]
    public async Task Only_valid_tokens_are_accepted(string scenario, bool expected)
    {
        using var rsa = RSA.Create(2048);
        using var other = RSA.Create(2048);
        var issuer = $"https://{Guid.NewGuid():N}.test";
        var audience = issuer + "/images";
        var oauth = new OAuthSettings { TenantId = Guid.NewGuid().ToString(), Audience = "configured-api" };
        var signingKey = new RsaSecurityKey(rsa) { KeyId = "test" };
        var publicKey = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(rsa.ExportParameters(false)) { KeyId = "test" });
        var factory = new Factory(JsonSerializer.Serialize(new { keys = new[] { publicKey } }));
        var token = scenario == "malformed" ? "sk-not-a-jwt" : new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: scenario == "wrong-issuer" ? issuer + "/untrusted" : issuer,
            audience: scenario == "wrong-audience" ? "untrusted-api" : audience,
            claims: [new Claim("sub", "subject")],
            notBefore: DateTime.UtcNow.AddHours(-2),
            expires: scenario == "expired" ? DateTime.UtcNow.AddHours(-1) : DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(scenario == "wrong-signature"
                ? new RsaSecurityKey(other) { KeyId = "test" } : signingKey, SecurityAlgorithms.RsaSha256)));
        var result = await new JwtValidator(factory).ValidateAsync(token, issuer, audience, oauth);
        Assert.Equal(expected, result is not null);
    }

    [Fact]
    public async Task Signed_wrapper_does_not_gain_actor_roles_or_groups()
    {
        using var rsa = RSA.Create(2048);
        var issuer = $"https://{Guid.NewGuid():N}.test";
        var tenant = Guid.NewGuid().ToString();
        var key = new RsaSecurityKey(rsa) { KeyId = "test" };
        var signing = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);
        var handler = new JwtSecurityTokenHandler();
        var actor = handler.WriteToken(new JwtSecurityToken($"https://login.microsoftonline.com/{tenant}/v2.0", "graph",
            [new Claim("roles", "administrator"), new Claim("groups", "privileged-group")], expires: DateTime.UtcNow.AddHours(1), signingCredentials: signing));
        var wrapper = handler.WriteToken(new JwtSecurityToken(issuer, issuer + "/images",
            [new Claim("sub", "subject"), new Claim("roles", "reader"), new Claim("act", actor)], expires: DateTime.UtcNow.AddHours(1), signingCredentials: signing));
        var publicKey = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(rsa.ExportParameters(false)) { KeyId = "test" });
        var result = await new JwtValidator(new Factory(JsonSerializer.Serialize(new { keys = new[] { publicKey } })))
            .ValidateAsync(wrapper, issuer, issuer + "/images", new OAuthSettings { TenantId = tenant, Audience = "configured-api" });
        Assert.NotNull(result);
        Assert.DoesNotContain(result.Claims, claim => claim.Value is "administrator" or "privileged-group");
        Assert.Contains(result.Claims, claim => claim.Value == "reader");
    }

    private sealed class Factory(string jwks) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(jwks));
    }

    private sealed class Handler(string jwks) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(jwks, System.Text.Encoding.UTF8, "application/json") });
    }
}
