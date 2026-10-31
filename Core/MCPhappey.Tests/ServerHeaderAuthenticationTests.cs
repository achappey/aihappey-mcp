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
        config.Server.Headers.Add(new() { Name = "X-Api-Key", IsSecret = true, Value = "literal-secret", Default = "literal-default" });
        var original = JsonSerializer.Serialize(config.Server);
        var direct = config.ToMCPServerRegistryItem("https://mcp.test", [], new ServerAccessPolicy());
        var delegated = config.ToMCPServerRegistryItem("https://mcp.test", [], new ServerAccessPolicy(OAuth()));
        var directJson = JsonSerializer.Serialize(direct);
        var delegatedJson = JsonSerializer.Serialize(delegated);
        Assert.Contains("openai_api_key", directJson);
        foreach (var json in new[] { directJson, delegatedJson })
            foreach (var secret in new[] { "caller-secret", "default-secret", "literal-secret", "literal-default", "operator-secret" })
                Assert.DoesNotContain(secret, json);
        Assert.DoesNotContain("openai_api_key", delegatedJson);
        Assert.True(JsonSerializer.SerializeToElement(delegated).GetProperty("server").GetProperty("_meta")
            .GetProperty("io.modelcontextprotocol.registry/publisher-provided").TryGetProperty("test.mcp/auth", out _));
        Assert.Contains("access_token", delegatedJson);
        Assert.Equal(original, JsonSerializer.Serialize(config.Server));
        Assert.Contains("access_token", config.ToMcpServer("https://mcp.test", new ServerAccessPolicy(OAuth())).Headers!["Authorization"]);
    }

    [Theory]
    [InlineData("https://mcp.test", "test.mcp/auth")]
    [InlineData("https://api.widgets.example", "example.widgets.api/auth")]
    [InlineData("http://edge.tools.example:8080/published", "example.tools.edge/auth")]
    [InlineData("https://custom-registry.internal:9443", "internal.custom-registry/auth")]
    [InlineData("https://mcp.test:8443", "test.mcp/auth")]
    [InlineData("http://localhost", "localhost/auth")]
    [InlineData("http://localhost:5050", "localhost/auth")]
    public void Registry_OAuth_hint_is_only_in_the_official_server_container_under_the_remote_hostname(string baseUrl, string authNamespace)
    {
        var oauth = OAuth();
        var item = ImageServer().ToMCPServerRegistryItem(baseUrl, [], new ServerAccessPolicy(oauth));
        var root = JsonSerializer.SerializeToElement(item);
        Assert.Null(item.Meta);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("_meta").ValueKind);
        var server = root.GetProperty("server");
        var remote = Assert.Single(server.GetProperty("remotes").EnumerateArray());
        Assert.Equal($"{baseUrl}/images", remote.GetProperty("url").GetString());
        var container = Assert.Single(server.GetProperty("_meta").EnumerateObject());
        Assert.Equal("io.modelcontextprotocol.registry/publisher-provided", container.Name);
        var publisher = Assert.Single(container.Value.EnumerateObject());
        Assert.Equal(authNamespace, publisher.Name);
        var hint = publisher.Value;
        Assert.Equal(6, hint.EnumerateObject().Count());
        Assert.Equal("oauth2", hint.GetProperty("type").GetString());
        Assert.Equal("microsoft-entra-id", hint.GetProperty("provider").GetString());
        Assert.Equal($"{baseUrl}/.well-known/oauth-protected-resource/images", hint.GetProperty("resourceMetadataUrl").GetString());
        Assert.Equal(oauth.TenantId, hint.GetProperty("tenantId").GetString());
        Assert.Equal(oauth.ClientId, hint.GetProperty("clientId").GetString());
        Assert.Equal(oauth.Audience, hint.GetProperty("audience").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Registry_entry_metadata_is_preserved_independently_of_server_OAuth_hints(bool oauthConfigured)
    {
        var config = new ServerConfig { Server = ImageServer().Server, SourceType = ServerSourceType.Dynamic };
        config.Server.Owners = ["owner", "unknown"];
        var policy = new ServerAccessPolicy(oauthConfigured ? OAuth() : null);
        var item = config.ToMCPServerRegistryItem("https://registry.widgets.example", [new() { Id = "owner", DisplayName = "Owner", Mail = "owner@example.test" }], policy);
        var root = JsonSerializer.SerializeToElement(item);
        var entry = Assert.Single(root.GetProperty("_meta").EnumerateObject());
        Assert.Equal("example.widgets.registry/registry", entry.Name);
        Assert.Equal(3, entry.Value.EnumerateObject().Count());
        Assert.Equal("active", entry.Value.GetProperty("status").GetString());
        Assert.True(entry.Value.GetProperty("isLatest").GetBoolean());
        var author = Assert.Single(entry.Value.GetProperty("authors").EnumerateArray());
        Assert.Equal(2, author.EnumerateObject().Count());
        Assert.Equal("Owner", author.GetProperty("name").GetString());
        Assert.Equal("owner@example.test", author.GetProperty("email").GetString());
        Assert.Equal(oauthConfigured, root.GetProperty("server").TryGetProperty("_meta", out _));
    }

    [Theory]
    [InlineData(true, true, true, false, ServerAccessMode.Delegated)]
    [InlineData(true, true, false, false, ServerAccessMode.Delegated)]
    [InlineData(false, true, true, false, ServerAccessMode.Headers)]
    [InlineData(true, false, true, false, ServerAccessMode.Headers)]
    [InlineData(true, false, false, false, ServerAccessMode.Public)]
    [InlineData(false, false, false, false, ServerAccessMode.Public)]
    [InlineData(false, true, false, false, ServerAccessMode.Unavailable)]
    [InlineData(true, false, true, true, ServerAccessMode.Delegated)]
    [InlineData(false, false, true, true, ServerAccessMode.Unavailable)]
    public void Registry_hints_follow_existing_policy_without_changing_access_or_configuration(
        bool oauthConfigured, bool hasObo, bool hasHeaders, bool restricted, ServerAccessMode expected)
    {
        var config = ImageServer();
        if (!hasObo) config.Server.OBO = null;
        if (!hasHeaders) config.Server.Headers = null;
        if (restricted) config.Server.Roles = ["required"];
        var original = JsonSerializer.Serialize(config.Server);
        var policy = new ServerAccessPolicy(oauthConfigured ? OAuth() : null);
        var server = config.ToRegistryServer("https://arbitrary.publisher.test:8443", policy);
        Assert.Equal(expected == ServerAccessMode.Delegated, server.Meta is not null);
        Assert.Equal(JsonSerializer.Serialize(policy.GetPublicHeaders(config.Server)),
            JsonSerializer.Serialize(Assert.Single(server.Remotes!).Headers));
        var registry = new[] { config }.ToMcpServerRegistry("https://arbitrary.publisher.test:8443", [], accessPolicy: policy);
        Assert.Equal(expected == ServerAccessMode.Unavailable ? 0 : 1, registry.Metadata.Count);
        Assert.Equal(registry.Metadata.Count, registry.Servers.Count());
        var headers = new Dictionary<string, string> { ["Authorization"] = "Bearer caller-key" };
        if (expected == ServerAccessMode.Headers)
            Assert.Equal("caller-key", policy.GetCredential(config.Server, headers, "openai_api_key"));
        else
            Assert.Throws<UnauthorizedAccessException>(() => policy.GetCredential(config.Server, headers, "openai_api_key"));
        Assert.Equal(expected, policy.GetMode(config.Server));
        Assert.Equal(original, JsonSerializer.Serialize(config.Server));
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
