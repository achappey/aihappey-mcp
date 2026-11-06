using System.Collections.Concurrent;
using System.Net;
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
using MCPhappey.Tools.Google;
using MCPhappey.Tools.Google.Agents;
using MCPhappey.Tools.Google.Audio;
using MCPhappey.Tools.Google.Interactions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tests;

public sealed class GoogleDualModeTests
{
    [Theory]
    [InlineData("Google-Agents")]
    [InlineData("Google-Audio")]
    [InlineData("Google-CodeExecution")]
    [InlineData("Google-NanoBanana")]
    public void Registry_definitions_select_the_deployment_mode_without_mutating_metadata(string name)
    {
        var config = Definition(name);
        var original = JsonSerializer.Serialize(config.Server);
        var direct = new ServerAccessPolicy();
        var delegated = new ServerAccessPolicy(ServerHeaderAuthenticationTests.OAuth());
        Assert.Equal(ServerAccessMode.Headers, direct.GetMode(config.Server));
        Assert.Equal(ServerAccessMode.Delegated, delegated.GetMode(config.Server));
        var header = Assert.Single(direct.GetPublicHeaders(config.Server)!);
        Assert.Equal("x-goog-api-key", header.Name);
        Assert.Equal("{google_api_key}", header.Value);
        Assert.True(header.IsRequired);
        var input = header.Variables!["google_api_key"];
        Assert.True(input.IsSecret);
        Assert.True(input.IsRequired);
        Assert.False(string.IsNullOrWhiteSpace(input.Placeholder));
        Assert.Equal("caller-key", direct.GetCredential(config.Server,
            new Dictionary<string, string> { ["X-Goog-Api-Key"] = "caller-key" }, "google_api_key"));
        Assert.DoesNotContain("google_api_key", JsonSerializer.Serialize(delegated.GetPublicHeaders(config.Server)));
        Assert.Contains("access_token", JsonSerializer.Serialize(delegated.GetPublicHeaders(config.Server)));
        Assert.NotEmpty(config.Server.OBO!);
        Assert.Equal(original, JsonSerializer.Serialize(config.Server));
    }

    [Theory]
    [InlineData("Google-Agents", false)]
    [InlineData("Google-Agents", true)]
    [InlineData("Google-Audio", false)]
    [InlineData("Google-Audio", true)]
    [InlineData("Google-CodeExecution", false)]
    [InlineData("Google-CodeExecution", true)]
    [InlineData("Google-NanoBanana", false)]
    [InlineData("Google-NanoBanana", true)]
    public async Task Reused_clients_resolve_credentials_per_request_and_preserve_provider_contracts(string name, bool delegated)
    {
        var config = Definition(name);
        var sent = new List<(string Key, string Method)>();
        using var handler = new CaptureHandler(async request =>
        {
            Assert.Equal("https", request.RequestUri!.Scheme);
            Assert.Equal(GoogleProviderAccess.ApiHost, request.RequestUri.Host);
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("X-Unrelated-Key"));
            sent.Add((Assert.Single(request.Headers.GetValues("x-goog-api-key")), request.Method.Method));
            if (name == "Google-Agents")
            {
                Assert.StartsWith("/v1beta/agents", request.RequestUri.AbsolutePath);
                if (request.Method == HttpMethod.Post)
                    Assert.Equal("preserved", JsonNode.Parse(await request.Content!.ReadAsStringAsync())!["extra"]!.GetValue<string>());
            }
            else
            {
                Assert.Equal("/v1beta/interactions", request.RequestUri.AbsolutePath);
                Assert.Equal(GoogleInteractionsClient.ApiRevision, Assert.Single(request.Headers.GetValues("Api-Revision")));
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
                Assert.Equal("test-model", body["model"]!.GetValue<string>());
                Assert.Equal("hello", body["input"]!["text"]!.GetValue<string>());
                Assert.False(body["store"]!.GetValue<bool>());
                Assert.False(body["stream"]!.GetValue<bool>());
            }
            return JsonResponse("""{"id":"agent_1","extra":"preserved","outputs":[{"type":"text","text":"answer"}]}""");
        });
        var registrations = Services(config, delegated, "first-caller");
        registrations.AddGoogleAI(delegated ? "operator-key" : null);
        if (!delegated)
            registrations.AddSingleton<GoogleSettings>(_ => throw new InvalidOperationException("Header access must not resolve operator settings."));
        ConfigureTransport(registrations, handler);
        using var services = registrations.BuildServiceProvider();
        await using var server = Server(config);
        var agents = services.GetRequiredService<GoogleAgentsClient>();
        var interactions = services.GetRequiredService<GoogleInteractionsClient>();
        foreach (var key in new[] { "first-caller", "second-caller" })
        {
            SetAccess(services, config, delegated, key);
            if (name == "Google-Agents")
            {
                var result = await agents.CreateAsync(new JsonObject { ["extra"] = "preserved" }, server: server);
                Assert.Equal("preserved", result["extra"]!.GetValue<string>());
                await agents.DeleteAsync("agent_1", server: server);
            }
            else
            {
                var result = await interactions.CreateInteractionAsync(Interaction(), server: server);
                Assert.Equal("preserved", result["extra"]!.GetValue<string>());
                Assert.Equal("answer", await interactions.CreateTextInteractionAsync(Interaction(), server: server));
            }
        }
        Assert.Equal(delegated ? ["operator-key", "operator-key", "operator-key", "operator-key"]
            : new[] { "first-caller", "first-caller", "second-caller", "second-caller" }, sent.Select(item => item.Key));
    }

    [Theory]
    [InlineData("Google-Agents")]
    [InlineData("Google-Audio")]
    [InlineData("Google-CodeExecution")]
    [InlineData("Google-NanoBanana")]
    public async Task Header_mode_does_not_fall_back_to_operator_credentials(string name)
    {
        var config = Definition(name);
        var registrations = Services(config, false, "caller-key");
        registrations.AddGoogleAI("operator-key");
        using var handler = new CaptureHandler(_ => throw new InvalidOperationException("The provider must not be reached."));
        ConfigureTransport(registrations, handler);
        using var services = registrations.BuildServiceProvider();
        await using var server = Server(config);
        SetAccess(services, config, false, null);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => services.GetRequiredService<GoogleInteractionsClient>()
            .CreateInteractionAsync(Interaction(), server: server));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => services.GetRequiredService<GoogleAgentsClient>()
            .CreateAsync(new JsonObject(), server: server));
    }

    [Theory]
    [InlineData("Google-Agents")]
    [InlineData("Google-Audio")]
    [InlineData("Google-CodeExecution")]
    [InlineData("Google-NanoBanana")]
    public void Delegated_mode_requires_validated_identity_and_configured_credentials(string name)
    {
        var config = Definition(name);
        using var services = Services(config, true, "caller-key").AddGoogleAI().BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => GoogleProviderAccess.GetApiKey(services));
        Assert.Throws<UnauthorizedAccessException>(() => services.GetProviderCredential(config, "google_api_key"));
        services.GetRequiredService<IHttpContextAccessor>().HttpContext!.User = new ClaimsPrincipal(new ClaimsIdentity());
        Assert.Throws<UnauthorizedAccessException>(() => GoogleProviderAccess.GetApiKey(services));
    }

    [Fact]
    public async Task Concurrent_callers_do_not_share_google_keys_or_client_default_headers()
    {
        var config = Definition("Google-Agents");
        var sent = new ConcurrentBag<string>();
        using var handler = new CaptureHandler(request =>
        {
            sent.Add(Assert.Single(request.Headers.GetValues("x-goog-api-key")));
            return Task.FromResult(JsonResponse("{}"));
        });
        async Task CallAsync(string key)
        {
            var registrations = Services(config, false, key).AddGoogleAI();
            ConfigureTransport(registrations, handler);
            using var services = registrations.BuildServiceProvider();
            await services.GetRequiredService<GoogleAgentsClient>().CreateAsync(new JsonObject());
            await new GoogleContentDownloader().DownloadAsync(services, config,
                "https://generativelanguage.googleapis.com/v1beta/agents/agent_1");
        }
        await Task.WhenAll(CallAsync("first-caller"), CallAsync("second-caller"));
        Assert.Equal(2, sent.Count(key => key == "first-caller"));
        Assert.Equal(2, sent.Count(key => key == "second-caller"));
    }

    [Fact]
    public async Task A_google_request_cannot_borrow_another_servers_credentials()
    {
        var config = Definition("Google-Agents");
        var other = Definition("Google-Audio");
        using var services = Services(config, false, "caller-key").AddGoogleAI("operator-key")
            .AddSingleton<IReadOnlyList<ServerConfig>>(new[] { config, other }).BuildServiceProvider();
        await using var server = Server(other);
        Assert.Throws<UnauthorizedAccessException>(() => GoogleProviderAccess.GetApiKey(services, server));
    }

    [Theory]
    [InlineData("Google-Agents")]
    [InlineData("Google-Audio")]
    [InlineData("Google-CodeExecution")]
    [InlineData("Google-NanoBanana")]
    [InlineData("Google-AgentEditor")]
    public void Missing_request_access_state_cannot_turn_target_requests_into_operator_access(string name)
    {
        var config = Definition(name);
        using var services = Services(config, false, "caller-key").AddGoogleAI("operator-key").BuildServiceProvider();
        var context = services.GetRequiredService<IHttpContextAccessor>().HttpContext!;
        context.Request.Path = "/" + name.ToLowerInvariant();
        context.Items.Clear();
        Assert.Throws<UnauthorizedAccessException>(() => GoogleProviderAccess.GetApiKey(services));
    }

    [Theory]
    [InlineData("Meta-Audio")]
    [InlineData("Meta-CodeExecution")]
    [InlineData("Meta-ImageGeneration")]
    [InlineData("Unrelated-DirectPrompt")]
    public async Task Unrelated_google_consumers_keep_configured_credentials(string name)
    {
        var config = Definition("Google-Audio");
        config.Server.ServerInfo.Name = name;
        var registrations = Services(config, false, "caller-key").AddGoogleAI("operator-key");
        using var handler = new CaptureHandler(request =>
        {
            Assert.Equal("operator-key", Assert.Single(request.Headers.GetValues("x-goog-api-key")));
            return Task.FromResult(JsonResponse("{}"));
        });
        ConfigureTransport(registrations, handler);
        using var services = registrations.BuildServiceProvider();
        await using var server = Server(config);
        await services.GetRequiredService<GoogleInteractionsClient>().CreateInteractionAsync(Interaction(), server: server);
        Assert.False(new GoogleContentDownloader().Supports(config));
        Assert.False(GoogleProviderAccess.IsHeaderMode(services, server));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Google_agent_resources_run_before_operator_scrapers(bool delegated)
    {
        var config = Definition("Google-Agents");
        var registrations = Services(config, delegated, "caller-key").AddGoogleAI(delegated ? "operator-key" : null);
        using var handler = new CaptureHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(delegated ? "operator-key" : "caller-key", Assert.Single(request.Headers.GetValues("x-goog-api-key")));
            return Task.FromResult(JsonResponse("""{"agents":[],"extra":"preserved"}"""));
        });
        ConfigureTransport(registrations, handler);
        using var services = registrations.BuildServiceProvider();
        var downloader = new DownloadService(null!, new TransformService([]), [new ForbiddenScraper()],
            services.GetServices<IRequestContentDownloader>());
        var resources = new ResourceService(downloader);
        await using var server = Server(config);
        var result = await resources.GetServerResource(services, server,
            "https://generativelanguage.googleapis.com/v1beta/agents?page_size=5&page_token=next&parent=test");
        var content = Assert.IsType<TextResourceContents>(Assert.Single(result.Contents));
        Assert.Equal("application/json", content.MimeType);
        Assert.Equal("preserved", JsonNode.Parse(content.Text)!["extra"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("https://other.test/v1beta/agents")]
    [InlineData("http://generativelanguage.googleapis.com/v1beta/agents")]
    [InlineData("https://generativelanguage.googleapis.com/v1beta/files")]
    [InlineData("https://generativelanguage.googleapis.com/v1beta/agents/../../files")]
    [InlineData("https://generativelanguage.googleapis.com/v1beta/agents/%2e%2e/files")]
    [InlineData("https://user:password@generativelanguage.googleapis.com/v1beta/agents")]
    [InlineData("https://generativelanguage.googleapis.com:8443/v1beta/agents")]
    public void Agent_credentials_are_restricted_to_google_agent_endpoints(string url)
        => Assert.Throws<UnauthorizedAccessException>(() => GoogleProviderAccess.ValidateAgentUrl(url));

    [Theory]
    [InlineData("Google-Agents")]
    [InlineData("Google-Audio")]
    [InlineData("Google-CodeExecution")]
    [InlineData("Google-NanoBanana")]
    public async Task Header_file_inputs_reject_protected_and_private_urls_before_operator_scrapers(string name)
    {
        var config = Definition(name);
        using var services = Services(config, false, "caller-key").AddGoogleAI("operator-key").BuildServiceProvider();
        var downloader = new DownloadService(null!, new TransformService([]), [new ForbiddenScraper()],
            services.GetServices<IRequestContentDownloader>());
        await using var server = Server(config);
        foreach (var url in new[] { "https://tenant.sharepoint.com/document.json", "https://1drv.ms/file",
            "https://graph.microsoft.com/v1.0/me/drive", "https://127.0.0.1/document.json", "http://public.test/document.json" })
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => downloader.DownloadContentAsync(services, server, url));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => downloader.ScrapeContentAsync(services, server, url));
        }
        PublicFileDownloader.ValidateUri(new Uri("https://public.test/payload.json?token=signed"));
    }

    [Theory]
    [InlineData("Google-Agents")]
    [InlineData("Google-Audio")]
    [InlineData("Google-CodeExecution")]
    [InlineData("Google-NanoBanana")]
    public async Task Delegated_file_inputs_keep_the_existing_download_pipeline(string name)
    {
        var config = Definition(name);
        using var services = Services(config, true, "identity-token").AddGoogleAI().BuildServiceProvider();
        Assert.Null(await new GoogleContentDownloader().DownloadAsync(services, config, "https://tenant.sharepoint.com/document.json"));
    }

    [Fact]
    public async Task Inline_google_media_never_uploads_to_graph_and_preserves_text_and_mime_types()
    {
        var result = await GoogleInteractionToolExtensions.CreateResultAsync(MediaResponse(), true,
            (_, _) => throw new InvalidOperationException("OBO must not be invoked."));
        Assert.Equal("answer", Assert.IsType<TextContentBlock>(result.Content[0]).Text);
        Assert.Equal("image/png", Assert.IsType<ImageContentBlock>(result.Content[1]).MimeType);
        Assert.Equal("audio/mp3", Assert.IsType<AudioContentBlock>(result.Content[2]).MimeType);
        var document = Assert.IsType<EmbeddedResourceBlock>(result.Content[3]);
        var blob = Assert.IsType<BlobResourceContents>(document.Resource);
        Assert.Equal("application/pdf", blob.MimeType);
        Assert.Equal(new byte[] { 1, 2, 3 }, blob.Blob.ToArray());
    }

    [Fact]
    public async Task Delegated_google_images_and_documents_keep_uploaded_links_and_never_downgrade_on_failure()
    {
        var uploaded = new List<string>();
        var result = await GoogleInteractionToolExtensions.CreateResultAsync(MediaResponse(), false, (media, _) =>
        {
            uploaded.Add(media.Type);
            return Task.FromResult<ResourceLinkBlock?>(new() { Uri = "https://tenant.sharepoint.com/output", Name = "output", MimeType = media.MimeType });
        });
        Assert.Equal(new[] { "image", "document" }, uploaded);
        Assert.Equal(2, result.Content.OfType<ResourceLinkBlock>().Count());
        Assert.Single(result.Content.OfType<AudioContentBlock>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => GoogleInteractionToolExtensions.CreateResultAsync(MediaResponse(), false,
            (_, _) => Task.FromResult<ResourceLinkBlock?>(null)));
    }

    [Fact]
    public async Task Inline_google_media_has_an_aggregate_size_limit()
    {
        var response = new JsonObject { ["outputs"] = new JsonArray(new JsonObject
        {
            ["type"] = "document", ["mime_type"] = "application/pdf",
            ["data"] = Convert.ToBase64String(new byte[ImageOutputService.MaxInlineBytes + 1])
        }) };
        await Assert.ThrowsAsync<InvalidOperationException>(() => GoogleInteractionToolExtensions.CreateResultAsync(response, true,
            (_, _) => throw new InvalidOperationException("OBO must not be invoked.")));
    }

    [Fact]
    public async Task Editor_is_obo_only_but_can_deploy_using_the_shared_agents_client()
    {
        var config = Definition("Google-AgentEditor");
        Assert.Empty(config.Server.Headers ?? []);
        Assert.Equal(ServerAccessMode.Unavailable, new ServerAccessPolicy().GetMode(config.Server));
        using (var services = Services(config, false, "caller-key").AddGoogleAI("operator-key").BuildServiceProvider())
            Assert.Throws<UnauthorizedAccessException>(() => GoogleProviderAccess.GetApiKey(services));

        var registrations = Services(config, true, "caller-key").AddGoogleAI("operator-key");
        using var handler = new CaptureHandler(request =>
        {
            Assert.Equal("operator-key", Assert.Single(request.Headers.GetValues("x-goog-api-key")));
            return Task.FromResult(JsonResponse("""{"id":"agent_1"}"""));
        });
        ConfigureTransport(registrations, handler);
        using var delegated = registrations.BuildServiceProvider();
        await using var server = Server(config);
        var result = await GoogleAgents.CreateValidatedAsync(delegated, server,
            new JsonObject { ["id"] = "agent_1", ["base_agent"] = GoogleAgentDocument.SupportedBaseAgent }, CancellationToken.None);
        Assert.Equal("agent_1", result["id"]!.GetValue<string>());
    }

    [Fact]
    public void Removed_features_are_not_exposed_and_single_speaker_and_podcast_tools_remain()
    {
        Assert.Throws<FileNotFoundException>(() => Definition("Google-Video"));
        Assert.Null(typeof(GoogleAudio).GetMethod("GoogleAudio_CreateMultiSpeakerSpeech"));
        Assert.NotNull(typeof(GoogleAudio).GetMethod(nameof(GoogleAudio.GoogleAudio_CreateSpeech)));
        Assert.NotNull(typeof(GoogleAudio).GetMethod(nameof(GoogleAudio.GoogleAudio_CreatePodcastScript)));
        Assert.DoesNotContain("multi speaker", Definition("Google-Audio").Server.Instructions!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Registration_is_idempotent_and_available_without_operator_keys_and_disables_redirects()
    {
        var registrations = new ServiceCollection().AddGoogleAI().AddGoogleAI("operator-key").AddGoogleAI();
        using var services = registrations.BuildServiceProvider();
        Assert.Single(services.GetServices<IRequestContentDownloader>());
        Assert.NotNull(services.GetRequiredService<PublicFileDownloader>());
        Assert.NotNull(services.GetRequiredService<GoogleAgentsClient>());
        Assert.NotNull(services.GetRequiredService<GoogleInteractionsClient>());
        Assert.Equal("operator-key", services.GetRequiredService<GoogleSettings>().ApiKey);
        foreach (var name in new[] { nameof(GoogleAgentsClient), nameof(GoogleInteractionsClient) })
        {
            var handler = services.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(name);
            while (handler is DelegatingHandler delegating) handler = delegating.InnerHandler!;
            var primary = Assert.IsType<HttpClientHandler>(handler);
            Assert.False(primary.AllowAutoRedirect);
            Assert.False(primary.UseCookies);
        }
    }

    [Fact]
    public async Task Google_redirects_are_errors_and_cancellation_is_preserved()
    {
        var config = Definition("Google-Agents");
        var calls = 0;
        using var handler = new CaptureHandler(_ =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers = { Location = new Uri("https://other.test/agents") }, Content = new StringContent("")
            });
        });
        var registrations = Services(config, false, "caller-key").AddGoogleAI();
        ConfigureTransport(registrations, handler);
        using var services = registrations.BuildServiceProvider();
        await Assert.ThrowsAsync<HttpRequestException>(() => services.GetRequiredService<GoogleAgentsClient>().CreateAsync(new JsonObject()));
        await Assert.ThrowsAsync<HttpRequestException>(() => services.GetRequiredService<GoogleInteractionsClient>().CreateInteractionAsync(Interaction()));
        Assert.Equal(2, calls);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => services.GetRequiredService<GoogleInteractionsClient>()
            .CreateInteractionAsync(Interaction(), cancellation.Token));
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData("Google-Agents")]
    [InlineData("Google-Audio")]
    [InlineData("Google-CodeExecution")]
    [InlineData("Google-NanoBanana")]
    public async Task Delegated_transport_rejects_api_keys_as_identity_tokens(string name)
    {
        var config = Definition(name);
        using var services = new ServiceCollection().AddSingleton<IJwtValidator>(new RejectingValidator()).BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("mcp.test");
        context.Request.Path = "/" + name.ToLowerInvariant();
        context.Request.Headers.Authorization = "Bearer provider-key-not-an-identity";
        context.Request.Headers["x-goog-api-key"] = "caller-key";
        context.Response.Body = new MemoryStream();
        var reached = false;
        await new ServerAccessMiddleware(_ => { reached = true; return Task.CompletedTask; },
            new ServerAccessPolicy(ServerHeaderAuthenticationTests.OAuth())).InvokeAsync(context, [config]);
        Assert.False(reached);
        Assert.Equal(401, context.Response.StatusCode);
    }

    private static ServerConfig Definition(string name) => AgentDualModeTests.Definition(name);

    private static IServiceCollection Services(ServerConfig config, bool delegated, string key)
    {
        var context = new DefaultHttpContext();
        if (delegated)
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "subject")], "validated"));
        context.Items[typeof(ServerRequestAccess)] = Access(config, delegated, key);
        return new ServiceCollection().AddSingleton<IReadOnlyList<ServerConfig>>(new[] { config })
            .AddSingleton(new ServerAccessPolicy(delegated ? ServerHeaderAuthenticationTests.OAuth() : null))
            .AddSingleton<IHttpContextAccessor>(new FixedAccessor { HttpContext = context });
    }

    private static ServerRequestAccess Access(ServerConfig config, bool delegated, string? key)
        => new(config, delegated ? ServerAccessMode.Delegated : ServerAccessMode.Headers,
            key is null ? new Dictionary<string, string>() : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["x-goog-api-key"] = key, ["Authorization"] = "Bearer not-a-provider-key", ["X-Unrelated-Key"] = "must-not-forward"
            });

    private static void SetAccess(IServiceProvider services, ServerConfig config, bool delegated, string? key)
        => services.GetRequiredService<IHttpContextAccessor>().HttpContext!.Items[typeof(ServerRequestAccess)] = Access(config, delegated, key);

    private static void ConfigureTransport(IServiceCollection registrations, HttpMessageHandler handler)
    {
        registrations.AddHttpClient<GoogleAgentsClient>().ConfigurePrimaryHttpMessageHandler(() => handler);
        registrations.AddHttpClient<GoogleInteractionsClient>().ConfigurePrimaryHttpMessageHandler(() => handler);
    }

    private static McpServer Server(ServerConfig config)
        => McpServer.Create(new StreamServerTransport(Stream.Null, Stream.Null), new()
        {
            ServerInfo = new() { Name = config.Server.ServerInfo.Name, Version = "1.0.0" }
        });

    private static GoogleInteractionRequest Interaction() => new()
    {
        Model = "test-model", Input = GoogleInteractionInput.Text("hello")
    };

    private static JsonObject MediaResponse() => new()
    {
        ["outputs"] = new JsonArray(GoogleInteractionInput.Text("answer"),
            GoogleInteractionInput.Media("image", "AQID", null, "image/png"),
            GoogleInteractionInput.Media("audio", "AQID", null, "audio/mp3"),
            GoogleInteractionInput.Media("document", "AQID", null, "application/pdf"))
    };

    private static HttpResponseMessage JsonResponse(string text)
        => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };

    private sealed class FixedAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private sealed class CaptureHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            return await respond(request);
        }
    }

    private sealed class ForbiddenScraper : IContentScraper
    {
        public bool SupportsHost(ServerConfig serverConfig, string host) => throw new InvalidOperationException("Operator scraper must not run.");
        public Task<IEnumerable<FileItem>?> GetContentAsync(McpServer server, IServiceProvider services, string url, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Operator scraper must not run.");
    }

    private sealed class RejectingValidator : IJwtValidator
    {
        public Task<ClaimsPrincipal?> ValidateAsync(string token, string issuer, string audience, OAuthSettings settings)
            => Task.FromResult<ClaimsPrincipal?>(null);
    }
}
