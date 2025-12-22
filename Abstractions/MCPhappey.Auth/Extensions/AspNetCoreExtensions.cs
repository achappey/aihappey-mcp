
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace MCPhappey.Auth.Extensions;

public static class AspNetCoreExtensions
{
    public static IServiceCollection AddAuthServices(
        this WebApplicationBuilder builder,
        string privateKey)
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem($"-----BEGIN PRIVATE KEY-----\n{privateKey}\n-----END PRIVATE KEY-----");
        var key = new RsaSecurityKey(rsa) { KeyId = "mcp-keyId" };
        var creds = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);

        builder.Services.AddSingleton(creds);
        builder.Services.AddSingleton(key);
        builder.Services.AddAuthorization();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<IJwtValidator, JwtValidator>();

        builder.Services.AddHttpClient();

        return builder.Services;
    }
}