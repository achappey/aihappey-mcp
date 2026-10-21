using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using MCPhappey.Auth;
using MCPhappey.Auth.Extensions;
using MCPhappey.Auth.Models;
using MCPhappey.Common.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace MCPhappey.Tests;

public sealed class ProtectedResourceAudienceTests
{
    [Fact]
    public async Task Protected_server_validation_uses_the_requested_server_resource_as_audience()
    {
        var validator = new RecordingJwtValidator();
        var settings = new OAuthSettings { Scopes = "mcp.read" };
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IJwtValidator>(validator);
        builder.Services.AddSingleton(settings);

        await using var app = builder.Build();
        app.UseRouting();
        app.MapOAuth(
        [
            new ServerConfig
            {
                Server = new MCPhappey.Common.Models.Server
                {
                    ServerInfo = new ServerInfo { Name = "server-a" },
                    OBO = new Dictionary<string, string> { ["graph.microsoft.com"] = "mcp.read" },
                    Roles = ["required"]
                }
            }
        ], settings);
        app.Run(_ => Task.CompletedTask);

        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{address}/server-a");
            var unrelatedAudienceToken = new JwtSecurityTokenHandler().WriteToken(
                new JwtSecurityToken(audience: "https://mcp.example.test/server-b"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", unrelatedAudienceToken);

            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal($"{address.TrimEnd('/')}/server-a", validator.Audience);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private sealed class RecordingJwtValidator : IJwtValidator
    {
        public string? Audience { get; private set; }

        public Task<ClaimsPrincipal?> ValidateAsync(
            string token,
            string issuer,
            string audience,
            OAuthSettings oAuthSettings)
        {
            Audience = audience;
            return Task.FromResult<ClaimsPrincipal?>(null);
        }
    }
}
