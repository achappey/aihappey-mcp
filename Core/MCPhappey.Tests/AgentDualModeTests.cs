using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCPhappey.Auth;
using MCPhappey.Auth.Models;
using MCPhappey.Common;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools;
using MCPhappey.Tools.Anthropic;
using MCPhappey.Tools.Anthropic.Skills;
using MCPhappey.Tools.OpenAI.Decisions;
using MCPhappey.Tools.OpenAI.Skills;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tests;

public sealed class AgentDualModeTests
{
    [Theory]
    [InlineData("OpenAI-Agents", "openai_api_key", "Authorization", "Bearer caller-key")]
    [InlineData("OpenAI-Decisions", "openai_api_key", "Authorization", "Bearer caller-key")]
    [InlineData("Anthropic-Agents", "anthropic_api_key", "x-api-key", "caller-key")]
    public void Real_registry_definitions_support_both_modes_without_mutating_Obo(
        string name, string variable, string headerName, string headerValue)
    {
        var config = Definition(name);
        var original = JsonSerializer.Serialize(config.Server);
        var direct = new ServerAccessPolicy();
        var delegated = new ServerAccessPolicy(ServerHeaderAuthenticationTests.OAuth());
        Assert.Equal(ServerAccessMode.Headers, direct.GetMode(config.Server));
        Assert.Equal(ServerAccessMode.Delegated, delegated.GetMode(config.Server));
        Assert.Equal("caller-key", direct.GetCredential(config.Server,
            new Dictionary<string, string> { [headerName] = headerValue }, variable));
        Assert.True(Assert.Single(config.Server.Headers!).Variables![variable].IsSecret);
        Assert.DoesNotContain("image generation", config.Server.Headers![0].Description);
        var oauthHeaders = JsonSerializer.Serialize(delegated.GetPublicHeaders(config.Server));
        Assert.Contains("access_token", oauthHeaders);
        Assert.DoesNotContain(variable, oauthHeaders);
        Assert.NotEmpty(config.Server.OBO!);
        Assert.Equal(original, JsonSerializer.Serialize(config.Server));
    }

    [Theory]
    [InlineData("OpenAI-Agents")]
    [InlineData("Anthropic-Agents")]
    public async Task Agent_helpers_and_resources_use_each_callers_key_without_operator_settings(string name)
    {
        var config = Definition(name);
        var sent = new ConcurrentBag<(string Key, string Method)>();
        using var handler = new CaptureHandler(request =>
        {
            var key = name == "OpenAI-Agents" ? request.Headers.Authorization!.Parameter!
                : Assert.Single(request.Headers.GetValues(AnthropicHeaders.ApiKeyHeader));
            sent.Add((key, request.Method.Method));
            Assert.False(request.Headers.Contains("X-Unrelated-Key"));
            if (name == "OpenAI-Agents")
                Assert.Equal("agents=v1", Assert.Single(request.Headers.GetValues("OpenAI-Beta")));
            else
            {
                Assert.Null(request.Headers.Authorization);
                Assert.Equal(AnthropicHeaders.AnthropicVersion, Assert.Single(request.Headers.GetValues(AnthropicHeaders.AnthropicVersionHeader)));
                Assert.Contains(AnthropicHeaders.ManagedAgentsBetaFeature,
                    Assert.Single(request.Headers.GetValues(AnthropicHeaders.AnthropicBetaHeader)));
            }
            return JsonResponse("""{"id":"agent_1","version":2,"tools":[],"extra":"preserved"}""");
        });

        async Task CallAsync(string key)
        {
            using var services = Services(config, false, key).AddSingleton<IHttpClientFactory>(new Factory(handler)).BuildServiceProvider();
            Assert.Null(services.GetService<OpenAISettings>());
            Assert.Null(services.GetService<AnthropicSettings>());
            var url = $"https://{AgentProviderHttp.GetProviderHost(config)}/v1/agents";
            // Exercise the same shared helper used by every mutation/read-modify-write call.
            var body = new JsonObject { ["name"] = "test" };
            var result = await CallSharedHelperAsync(name, services, HttpMethod.Post, url, body);
            Assert.Equal("preserved", result["extra"]!.GetValue<string>());
            var file = Assert.Single((await new AgentContentDownloader().DownloadAsync(services, config,
                url + "/agent_1?version=2"))!);
            Assert.Equal("application/json", file.MimeType);
            Assert.Equal("preserved", JsonNode.Parse(file.Contents.ToString())!["extra"]!.GetValue<string>());
        }

        await Task.WhenAll(CallAsync("first-caller"), CallAsync("second-caller"));
        Assert.Equal(2, sent.Count(item => item.Key == "first-caller"));
        Assert.Equal(2, sent.Count(item => item.Key == "second-caller"));
        Assert.Equal(2, sent.Count(item => item.Method == "POST"));
        Assert.Equal(2, sent.Count(item => item.Method == "GET"));
    }

    [Theory]
    [InlineData("OpenAI-Agents", "openai_api_key")]
    [InlineData("OpenAI-Decisions", "openai_api_key")]
    [InlineData("Anthropic-Agents", "anthropic_api_key")]
    public void Header_mode_never_evaluates_operator_credentials_or_falls_back_when_key_is_missing(string name, string variable)
    {
        var config = Definition(name);
        using var services = Services(config, false, "caller-key").BuildServiceProvider();
        Assert.Equal("caller-key", ProviderApiKeyResolver.Resolve(services, config, variable,
            () => throw new InvalidOperationException("Operator credentials must not be read.")));
        var access = services.GetRequiredService<IHttpContextAccessor>().HttpContext!;
        access.Items[typeof(ServerRequestAccess)] = new ServerRequestAccess(config, ServerAccessMode.Headers,
            new Dictionary<string, string>());
        Assert.Throws<UnauthorizedAccessException>(() => ProviderApiKeyResolver.Resolve(services, config, variable, () => "operator-key"));
    }

    [Theory]
    [InlineData("OpenAI-Agents", "openai_api_key")]
    [InlineData("OpenAI-Decisions", "openai_api_key")]
    [InlineData("Anthropic-Agents", "anthropic_api_key")]
    public void Delegated_resolution_requires_validated_identity_and_never_uses_the_callers_provider_key(string name, string variable)
    {
        var config = Definition(name);
        using var services = Services(config, true, "caller-provider-key").BuildServiceProvider();
        Assert.Equal("operator-key", ProviderApiKeyResolver.Resolve(services, config, variable, () => "operator-key"));
        Assert.Throws<InvalidOperationException>(() => ProviderApiKeyResolver.Resolve(services, config, variable, () => null));
        Assert.Throws<UnauthorizedAccessException>(() => services.GetProviderCredential(config, variable));
        services.GetRequiredService<IHttpContextAccessor>().HttpContext!.User = new ClaimsPrincipal(new ClaimsIdentity());
        Assert.Throws<UnauthorizedAccessException>(() => ProviderApiKeyResolver.Resolve(services, config, variable, () => "operator-key"));
    }

    [Theory]
    [InlineData("OpenAI-Agents")]
    [InlineData("Anthropic-Agents")]
    public async Task Delegated_agent_transport_uses_operator_key_not_identity_or_provider_headers(string name)
    {
        var config = Definition(name);
        using var handler = new CaptureHandler(request =>
        {
            var actual = name == "OpenAI-Agents" ? request.Headers.Authorization!.Parameter
                : Assert.Single(request.Headers.GetValues("x-api-key"));
            Assert.Equal("operator-key", actual);
            return JsonResponse("{}");
        });
        using var services = Services(config, true, "caller-key")
            .AddSingleton(new OpenAISettings { ApiKey = "operator-key" })
            .AddSingleton(new AnthropicSettings { ApiKey = "operator-key" })
            .AddSingleton<IHttpClientFactory>(new Factory(handler)).BuildServiceProvider();
        await AgentProviderHttp.SendAsync(services, config, HttpMethod.Get,
            $"https://{AgentProviderHttp.GetProviderHost(config)}/v1/agents", null);
    }

    [Theory]
    [InlineData("OpenAI-Agents")]
    [InlineData("OpenAI-Decisions")]
    [InlineData("Anthropic-Agents")]
    public async Task Entra_transport_rejects_direct_key_access_before_any_tool_or_resource_execution(string name)
    {
        var config = Definition(name);
        using var services = new ServiceCollection().AddSingleton<IJwtValidator>(new RejectingValidator()).BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("mcp.test");
        context.Request.Path = "/" + name.ToLowerInvariant();
        context.Request.Headers.Authorization = "Bearer provider-key-not-an-identity";
        context.Request.Headers["x-api-key"] = "caller-anthropic-key";
        context.Response.Body = new MemoryStream();
        var reached = false;
        await new ServerAccessMiddleware(_ => { reached = true; return Task.CompletedTask; },
            new ServerAccessPolicy(ServerHeaderAuthenticationTests.OAuth())).InvokeAsync(context, [config]);
        Assert.False(reached);
        Assert.Equal(401, context.Response.StatusCode);
        Assert.False(context.Items.ContainsKey(typeof(ServerRequestAccess)));
    }

    [Fact]
    public void Request_credentials_cannot_be_reused_for_a_different_server()
    {
        using var services = Services(Definition("OpenAI-Agents"), false, "caller-key").BuildServiceProvider();
        Assert.Throws<UnauthorizedAccessException>(() => services.GetProviderCredential(Definition("OpenAI-Decisions"), "openai_api_key"));
    }

    [Theory]
    [InlineData("OpenAI-Agents")]
    [InlineData("Anthropic-Agents")]
    public async Task Shared_helpers_retain_operator_configuration_for_unrelated_servers(string provider)
    {
        var config = Definition(provider);
        config.Server.ServerInfo.Name = "Unrelated-Server";
        using var handler = new CaptureHandler(request =>
        {
            var key = provider == "OpenAI-Agents" ? request.Headers.Authorization!.Parameter
                : Assert.Single(request.Headers.GetValues("x-api-key"));
            Assert.Equal("operator-key", key);
            return JsonResponse("{}");
        });
        using var services = Services(config, false, "caller-key")
            .AddSingleton(new OpenAISettings { ApiKey = "operator-key" })
            .AddSingleton(new AnthropicSettings { ApiKey = "operator-key" })
            .AddSingleton<IHttpClientFactory>(new Factory(handler)).BuildServiceProvider();
        if (provider == "OpenAI-Agents")
            await CallSharedHelperAsync(provider, services, HttpMethod.Get, "https://api.openai.com/v1/vaults", null);
        else
            await CallSharedHelperAsync(provider, services, HttpMethod.Get, "https://api.anthropic.com/v1/sessions", null);
        Assert.False(new AgentContentDownloader().Supports(config));
    }

    [Theory]
    [InlineData("OpenAI-Agents")]
    [InlineData("Anthropic-Agents")]
    public async Task Target_HTTP_requests_without_validated_access_state_cannot_fall_back_to_operator_keys(string name)
    {
        var config = Definition(name);
        using var services = Services(config, false, "caller-key")
            .AddSingleton(new OpenAISettings { ApiKey = "operator-key" })
            .AddSingleton(new AnthropicSettings { ApiKey = "operator-key" }).BuildServiceProvider();
        var context = services.GetRequiredService<IHttpContextAccessor>().HttpContext!;
        context.Request.Path = "/" + name.ToLowerInvariant();
        context.Items.Clear();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => CallSharedHelperAsync(name, services,
            HttpMethod.Get, $"https://{AgentProviderHttp.GetProviderHost(config)}/v1/agents", null));
    }

    [Theory]
    [InlineData("OpenAI-Agents")]
    [InlineData("Anthropic-Agents")]
    public async Task Provider_resources_take_precedence_over_generic_operator_scrapers(string name)
    {
        var config = Definition(name);
        using var handler = new CaptureHandler(_ => JsonResponse("""{"data":[],"object":"list"}"""));
        using var services = Services(config, false, "caller-key").AddSingleton<IHttpClientFactory>(new Factory(handler)).BuildServiceProvider();
        var downloads = new DownloadService(null!, new TransformService([]), [new ForbiddenScraper()], [new AgentContentDownloader()]);
        var resources = new ResourceService(downloads);
        await using var server = Server(config);
        var result = await resources.GetServerResource(services, server,
            $"https://{AgentProviderHttp.GetProviderHost(config)}/v1/agents");
        var content = Assert.IsType<TextResourceContents>(Assert.Single(result.Contents));
        Assert.Equal("application/json", content.MimeType);
        Assert.Equal("list", JsonNode.Parse(content.Text)!["object"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("https://other.test/v1/agents")]
    [InlineData("http://api.openai.com/v1/agents")]
    [InlineData("https://api.openai.com/v1/files")]
    [InlineData("https://api.openai.com/v1/agents/../../v1/files")]
    [InlineData("https://api.openai.com/v1/agents/%2e%2e/files")]
    [InlineData("https://user:password@api.openai.com/v1/agents")]
    public void Provider_credentials_are_restricted_to_intended_agent_endpoints(string url)
        => Assert.Throws<UnauthorizedAccessException>(() => AgentProviderHttp.ValidateUrl(Definition("OpenAI-Agents"), url));

    [Theory]
    [InlineData("OpenAI-Agents")]
    [InlineData("OpenAI-Decisions")]
    [InlineData("Anthropic-Agents")]
    public async Task Header_downloads_reject_protected_locations_without_reaching_network_or_operator_scrapers(string name)
    {
        var config = Definition(name);
        using var services = Services(config, false, "caller-key").AddSingleton<PublicFileDownloader>().BuildServiceProvider();
        foreach (var url in new[] { "https://tenant.sharepoint.com/schema.json", "https://1drv.ms/file",
            "https://graph.microsoft.com/v1.0/me/drive", "https://127.0.0.1/schema.json", "http://public.test/schema.json" })
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new AgentContentDownloader().DownloadAsync(services, config, url));
    }

    [Fact]
    public async Task Delegated_file_inputs_keep_the_existing_download_pipeline()
    {
        var config = Definition("OpenAI-Decisions");
        using var services = Services(config, true, "identity-token").BuildServiceProvider();
        Assert.Null(await new AgentContentDownloader().DownloadAsync(services, config, "https://tenant.sharepoint.com/document.pdf"));
    }

    [Fact]
    public void Public_file_metadata_preserves_MIME_and_safe_filename()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        response.Content.Headers.ContentDisposition = new("attachment") { FileNameStar = "../schema.json" };
        var file = PublicFileDownloader.ToFileItem(response, new Uri("https://public.test/download?token=one-use"), BinaryData.FromString("{}"));
        Assert.Equal("schema.json", file.Filename);
        Assert.Equal("application/json", file.MimeType);
        Assert.Equal("{}", file.Contents.ToString());
        Assert.Contains("token=one-use", file.Uri);
        PublicFileDownloader.ValidateUri(new Uri("https://public.test/schema.json?token=one-use"));
    }

    [Theory]
    [InlineData("https://localhost/file.json")]
    [InlineData("https://169.254.169.254/file.json")]
    [InlineData("https://[::1]/file.json")]
    [InlineData("https://tenant.sharepoint.com/file.json")]
    [InlineData("https://public.test:8443/file.json")]
    [InlineData("https://user:password@public.test/file.json")]
    public void Redirect_destinations_are_subject_to_the_same_public_file_rules(string url)
        => Assert.Throws<UnauthorizedAccessException>(() => PublicFileDownloader.ValidateUri(new Uri(url)));

    [Fact]
    public async Task Provider_redirects_are_errors_not_anonymous_or_credential_forwarding_fallbacks()
    {
        var config = Definition("OpenAI-Agents");
        var calls = 0;
        using var handler = new CaptureHandler(_ =>
        {
            calls++;
            var response = new HttpResponseMessage(HttpStatusCode.Redirect) { Content = new StringContent("") };
            response.Headers.Location = new Uri("https://other.test/agents");
            return response;
        });
        using var services = Services(config, false, "caller-key").AddSingleton<IHttpClientFactory>(new Factory(handler)).BuildServiceProvider();
        await Assert.ThrowsAsync<HttpRequestException>(() => AgentProviderHttp.SendAsync(services, config,
            HttpMethod.Get, "https://api.openai.com/v1/agents", null));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Registration_is_available_without_operator_keys_and_download_boundary_is_not_duplicated()
    {
        var registrations = new ServiceCollection();
        registrations.AddOpenAIDecisions(null);
        registrations.AddDualModeAgentAccess();
        using var services = registrations.BuildServiceProvider();
        Assert.Single(services.GetServices<IRequestContentDownloader>());
        Assert.NotNull(services.GetRequiredService<PublicFileDownloader>());
        Assert.NotNull(services.GetRequiredService<IHttpClientFactory>());
        Assert.Null(services.GetService<OpenAIDecisionsClient>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Decisions_resolves_correct_credentials_and_preserves_the_complete_response(bool delegated)
    {
        var config = Definition("OpenAI-Decisions");
        using var handler = new CaptureHandler(request =>
        {
            Assert.Equal(delegated ? "operator-key" : "caller-key", request.Headers.Authorization!.Parameter);
            Assert.Equal("https://api.openai.com/v1/decisions", request.RequestUri!.ToString());
            return JsonResponse("""{"answers":[],"extra":{"preserve":true}}""");
        });
        using var operatorHttp = new HttpClient(handler, false) { BaseAddress = new Uri("https://api.openai.com/v1/") };
        operatorHttp.DefaultRequestHeaders.Authorization = new("Bearer", "operator-key");
        var registrations = Services(config, delegated, "caller-key").AddSingleton<IHttpClientFactory>(new Factory(handler));
        if (delegated)
            registrations.AddSingleton(new OpenAIDecisionsClient(operatorHttp));
        using var services = registrations.BuildServiceProvider();
        await using var server = Server(config);
        var response = await OpenAIDecisionsClientResolver.CreateAsync(services, server, new()
        {
            Input = JsonValue.Create("evidence")!, Questions = OpenAIDecisionsQuestions.Predicate("Is it present?")
        });
        Assert.True(response.GetProperty("extra").GetProperty("preserve").GetBoolean());
        if (!delegated)
            Assert.Null(services.GetService<OpenAIDecisionsClient>());
    }

    [Fact]
    public async Task Decisions_folder_execution_retains_independent_results_partial_errors_and_progress()
    {
        var inputs = new OpenAIDecisionsInputBuilder((url, _) => url.EndsWith("bad.txt")
            ? throw new InvalidOperationException("extraction failed")
            : Task.FromResult<IEnumerable<FileItem>>([new() { MimeType = "text/plain", Contents = BinaryData.FromString("document evidence") }]),
            (_, _) => throw new InvalidOperationException("No images expected."));
        var reports = new List<OpenAIDecisionsProgress>();
        var calls = 0;
        var executor = new OpenAIDecisionsExecutor(inputs,
            (_, _) => Task.FromResult(new OpenAIDecisionsDocuments(
                [new("https://tenant.sharepoint.com/good.txt", "good.txt"), new("https://tenant.sharepoint.com/bad.txt", "bad.txt")],
                [new("https://tenant.sharepoint.com/folder", "folder")])),
            (request, _) =>
            {
                calls++;
                Assert.Contains("shared context", request.Input.GetValue<string>());
                Assert.Contains("document evidence", request.Input.GetValue<string>());
                return Task.FromResult(JsonSerializer.SerializeToElement(new { answers = Array.Empty<object>(), extra = true }));
            },
            (report, _) => { reports.Add(report); return Task.CompletedTask; });
        var result = await executor.ExecuteAsync(OpenAIDecisionsQuestions.Predicate("Is it present?"), "shared context",
            ["https://tenant.sharepoint.com/folder"], null);
        Assert.Equal("per_document", result.GetProperty("mode").GetString());
        Assert.Equal(1, result.GetProperty("succeeded").GetInt32());
        Assert.Equal(1, result.GetProperty("failed").GetInt32());
        Assert.True(result.GetProperty("results")[0].GetProperty("response").GetProperty("extra").GetBoolean());
        Assert.Equal("extraction failed", result.GetProperty("results")[1].GetProperty("error").GetString());
        Assert.Equal(1, calls);
        Assert.Equal(reports[^1].Total, reports[^1].Progress);
    }

    internal static ServerConfig Definition(string name)
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "Servers", "MCPhappey.Servers.JSON", "Servers", name, "Server.json");
            if (File.Exists(path))
                return new() { Server = JsonSerializer.Deserialize<Common.Models.Server>(File.ReadAllText(path))! };
        }
        throw new FileNotFoundException($"Cannot locate the {name} server definition.");
    }

    private static IServiceCollection Services(ServerConfig config, bool delegated, string key)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Authorization"] = "Bearer " + key, ["x-api-key"] = key, ["X-Unrelated-Key"] = "must-not-forward"
        };
        var context = new DefaultHttpContext();
        if (delegated)
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "subject")], "validated"));
        context.Items[typeof(ServerRequestAccess)] = new ServerRequestAccess(config,
            delegated ? ServerAccessMode.Delegated : ServerAccessMode.Headers, headers);
        return new ServiceCollection().AddSingleton<IReadOnlyList<ServerConfig>>(new[] { config })
            .AddSingleton(new ServerAccessPolicy(delegated ? ServerHeaderAuthenticationTests.OAuth() : null))
            .AddSingleton<IHttpContextAccessor>(new FixedAccessor { HttpContext = context });
    }

    private static McpServer Server(ServerConfig config)
        => McpServer.Create(new StreamServerTransport(Stream.Null, Stream.Null), new()
        {
            ServerInfo = new() { Name = config.Server.ServerInfo.Name, Version = "1.0.0" }
        });

    private static HttpResponseMessage JsonResponse(string text)
        => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };

    private static Task<JsonNode> CallSharedHelperAsync(string provider, IServiceProvider services,
        HttpMethod method, string url, JsonNode? body)
    {
        var typeName = provider == "OpenAI-Agents"
            ? "MCPhappey.Tools.OpenAI.AgentsApi.OpenAIAgentsHttp"
            : "MCPhappey.Tools.Anthropic.AnthropicManagedAgentsHttp";
        var helper = typeof(AgentProviderHttp).Assembly.GetType(typeName)!
            .GetMethod("SendAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
        return (Task<JsonNode>)helper.Invoke(null, [services, method, url, body, CancellationToken.None])!;
    }

    private sealed class FixedAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }
    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
    private sealed class CaptureHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            return respond(request);
        }
    }
    private sealed class ForbiddenScraper : IContentScraper
    {
        public bool SupportsHost(ServerConfig serverConfig, string host) => throw new InvalidOperationException("Generic operator scraper must not run.");
        public Task<IEnumerable<FileItem>?> GetContentAsync(McpServer server, IServiceProvider services, string url, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Generic operator scraper must not run.");
    }
    private sealed class RejectingValidator : IJwtValidator
    {
        public Task<ClaimsPrincipal?> ValidateAsync(string token, string issuer, string audience, OAuthSettings settings)
            => Task.FromResult<ClaimsPrincipal?>(null);
    }
}
