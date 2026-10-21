using System.Net;
using System.Text;
using System.Text.Json;
using MCPhappey.Auth.Cache;
using MCPhappey.Auth.Controllers;
using MCPhappey.Auth.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace MCPhappey.Tests;

public sealed class ConfidentialClientTokenTests
{
    [Fact]
    public async Task Client_credentials_rejects_a_wrong_secret_even_with_a_cached_code()
    {
        var code = $"test-code-{Guid.NewGuid():N}";
        CodeCache.Store(code, "https://client.example.test/callback");

        var (statusCode, _) = await RequestTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "confidential-client",
            ["client_secret"] = "wrong-secret",
            ["code"] = code,
            ["code_verifier"] = "attacker-controlled-verifier",
            ["resource"] = "https://mcp.example.test/server"
        });

        Assert.Equal(HttpStatusCode.BadRequest, (HttpStatusCode)statusCode);
    }

    [Fact]
    public async Task Client_credentials_accepts_a_configured_secret_without_an_authorization_code()
    {
        var (statusCode, responseBody) = await RequestTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "confidential-client",
            ["client_secret"] = "configured-secret",
            ["resource"] = "https://mcp.example.test/server"
        });

        Assert.Equal(HttpStatusCode.OK, (HttpStatusCode)statusCode);
        using var response = JsonDocument.Parse(responseBody);
        Assert.False(string.IsNullOrEmpty(response.RootElement.GetProperty("access_token").GetString()));
    }

    private static async Task<(int StatusCode, string Body)> RequestTokenAsync(
        Dictionary<string, string> formValues)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("mcp.example.test");
        context.Request.ContentType = "application/x-www-form-urlencoded";
        var body = string.Join("&", formValues.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        context.Request.Body = new MemoryStream(bodyBytes);
        context.Request.ContentLength = bodyBytes.Length;
        context.Response.Body = new MemoryStream();
        using var services = new ServiceCollection()
            .AddLogging()
            .ConfigureHttpJsonOptions(_ => { })
            .BuildServiceProvider();
        context.RequestServices = services;

        var settings = new OAuthSettings
        {
            Scopes = "mcp.read mcp.write",
            ConfidentialClients = new Dictionary<string, ConfidentialClientSettings>
            {
                ["confidential-client"] = new() { ClientSecrets = ["configured-secret"] }
            }
        };
        var signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes("test-signing-key-32-bytes-long!!")),
            SecurityAlgorithms.HmacSha256);

        var result = await TokenController.Handle(
            context, new UnusedHttpClientFactory(), settings, signingCredentials);
        await result.ExecuteAsync(context);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        return (context.Response.StatusCode, await reader.ReadToEndAsync());
    }

    private sealed class UnusedHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            throw new InvalidOperationException("The client-credentials flow must not call an external endpoint.");
    }
}
