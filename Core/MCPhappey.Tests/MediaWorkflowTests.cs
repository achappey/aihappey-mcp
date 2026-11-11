using System.Net;
using System.Reflection;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCPhappey.Auth;
using MCPhappey.Common.Models;
using MCPhappey.Common;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.BlackForestLabs;
using MCPhappey.Tools.BlackForestLabs.Image;
using MCPhappey.Tools.BlackForestLabs.Video;
using MCPhappey.Tools.Google;
using MCPhappey.Tools.Google.Image;
using MCPhappey.Tools.Google.Interactions;
using MCPhappey.Tools.Google.Video;
using MCPhappey.Tools.OpenAI.Image;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OpenAI;

namespace MCPhappey.Tests;

public sealed class MediaWorkflowTests
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLbtAAAAABJRU5ErkJggg==");

    [Theory]
    [InlineData("OpenAI-Images", "Authorization", "openai_api_key", typeof(OpenAIImages), 3)]
    [InlineData("Google-Images", "x-goog-api-key", "google_api_key", typeof(GoogleNanoBanana), 2)]
    [InlineData("Google-Videos", "x-goog-api-key", "google_api_key", typeof(GoogleVideos), 2)]
    [InlineData("BlackForestLabs-Images", "x-key", "bfl_api_key", typeof(BlackForestLabsImages), 1)]
    [InlineData("BlackForestLabs-Videos", "x-key", "bfl_api_key", typeof(BlackForestLabsVideos), 2)]
    public void Five_servers_publish_dual_access_and_resolvable_tools(string name, string headerName, string variable, Type plugin, int toolCount)
    {
        var config = AgentDualModeTests.Definition(name);
        Assert.Equal(plugin, Type.GetType(Assert.Single(config.Server.Plugins!)));
        Assert.NotEmpty(config.Server.OBO!);
        var direct = new ServerAccessPolicy();
        var delegated = new ServerAccessPolicy(ServerHeaderAuthenticationTests.OAuth());
        Assert.Equal(ServerAccessMode.Headers, direct.GetMode(config.Server));
        Assert.Equal(ServerAccessMode.Delegated, delegated.GetMode(config.Server));
        var header = Assert.Single(direct.GetPublicHeaders(config.Server)!);
        Assert.Equal(headerName, header.Name);
        Assert.True(header.Variables![variable].IsSecret);
        Assert.Equal("Authorization", Assert.Single(delegated.GetPublicHeaders(config.Server)!).Name);
        Assert.DoesNotContain(variable, JsonSerializer.Serialize(delegated.GetPublicHeaders(config.Server)));
        var methods = plugin.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null).ToArray();
        Assert.Equal(toolCount, methods.Length);
        foreach (var method in methods)
        {
            var tool = McpServerTool.Create(method).ProtocolTool;
            Assert.NotNull(tool);
            Assert.StartsWith(config.Server.ServerInfo.Title!, tool.Title);
            Assert.DoesNotContain("previous_interaction", tool.InputSchema.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("previousInteraction", tool.InputSchema.ToString(), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Retired_servers_are_removed_and_existing_identifiers_are_preserved()
    {
        Assert.Throws<FileNotFoundException>(() => AgentDualModeTests.Definition("Google-NanoBanana"));
        Assert.Throws<FileNotFoundException>(() => AgentDualModeTests.Definition("BlackForestLabs-Finetunes"));
        Assert.Null(typeof(OpenAIImages).Assembly.GetType("MCPhappey.Tools.BlackForestLabs.Finetune.BlackForestLabsFinetune"));
        Assert.Equal("google_nano_banana_create_image", McpServerTool.Create(typeof(GoogleNanoBanana).GetMethod(nameof(GoogleNanoBanana.GoogleNanoBanana_CreateImage))!).ProtocolTool.Name);
        Assert.Equal("open_ai_images_create_image", McpServerTool.Create(typeof(OpenAIImages).GetMethod(nameof(OpenAIImages.OpenAIImages_CreateImage))!).ProtocolTool.Name);
        Assert.Equal("open_ai_images_create_image_edit", McpServerTool.Create(typeof(OpenAIImages).GetMethod(nameof(OpenAIImages.OpenAIImages_CreateImageEdit))!).ProtocolTool.Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Google_video_requests_are_inline_synchronous_and_stateless(bool delegated)
    {
        var config = AgentDualModeTests.Definition("Google-Videos");
        var calls = 0;
        using var handler = new CaptureHandler(async request =>
        {
            calls++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://generativelanguage.googleapis.com/v1beta/interactions", request.RequestUri!.ToString());
            Assert.Equal(delegated ? "operator-key" : "caller-key", Assert.Single(request.Headers.GetValues("x-goog-api-key")));
            Assert.Null(request.Headers.Authorization);
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!.AsObject();
            foreach (var field in new[] { "store", "stream", "background" }) Assert.False(body[field]!.GetValue<bool>());
            Assert.Null(body["previous_interaction_id"]);
            Assert.Null(body["system_instruction"]);
            Assert.Null(body["generation_config"]);
            Assert.Null(body["responseFormat"]);
            Assert.Equal("inline", body["response_format"]!["delivery"]!.GetValue<string>());
            Assert.Equal("10s", body["response_format"]!["duration"]!.GetValue<string>());
            var content = body["input"]!.AsArray();
            Assert.Equal("Zmlyc3Q=", content[0]!["data"]!.GetValue<string>());
            Assert.Equal("bGFzdA==", content[1]!["data"]!.GetValue<string>());
            Assert.False(content[0]!.AsObject().ContainsKey("uri"));
            Assert.False(content[1]!.AsObject().ContainsKey("uri"));
            return Json("""{"status":"completed","steps":[{"type":"model_output","content":[{"type":"video","data":"AQID","mime_type":"video/mp4"}]}]}""");
        });
        var registrations = Services(config, delegated, "caller-key");
        registrations.AddGoogleAI(delegated ? "operator-key" : null);
        registrations.AddHttpClient<GoogleInteractionsClient>().ConfigurePrimaryHttpMessageHandler(() => handler);
        using var services = registrations.BuildServiceProvider();
        await using var server = Server(config);
        var input = new JsonArray(GoogleInteractionInput.Media("image", "Zmlyc3Q=", null, "image/png"),
            GoogleInteractionInput.Media("image", "bGFzdA==", null, "image/png"), GoogleInteractionInput.Text("Transition between the first and last images."));
        var response = await services.GetRequiredService<GoogleInteractionsClient>().CreateInteractionAsync(
            GoogleVideos.BuildRequest(GoogleVideos.DefaultVideoModel, input, 10, "16:9", "720p"), server: server);
        Assert.Equal(new byte[] { 1, 2, 3 }, Assert.Single(GoogleInteractionResponse.GetMedia(response, "video")).Data);
        Assert.Equal(1, calls); // No Files API, background polling, or conversation lookup.
    }

    [Theory]
    [InlineData("image", "image/png")]
    [InlineData("video", "video/mp4")]
    [InlineData("audio", "audio/mp3")]
    [InlineData("document", "application/pdf")]
    public void Google_media_source_properties_are_exclusive_in_serialized_json(string type, string mime)
    {
        var inline = JsonNode.Parse(JsonSerializer.Serialize(
            GoogleInteractionInput.Bytes(type, BinaryData.FromBytes(new byte[] { 1, 2, 3 }), mime)))!.AsObject();
        Assert.Equal("AQID", inline["data"]!.GetValue<string>());
        Assert.False(inline.ContainsKey("uri")); // A null URI still counts as supplied to the provider.
        Assert.Equal(mime, inline["mime_type"]!.GetValue<string>());
        Assert.All(inline, property => Assert.NotNull(property.Value));

        var byUri = JsonNode.Parse(JsonSerializer.Serialize(
            GoogleInteractionInput.Media(type, null, "https://media.example.test/source", null)))!.AsObject();
        Assert.Equal("https://media.example.test/source", byUri["uri"]!.GetValue<string>());
        Assert.False(byUri.ContainsKey("data"));
        Assert.False(byUri.ContainsKey("mime_type"));
        Assert.All(byUri, property => Assert.NotNull(property.Value));
    }

    [Theory]
    [InlineData("AQID", "https://media.example.test/source")]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData(" ", "\t")]
    public void Google_media_rejects_ambiguous_or_missing_sources(string? data, string? uri)
        => Assert.Throws<ArgumentException>(() => GoogleInteractionInput.Media("image", data, uri, "image/png"));

    [Fact]
    public async Task Google_media_client_refuses_stored_streaming_or_background_requests()
    {
        var config = AgentDualModeTests.Definition("Google-Videos");
        using var handler = new CaptureHandler(_ => throw new InvalidOperationException("The provider must not be reached."));
        var registrations = Services(config, false, "key").AddGoogleAI();
        registrations.AddHttpClient<GoogleInteractionsClient>().ConfigurePrimaryHttpMessageHandler(() => handler);
        using var services = registrations.BuildServiceProvider();
        await using var server = Server(config);
        foreach (var request in new[]
        {
            new GoogleInteractionRequest { Input = JsonValue.Create("prompt")!, Store = true },
            new GoogleInteractionRequest { Input = JsonValue.Create("prompt")!, Stream = true },
            new GoogleInteractionRequest { Input = JsonValue.Create("prompt")!, Background = true }
        })
            await Assert.ThrowsAsync<ArgumentException>(() => services.GetRequiredService<GoogleInteractionsClient>()
                .CreateInteractionAsync(request, server: server));
    }

    [Fact]
    public async Task Google_response_does_not_echo_inputs_thoughts_or_uri_only_outputs()
    {
        var response = JsonNode.Parse("""
            {"steps":[
              {"type":"user_input","content":[{"type":"text","text":"secret input"},{"type":"image","data":"AQID"}]},
              {"type":"thought","content":[{"type":"text","text":"hidden thought"}]},
              {"type":"model_output","content":[{"type":"text","text":"answer"},{"type":"video","data":"BAUG","mime_type":"video/mp4"}]}]}
            """)!.AsObject();
        Assert.Equal("answer", GoogleInteractionResponse.GetText(response));
        Assert.Empty(GoogleInteractionResponse.GetMedia(response, "image"));
        Assert.Equal(new byte[] { 4, 5, 6 }, Assert.Single(GoogleInteractionResponse.GetMedia(response, "video")).Data);
        var uriOnly = JsonNode.Parse("""{"steps":[{"type":"model_output","content":[{"type":"video","uri":"https://google.test/file"}]}]}""")!.AsObject();
        await Assert.ThrowsAsync<InvalidOperationException>(() => GoogleInteractionToolExtensions.CreateResultAsync(uriOnly, true,
            (_, _) => throw new InvalidOperationException("No upload fallback.")));
    }

    [Theory]
    [InlineData(null, "last", null, "generate", 10)]
    [InlineData("first", "last", null, "generate", 11)]
    [InlineData("first", null, "video", "edit", 10)]
    [InlineData(null, null, null, "edit", 10)]
    [InlineData(null, null, "video", "generate", 10)]
    [InlineData(null, null, "video", "extend", 3)]
    [InlineData(null, null, "video", "extend", 10)]
    [InlineData(null, null, "video", "extend", 11)]
    [InlineData(null, null, null, "generate", 16)]
    public void Google_rejects_incompatible_frames_inputs_and_durations(string? first, string? last, string? video, string operation, int duration)
        => Assert.Throws<ArgumentException>(() => GoogleVideos.Validate("prompt", operation, first, last, video, 0, duration, "16:9", "720p"));

    [Theory]
    [InlineData("generate", "first", "last", null, 3)]
    [InlineData("generate", "first", "last", null, 10)]
    [InlineData("generate", "first", null, null, 15)]
    [InlineData("edit", null, null, "video", 15)]
    public void Google_supports_explicit_media_without_conversation_ids(string operation, string? first, string? last, string? video, int duration)
        => GoogleVideos.Validate("prompt", operation, first, last, video, 0, duration, "16:9", "720p");

    [Fact]
    public async Task Google_extension_is_not_advertised_or_executed_and_frame_generation_is_preserved()
    {
        var config = AgentDualModeTests.Definition("Google-Videos");
        Assert.DoesNotContain("extend", config.Server.ServerInfo.Description!, StringComparison.OrdinalIgnoreCase);
        var tool = McpServerTool.Create(typeof(GoogleVideos).GetMethod(nameof(GoogleVideos.GoogleVideos_CreateVideo))!).ProtocolTool;
        Assert.Equal("google_videos_create_video", tool.Name);
        Assert.DoesNotContain("extend", tool.Title!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("extend", tool.Description!, StringComparison.OrdinalIgnoreCase);
        var schema = tool.InputSchema.ToString();
        Assert.DoesNotContain("extend", schema, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("firstFrameUrl", schema);
        Assert.Contains("lastFrameUrl", schema);

        // No downloader or provider services: a removed operation must be rejected
        // before downloading source media or making a billable provider request.
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var server = Server(config);
        var result = await GoogleVideos.GoogleVideos_CreateVideo("Continue this scene.", services, Context(server),
            videoUrl: "https://media.example.test/scene.mp4", operation: "extend");
        Assert.NotNull(result);
        Assert.True(result.IsError);
        Assert.Contains("Use generate or edit.", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
    }

    [Theory]
    [InlineData(null, null, null, "t2v")]
    [InlineData("first", null, null, "i2v")]
    [InlineData("first", "last", null, "i2v")]
    [InlineData(null, null, "video", "v2v")]
    public void Bfl_selects_explicit_native_video_modes(string? first, string? last, string? source, string mode)
    {
        var payload = BlackForestLabsVideos.BuildPayload("prompt", first, last, source, null, "auto", "hd", true, false, 2);
        Assert.Equal(mode, payload["mode"]!.GetValue<string>());
        Assert.Equal("auto", payload["duration"]!.GetValue<string>());
        Assert.Null(payload["first_frame_url"]);
        Assert.Null(payload["last_frame_url"]);
    }

    [Fact]
    public void Bfl_checks_native_model_specific_options()
    {
        Assert.Throws<ArgumentException>(() => BlackForestLabsVideos.BuildPayload("prompt", null, "last", null, 10, "auto", "hd", true, false, 2));
        Assert.Throws<ArgumentException>(() => BlackForestLabsVideos.BuildPayload("prompt", "first", null, "video", 10, "auto", "hd", true, false, 2));
        Assert.Throws<ArgumentException>(() => BlackForestLabsVideos.BuildPayload("prompt", null, null, "video", 16, "auto", "hd", true, false, 2));
        Assert.Throws<ArgumentException>(() => ImagePayload("flux-3-image", seed: 1));
        Assert.Throws<ArgumentException>(() => ImagePayload("flux-3-image-klein", grounding: true));
        Assert.Throws<ArgumentException>(() => ImagePayload("flux-3-image", references: 11));
        Assert.Throws<ArgumentException>(() => ImagePayload("flux-3-image", references: 1, width: 4096, height: 4096));
        Assert.Throws<ArgumentException>(() => ImagePayload("flux-3-image", width: 1024));
        Assert.Throws<ArgumentException>(() => ImagePayload("flux-3-image-klein", seed: -1));
        Assert.Throws<ArgumentException>(() => ImagePayload("flux-3-image-klein", steps: 17));
        var klein = ImagePayload("flux-3-image-klein", seed: 42, width: 64, height: 64, steps: 16);
        Assert.Equal(42, klein["seed"]!.GetValue<long>());
        Assert.Null(klein["grounding"]);
        Assert.Null(klein["version"]);
        Assert.Equal("latest", ImagePayload("flux-3-image")["version"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("https://other.test/v1/get_result?id=1")]
    [InlineData("http://api.bfl.ai/v1/get_result?id=1")]
    [InlineData("https://api.bfl.ai:8443/v1/get_result?id=1")]
    [InlineData("https://user:pass@api.bfl.ai/v1/get_result?id=1")]
    [InlineData("https://api.bfl.ai.evil.test/v1/get_result?id=1")]
    [InlineData("https://api.bfl.ai/v1/delete_finetune")]
    public void Bfl_polling_rejects_credential_exfiltration_destinations(string url)
        => Assert.Throws<UnauthorizedAccessException>(() => BlackForestLabsClient.ValidatePollingUri(url));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Bfl_polls_in_call_with_request_scoped_credentials_and_no_shared_task_state(bool delegated)
    {
        var config = AgentDualModeTests.Definition("BlackForestLabs-Videos");
        var requests = new List<string>();
        var polls = 0;
        using var handler = new CaptureHandler(async request =>
        {
            requests.Add(request.RequestUri!.AbsoluteUri);
            Assert.Equal(delegated ? "operator-key" : "caller-key", Assert.Single(request.Headers.GetValues("x-key")));
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("X-Unrelated-Key"));
            if (request.Method == HttpMethod.Post)
            {
                Assert.Equal("/v1/flux-3-video", request.RequestUri.AbsolutePath);
                var payload = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
                Assert.Equal("t2v", payload["mode"]!.GetValue<string>());
                return Json("""{"id":"task","polling_url":"https://api.us1.bfl.ai/v1/get_result?id=task"}""");
            }
            polls++;
            return polls % 2 == 1 ? Json("""{"status":"Pending"}""") : Json("""{"status":"Ready","result":{"sample":"AQID"}}""");
        });
        var registrations = Services(config, delegated, "caller-key").AddBlackForestLabs();
        if (delegated) registrations.AddSingleton(new BlackForestLabsSettings { ApiKey = "operator-key" });
        else registrations.AddSingleton<BlackForestLabsSettings>(_ => throw new InvalidOperationException("Operator settings must not be read."));
        registrations.AddHttpClient<BlackForestLabsClient>().ConfigurePrimaryHttpMessageHandler(() => handler);
        using var services = registrations.BuildServiceProvider();
        await using var server = Server(config);
        var client = services.GetRequiredService<BlackForestLabsClient>();
        client.PollInterval = TimeSpan.Zero;
        foreach (var _ in new[] { 1, 2 })
        {
            var result = await client.GenerateAsync(server, "flux-3-video",
                BlackForestLabsVideos.BuildPayload("prompt", null, null, null, 5, "auto", "hd", true, false, 2));
            Assert.Equal("AQID", result["sample"]!.GetValue<string>());
        }
        Assert.Equal(6, requests.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Bfl_reasoning_and_new_progress_phases_keep_polling_without_resubmitting_generation(bool delegated)
    {
        var config = AgentDualModeTests.Definition("BlackForestLabs-Videos");
        var submitted = 0;
        var polled = 0;
        var phases = new Queue<string>(["Reasoning", "reasoning", "Processing", "Rendering audio", "Ready"]);
        using var handler = new CaptureHandler(async request =>
        {
            Assert.Equal(delegated ? "operator-key" : "caller-key", Assert.Single(request.Headers.GetValues("x-key")));
            if (request.Method == HttpMethod.Post)
            {
                submitted++;
                var payload = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
                Assert.Equal("i2v", payload["mode"]!.GetValue<string>());
                Assert.Equal(20, payload["duration"]!.GetValue<int>());
                Assert.Equal("AQID", payload["keyframes"]![0]!.GetValue<string>());
                Assert.Equal("BAUG", payload["keyframes"]![1]!.GetValue<string>());
                return Json("""{"id":"task","polling_url":"https://api.bfl.ai/v1/get_result?id=task"}""");
            }
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/v1/get_result", request.RequestUri!.AbsolutePath);
            polled++;
            var phase = phases.Dequeue();
            return Json(phase == "Ready" ? """{"status":"Ready","result":{"sample":"AQID"}}"""
                : new JsonObject { ["status"] = phase }.ToJsonString());
        });
        var registrations = Services(config, delegated, "caller-key").AddBlackForestLabs();
        if (delegated) registrations.AddSingleton(new BlackForestLabsSettings { ApiKey = "operator-key" });
        registrations.AddHttpClient<BlackForestLabsClient>().ConfigurePrimaryHttpMessageHandler(() => handler);
        using var services = registrations.BuildServiceProvider();
        await using var server = Server(config);
        var client = services.GetRequiredService<BlackForestLabsClient>();
        client.PollInterval = TimeSpan.Zero;
        var payload = BlackForestLabsVideos.BuildPayload("scene", "first", "last", null, 20, "16:9", "hd", true, false, 2);
        payload["keyframes"] = new JsonArray(JsonValue.Create("AQID"), JsonValue.Create("BAUG"));
        var result = await client.GenerateAsync(server, "flux-3-video", payload);
        Assert.Equal("AQID", result["sample"]!.GetValue<string>());
        Assert.Equal(1, submitted);
        Assert.Equal(5, polled);
        Assert.Empty(phases);
    }

    [Fact]
    public async Task Bfl_header_mode_has_no_operator_key_fallback_and_delegated_mode_requires_identity()
    {
        var config = AgentDualModeTests.Definition("BlackForestLabs-Images");
        using var handler = new CaptureHandler(_ => throw new InvalidOperationException("The provider must not be reached."));
        var registrations = Services(config, false, "").AddBlackForestLabs();
        registrations.AddSingleton(new BlackForestLabsSettings { ApiKey = "operator-key" });
        registrations.AddHttpClient<BlackForestLabsClient>().ConfigurePrimaryHttpMessageHandler(() => handler);
        using var services = registrations.BuildServiceProvider();
        await using var server = Server(config);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => services.GetRequiredService<BlackForestLabsClient>()
            .GenerateAsync(server, "flux-3-image", ImagePayload("flux-3-image")));
        using var delegated = Services(config, true, "caller-key").AddBlackForestLabs().BuildServiceProvider();
        await Assert.ThrowsAsync<InvalidOperationException>(() => delegated.GetRequiredService<BlackForestLabsClient>()
            .GenerateAsync(server, "flux-3-image", ImagePayload("flux-3-image")));
        delegated.GetRequiredService<IHttpContextAccessor>().HttpContext!.User = new ClaimsPrincipal(new ClaimsIdentity());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => delegated.GetRequiredService<BlackForestLabsClient>()
            .GenerateAsync(server, "flux-3-image", ImagePayload("flux-3-image")));
    }

    [Theory]
    [InlineData("Error")]
    [InlineData("Request Moderated")]
    [InlineData("Content Moderated")]
    [InlineData("Task not found")]
    [InlineData("Failed")]
    [InlineData("Cancelled")]
    [InlineData("Canceled")]
    [InlineData("Expired")]
    public async Task Bfl_terminal_failures_do_not_wait_until_timeout(string status)
    {
        var config = AgentDualModeTests.Definition("BlackForestLabs-Videos");
        var calls = 0;
        using var handler = new CaptureHandler(request =>
        {
            calls++;
            return Task.FromResult(request.Method == HttpMethod.Post ? Json("""{"id":"task"}""")
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent(new JsonObject { ["status"] = status }.ToJsonString()) });
        });
        var registrations = Services(config, false, "key").AddBlackForestLabs();
        registrations.AddHttpClient<BlackForestLabsClient>().ConfigurePrimaryHttpMessageHandler(() => handler);
        using var services = registrations.BuildServiceProvider();
        await using var server = Server(config);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => services.GetRequiredService<BlackForestLabsClient>()
            .GenerateAsync(server, "flux-3-video", new JsonObject()));
        Assert.Contains(status, error.Message);
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Reasoning")]
    [InlineData("Future progress phase")]
    public async Task Bfl_timeout_and_caller_cancellation_are_distinct_and_bounded(string phase)
    {
        var config = AgentDualModeTests.Definition("BlackForestLabs-Videos");
        using var handler = new CaptureHandler(request => Task.FromResult(request.Method == HttpMethod.Post
            ? Json("""{"id":"task"}""") : Json(new JsonObject { ["status"] = phase }.ToJsonString())));
        var registrations = Services(config, false, "key").AddBlackForestLabs();
        registrations.AddHttpClient<BlackForestLabsClient>().ConfigurePrimaryHttpMessageHandler(() => handler);
        using var services = registrations.BuildServiceProvider();
        await using var server = Server(config);
        var client = services.GetRequiredService<BlackForestLabsClient>();
        client.PollTimeout = TimeSpan.FromMilliseconds(30);
        client.PollInterval = TimeSpan.FromSeconds(1);
        await Assert.ThrowsAsync<TimeoutException>(() => client.GenerateAsync(server, "flux-3-video", new JsonObject()));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GenerateAsync(server, "flux-3-video", new JsonObject(), cancellation.Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Video_and_artifact_outputs_are_raw_or_links_without_a_size_cap(bool delegated)
    {
        var video = new byte[10 * 1024 * 1024 + 1];
        var uploads = new List<string>();
        var result = await MediaOutputService.CreateAsync(
            [new("scene.mp4", BinaryData.FromBytes(video), "video/mp4"), new("scene-draft-cache.bin", BinaryData.FromBytes(new byte[] { 1 }), "application/octet-stream")],
            delegated, (media, _) =>
            {
                Assert.True(delegated);
                uploads.Add(media.Filename);
                return Task.FromResult<ResourceLinkBlock?>(new() { Name = media.Filename, Uri = "https://tenant.sharepoint.com/" + media.Filename, MimeType = media.MimeType });
            });
        Assert.Equal(2, result.Content.Count);
        if (delegated)
        {
            Assert.Equal(new[] { "scene.mp4", "scene-draft-cache.bin" }, uploads);
            Assert.All(result.Content, content => Assert.IsType<ResourceLinkBlock>(content));
        }
        else
        {
            Assert.Empty(uploads);
            var blob = Assert.IsType<BlobResourceContents>(Assert.IsType<EmbeddedResourceBlock>(result.Content[0]).Resource);
            Assert.Equal("video/mp4", blob.MimeType);
            Assert.Equal(video, blob.DecodedData.ToArray());
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.Equal(Convert.ToBase64String(video), json.RootElement.GetProperty("content")[0].GetProperty("resource").GetProperty("blob").GetString());
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => MediaOutputService.CreateAsync(
            [new("scene.mp4", BinaryData.FromBytes(new byte[] { 1 }), "video/mp4")], true,
            (_, _) => Task.FromResult<ResourceLinkBlock?>(null)));
    }

    [Theory]
    [InlineData("BlackForestLabs-Images")]
    [InlineData("BlackForestLabs-Videos")]
    [InlineData("Google-Videos")]
    public async Task Generation_tool_invocations_deliver_raw_media_without_Graph_services(string name)
    {
        var config = AgentDualModeTests.Definition(name);
        var image = name.EndsWith("Images");
        using var handler = new CaptureHandler(request =>
        {
            if (name == "Google-Videos")
                return Task.FromResult(Json("""{"status":"completed","steps":[{"type":"model_output","content":[{"type":"video","data":"AQID","mime_type":"video/mp4"}]}]}"""));
            if (request.Method == HttpMethod.Post) return Task.FromResult(Json("""{"id":"task"}"""));
            return Task.FromResult(Json(new JsonObject
            {
                ["status"] = "Ready", ["result"] = new JsonObject
                {
                    ["sample"] = image ? Convert.ToBase64String(Png) : "AQID", ["draft_cache"] = "BAUG"
                }
            }.ToJsonString()));
        });
        var registrations = Services(config, false, "caller-key").AddBlackForestLabs().AddGoogleAI();
        registrations.AddHttpClient<BlackForestLabsClient>().ConfigurePrimaryHttpMessageHandler(() => handler);
        registrations.AddHttpClient<GoogleInteractionsClient>().ConfigurePrimaryHttpMessageHandler(() => handler);
        using var services = registrations.BuildServiceProvider();
        await using var server = Server(config);
        var context = Context(server);
        var result = name switch
        {
            "BlackForestLabs-Images" => await BlackForestLabsImages.BlackForestLabsImages_CreateImage("scene", services, context),
            "BlackForestLabs-Videos" => await BlackForestLabsVideos.BlackForestLabsVideos_CreateVideo("scene", services, context, draft: true),
            _ => await GoogleVideos.GoogleVideos_CreateVideo("scene", services, context)
        };
        Assert.NotNull(result);
        Assert.False(result.IsError == true, JsonSerializer.Serialize(result));
        Assert.Empty(result.Content.OfType<ResourceLinkBlock>());
        if (image) Assert.Equal(Png, Assert.IsType<ImageContentBlock>(Assert.Single(result.Content)).DecodedData.ToArray());
        else
        {
            var blob = Assert.IsType<BlobResourceContents>(Assert.IsType<EmbeddedResourceBlock>(result.Content[0]).Resource);
            Assert.Equal("video/mp4", blob.MimeType);
            Assert.Equal(new byte[] { 1, 2, 3 }, blob.DecodedData.ToArray());
            if (name.StartsWith("BlackForest"))
            {
                Assert.Equal(2, result.Content.Count);
                var cache = Assert.IsType<BlobResourceContents>(Assert.IsType<EmbeddedResourceBlock>(result.Content[1]).Resource);
                Assert.Contains("draft-cache.bin", cache.Uri);
                Assert.Equal(new byte[] { 4, 5, 6 }, cache.DecodedData.ToArray());
            }
        }
    }

    [Theory]
    [InlineData("Google-Images")]
    [InlineData("Google-Videos")]
    public async Task Google_question_tools_download_delegated_media_then_send_only_inline_content(string name)
    {
        var config = AgentDualModeTests.Definition(name);
        var video = name == "Google-Videos";
        var scraper = new MediaScraper();
        using var handler = new CaptureHandler(async request =>
        {
            Assert.Equal("/v1beta/interactions", request.RequestUri!.AbsolutePath);
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
            Assert.False(body["store"]!.GetValue<bool>());
            Assert.False(body["background"]!.GetValue<bool>());
            Assert.Equal(GoogleInteractionsClient.DefaultModel, body["model"]!.GetValue<string>());
            Assert.Equal(video ? "video" : "image", body["input"]![0]!["type"]!.GetValue<string>());
            Assert.Equal(Convert.ToBase64String(video ? new byte[] { 1, 2, 3 } : Png), body["input"]![0]!["data"]!.GetValue<string>());
            Assert.DoesNotContain("sharepoint", body.ToJsonString());
            Assert.DoesNotContain("previous_interaction", body.ToJsonString());
            return Json("""{"status":"completed","steps":[{"type":"user_input","content":[{"type":"text","text":"not the answer"}]},{"type":"model_output","content":[{"type":"text","text":"answer"}]}]}""");
        });
        var registrations = Services(config, true, "identity").AddGoogleAI("operator-key");
        registrations.AddSingleton(new DownloadService(null!, new TransformService([]), [scraper]));
        registrations.AddHttpClient<GoogleInteractionsClient>().ConfigurePrimaryHttpMessageHandler(() => handler);
        using var services = registrations.BuildServiceProvider();
        await using var server = Server(config);
        var url = "https://tenant.sharepoint.com/scene." + (video ? "mp4" : "png");
        var result = video
            ? await GoogleVideos.GoogleVideos_AskVideo("What happens?", url, services, Context(server))
            : await GoogleNanoBanana.GoogleNanoBanana_AskImages("What is shown?", [url], services, Context(server));
        Assert.NotNull(result);
        Assert.False(result.IsError == true, JsonSerializer.Serialize(result));
        Assert.Equal("answer", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        Assert.Equal(url, Assert.Single(scraper.Urls));
    }

    [Fact]
    public async Task OpenAI_image_questions_use_explicit_images_and_disable_stored_output()
    {
        var config = AgentDualModeTests.Definition("OpenAI-Images");
        var scraper = new MediaScraper();
        using var handler = new CaptureHandler(async request =>
        {
            Assert.Equal("/v1/chat/completions", request.RequestUri!.AbsolutePath);
            Assert.Equal("operator-key", request.Headers.Authorization!.Parameter);
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
            Assert.False(body["store"]!.GetValue<bool>());
            Assert.Equal("gpt-4.1", body["model"]!.GetValue<string>());
            var message = Assert.Single(body["messages"]!.AsArray());
            var content = message!["content"]!.AsArray();
            Assert.Equal("What is shown?", content[0]!["text"]!.GetValue<string>());
            Assert.Equal("data:image/png;base64," + Convert.ToBase64String(Png), content[1]!["image_url"]!["url"]!.GetValue<string>());
            return Json("""{"id":"chatcmpl_test","object":"chat.completion","created":1,"model":"gpt-4.1","choices":[{"index":0,"message":{"role":"assistant","content":"answer"},"finish_reason":"stop"}]}""");
        });
        using var http = new HttpClient(handler);
        var registrations = Services(config, true, "identity");
        registrations.AddSingleton(new DownloadService(null!, new TransformService([]), [scraper]));
        registrations.AddSingleton(new OpenAIClient(new ApiKeyCredential("operator-key"), new OpenAIClientOptions
        { Transport = new HttpClientPipelineTransport(http) }));
        using var services = registrations.BuildServiceProvider();
        await using var server = Server(config);
        var result = await OpenAIImages.OpenAIImages_AskImages("What is shown?",
            ["https://tenant.sharepoint.com/scene.png"], services, Context(server));
        Assert.NotNull(result);
        Assert.False(result.IsError == true, JsonSerializer.Serialize(result));
        Assert.Equal("answer", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        Assert.Single(scraper.Urls);
    }

    [Fact]
    public void Domain_header_registration_is_available_without_keys_and_idempotent()
    {
        var headers = new Dictionary<string, Dictionary<string, string>>
        {
            ["API.BFL.AI"] = new() { ["X-KEY"] = "operator-bfl" },
            ["GENERATIVELANGUAGE.GOOGLEAPIS.COM"] = new() { ["X-GOOG-API-KEY"] = "operator-google" }
        };
        using var services = new ServiceCollection().AddBlackForestLabs().AddBlackForestLabs(headers).AddBlackForestLabs()
            .AddGoogleAI().AddGoogleAIFromHeaders(headers).AddGoogleAI().BuildServiceProvider();
        Assert.Equal("operator-bfl", services.GetRequiredService<BlackForestLabsSettings>().ApiKey);
        Assert.Equal("operator-google", services.GetRequiredService<GoogleSettings>().ApiKey);
        Assert.NotNull(services.GetRequiredService<BlackForestLabsClient>());
        Assert.NotNull(services.GetRequiredService<GoogleInteractionsClient>());
        var handler = services.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(nameof(BlackForestLabsClient));
        while (handler is DelegatingHandler delegating) handler = delegating.InnerHandler!;
        Assert.False(Assert.IsType<HttpClientHandler>(handler).AllowAutoRedirect);
        Assert.False(Assert.IsType<HttpClientHandler>(handler).UseCookies);
    }

    private static RequestContext<CallToolRequestParams> Context(McpServer server)
        => new(server, new JsonRpcRequest { Id = new RequestId(1), Method = "tools/call" }, new CallToolRequestParams { Name = "media_test" });

    private sealed class MediaScraper : IContentScraper
    {
        public List<string> Urls { get; } = [];
        public bool SupportsHost(ServerConfig serverConfig, string host) => true;
        public Task<IEnumerable<FileItem>?> GetContentAsync(McpServer server, IServiceProvider services, string url, CancellationToken cancellationToken = default)
        {
            Urls.Add(url);
            var video = url.EndsWith(".mp4");
            return Task.FromResult<IEnumerable<FileItem>?>([new FileItem
            {
                Uri = url, Filename = video ? "scene.mp4" : "scene.png", MimeType = video ? "video/mp4" : "image/png",
                Contents = BinaryData.FromBytes(video ? new byte[] { 1, 2, 3 } : Png)
            }]);
        }
    }

    private static JsonObject ImagePayload(string model, int references = 0, int? width = null, int? height = null,
        bool? grounding = null, long? seed = null, int? steps = null)
        => BlackForestLabsImages.BuildPayload("prompt", model, references, "auto", null, width, height, grounding, 2, seed, null, steps);

    private static IServiceCollection Services(ServerConfig config, bool delegated, string key)
    {
        var context = new DefaultHttpContext();
        if (delegated) context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "subject")], "validated"));
        context.Items[typeof(ServerRequestAccess)] = new ServerRequestAccess(config,
            delegated ? ServerAccessMode.Delegated : ServerAccessMode.Headers,
            new Dictionary<string, string> { ["x-key"] = key, ["x-goog-api-key"] = key, ["Authorization"] = "Bearer identity-not-provider", ["X-Unrelated-Key"] = "never-forward" });
        return new ServiceCollection().AddSingleton<IReadOnlyList<ServerConfig>>(new[] { config })
            .AddSingleton(new ServerAccessPolicy(delegated ? ServerHeaderAuthenticationTests.OAuth() : null))
            .AddSingleton<IHttpContextAccessor>(new FixedAccessor { HttpContext = context });
    }

    private static McpServer Server(ServerConfig config) => McpServer.Create(new StreamServerTransport(Stream.Null, Stream.Null), new()
    { ServerInfo = new() { Name = config.Server.ServerInfo.Name, Version = "1.0.0" } });

    private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    private sealed class FixedAccessor : IHttpContextAccessor { public HttpContext? HttpContext { get; set; } }
    private sealed class CaptureHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return respond(request);
        }
    }
}
