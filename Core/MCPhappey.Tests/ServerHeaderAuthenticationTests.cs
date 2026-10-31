using System.Net;
using System.Security.Claims;
using System.Text.Json;
using MCPhappey.Auth;
using MCPhappey.Auth.Models;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Servers.JSON;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace MCPhappey.Tests;

public sealed class ServerHeaderAuthenticationTests
{
    internal static ServerConfig ImageServer(string variable = "openai_api_key") => new()
    {
        Server = new()
        {
            ServerInfo = new() { Name = "Images", Version = "1.0.0" },
            OBO = new() { ["graph.microsoft.com"] = "https://graph.microsoft.com/Sites.ReadWrite.All" },
            Headers = [new() { Name = "Authorization", Value = $"Bearer {{{variable}}}", IsRequired = true,
                Variables = new() { [variable] = new() { IsRequired = true, IsSecret = true, Placeholder = "API key" } } }]
        }
    };

    internal static OAuthSettings OAuth() => new()
    {
        TenantId = "test-tenant", ClientId = "test-client", ClientSecret = "operator-secret",
        Audience = "configured-api", Scopes = "user.read"
    };

    [Fact]
    public void Obo_declarations_remain_in_one_definition_and_only_activate_with_application_OAuth()
    {
        var config = ImageServer();
        var obo = config.Server.OBO;
        Assert.Equal(ServerAccessMode.Headers, new ServerAccessPolicy().GetMode(config.Server));
        Assert.Equal(ServerAccessMode.Delegated, new ServerAccessPolicy(OAuth()).GetMode(config.Server));
        Assert.Same(obo, config.Server.OBO);
        Assert.Single(obo!);
        Assert.DoesNotContain("directAccess", JsonSerializer.Serialize(config.Server));
        Assert.Throws<InvalidOperationException>(() => new ServerAccessPolicy(new OAuthSettings()));
    }

    [Fact]
    public void Header_array_round_trips_all_registry_fields()
    {
        const string json = """{"headers":[{"name":"X-Key","value":"prefix:{key}","description":"provider","format":"string","isRequired":true,"isSecret":true,"placeholder":"guidance","choices":["one","two"],"default":"one","variables":{"key":{"description":"key","format":"string","isRequired":true,"isSecret":true,"placeholder":"secret","choices":["one","two"],"default":"one","value":"one"}}}]}""";
        var server = JsonSerializer.Deserialize<Common.Models.Server>(json)!;
        var actual = JsonSerializer.Serialize(server);
        var header = JsonDocument.Parse(actual).RootElement.GetProperty("headers")[0];
        Assert.Equal("X-Key", header.GetProperty("name").GetString());
        Assert.Equal("one", header.GetProperty("default").GetString());
        Assert.Equal("guidance", header.GetProperty("placeholder").GetString());
        Assert.Equal(2, header.GetProperty("variables").GetProperty("key").GetProperty("choices").GetArrayLength());
    }

    [Theory]
    [InlineData("{\"headers\":{\"Authorization\":\"old guidance\"}}")]
    [InlineData("{\"headers\":[{\"name\":\"Authorization\"},{\"name\":\"authorization\"}]}")]
    [InlineData("{\"headers\":[{\"name\":\"Authorization\",\"value\":\"Bearer {missing}\"}]}")]
    [InlineData("{\"headers\":[{\"name\":\"bad header\"}]}")]
    [InlineData("{\"headers\":[{\"name\":\"X-Key\",\"format\":\"unsupported\"}]}")]
    public void Invalid_header_definitions_are_rejected(string json)
        => Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Common.Models.Server>(json));

    [Fact]
    public void Invalid_legacy_server_is_skipped_rather_than_becoming_public()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var directory = Directory.CreateDirectory(Path.Combine(root, "Invalid"));
        try
        {
            File.WriteAllText(Path.Combine(directory.FullName, "Server.json"), """{"serverInfo":{"name":"Invalid","version":"1"},"headers":{"Authorization":"old"}}""");
            Assert.Empty(root.GetServers());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("Bearer caller-key", "caller-key")]
    [InlineData("Basic caller-key", null)]
    [InlineData("Bearer {openai_api_key}", null)]
    [InlineData("Bearer ", null)]
    public void Credentials_are_extracted_from_the_declared_template(string header, string? expected)
    {
        var server = ImageServer().Server;
        var policy = new ServerAccessPolicy();
        var headers = new Dictionary<string, string> { ["authorization"] = header };
        if (expected is null)
            Assert.Throws<UnauthorizedAccessException>(() => policy.GetCredential(server, headers, "openai_api_key"));
        else
            Assert.Equal(expected, policy.GetCredential(server, headers, "openai_api_key"));
        Assert.Throws<UnauthorizedAccessException>(() => new ServerAccessPolicy(OAuth()).GetCredential(server, headers, "openai_api_key"));
    }

    [Fact]
    public void Discovery_projects_effective_headers_without_mutating_config_or_exposing_secrets()
    {
        var config = ImageServer();
        config.Server.Headers![0].Variables!["openai_api_key"].Value = "caller-secret";
        config.Server.Headers[0].Variables!["openai_api_key"].Default = "default-secret";
        var direct = config.ToMCPServerRegistryItem("https://mcp.test", [], new ServerAccessPolicy());
        var delegated = config.ToMCPServerRegistryItem("https://mcp.test", [], new ServerAccessPolicy(OAuth()));
        var directJson = JsonSerializer.Serialize(direct);
        var delegatedJson = JsonSerializer.Serialize(delegated);
        Assert.Contains("openai_api_key", directJson);
        Assert.DoesNotContain("caller-secret", directJson);
        Assert.DoesNotContain("default-secret", directJson);
        Assert.DoesNotContain("openai_api_key", delegatedJson);
        Assert.DoesNotContain("operator-secret", delegatedJson);
        Assert.Contains("io.aihappey/auth", delegatedJson);
        Assert.Contains("access_token", delegatedJson);
        Assert.Equal("caller-secret", config.Server.Headers[0].Variables!["openai_api_key"].Value);
        Assert.Contains("access_token", config.ToMcpServer("https://mcp.test", new ServerAccessPolicy(OAuth())).Headers!["Authorization"]);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Caller_key_connects_only_when_delegated_authentication_is_not_active(bool oauthConfigured, bool allowed)
    {
        var config = ImageServer();
        var validator = new Validator(null);
        using var services = new ServiceCollection().AddSingleton<IJwtValidator>(validator).BuildServiceProvider();
        var context = Context(services, "Bearer caller-key");
        var reached = false;
        await new ServerAccessMiddleware(_ => { reached = true; return Task.CompletedTask; }, new ServerAccessPolicy(oauthConfigured ? OAuth() : null))
            .InvokeAsync(context, [config]);
        Assert.Equal(allowed, reached);
        Assert.Equal(oauthConfigured ? 1 : 0, validator.Calls);
        Assert.Equal(allowed ? 200 : 401, context.Response.StatusCode);
        if (!allowed)
            Assert.Contains("/.well-known/oauth-protected-resource/images", context.Response.Headers.WWWAuthenticate.ToString());
        else
        {
            Assert.False(context.User.Identity?.IsAuthenticated == true);
            Assert.Null(context.User.FindFirst(ClaimTypes.NameIdentifier));
            Assert.Equal(ServerAccessMode.Headers, ((ServerRequestAccess)context.Items[typeof(ServerRequestAccess)]!).Mode);
        }
    }

    [Theory]
    [InlineData("missing", false, 403)]
    [InlineData("required", false, 200)]
    [InlineData("required", true, 403)]
    public async Task Validated_subject_still_needs_roles_and_dynamic_ownership(string role, bool dynamicServer, int status)
    {
        var config = ImageServer();
        config.Server.Roles = ["required"];
        config = new ServerConfig { Server = config.Server, SourceType = dynamicServer ? ServerSourceType.Dynamic : ServerSourceType.Static };
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "subject"), new Claim("roles", role)], "validated"));
        using var services = new ServiceCollection().AddSingleton<IJwtValidator>(new Validator(principal)).BuildServiceProvider();
        var context = Context(services, "Bearer valid-test-token");
        await new ServerAccessMiddleware(_ => Task.CompletedTask, new ServerAccessPolicy(OAuth())).InvokeAsync(context, [config]);
        Assert.Equal(status, context.Response.StatusCode);
    }

    [Fact]
    public async Task Routing_is_exact_and_each_request_keeps_its_own_credentials_without_an_identity()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var config = ImageServer();
        var middleware = new ServerAccessMiddleware(_ => Task.CompletedTask, new ServerAccessPolicy());
        var first = Context(services, "Bearer first");
        var second = Context(services, "Bearer second");
        await middleware.InvokeAsync(first, [config]);
        await middleware.InvokeAsync(second, [config]);
        Assert.Equal("Bearer first", ((ServerRequestAccess)first.Items[typeof(ServerRequestAccess)]!).Headers["Authorization"]);
        Assert.Equal("Bearer second", ((ServerRequestAccess)second.Items[typeof(ServerRequestAccess)]!).Headers["Authorization"]);
        Assert.Null(first.User.FindFirst(ClaimTypes.NameIdentifier));
        Assert.Null(second.User.FindFirst(ClaimTypes.NameIdentifier));
        var other = Context(services, "");
        other.Request.Path = "/images-other";
        await middleware.InvokeAsync(other, [config]);
        Assert.False(other.Items.ContainsKey(typeof(ServerRequestAccess)));
    }

    private static DefaultHttpContext Context(IServiceProvider services, string authorization)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("mcp.test");
        context.Request.Path = "/images";
        context.Request.Headers.Authorization = authorization;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private sealed class Validator(ClaimsPrincipal? principal) : IJwtValidator
    {
        public int Calls { get; private set; }
        public Task<ClaimsPrincipal?> ValidateAsync(string token, string issuer, string audience, OAuthSettings settings)
        {
            Calls++;
            Assert.Equal("https://mcp.test/images", audience);
            return Task.FromResult(principal);
        }
    }
}
