using System.ClientModel.Primitives;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using MCPhappey.Auth;
using MCPhappey.Common;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.OpenAI.Image;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OpenAI;

namespace MCPhappey.Tests;

public sealed class ImageCredentialTests
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");

    [Fact]
    public async Task OpenAI_works_without_registered_client_and_uses_each_callers_key()
    {
        var config = ServerHeaderAuthenticationTests.ImageServer();
        var sentKeys = new List<string?>();
        using var http = new HttpClient(new CaptureHandler(request =>
        {
            sentKeys.Add(request.Headers.Authorization?.Parameter);
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
            {
                created = 1, data = new[] { new { b64_json = Convert.ToBase64String(Png) } }
            }), System.Text.Encoding.UTF8, "application/json") };
        }));
        using var services = Services(config, new ServerAccessPolicy())
            .AddSingleton(new OpenAIClientOptions { Transport = new HttpClientPipelineTransport(http) }).BuildServiceProvider();
        await using var server = McpServer.Create(new StreamServerTransport(Stream.Null, Stream.Null), new()
        {
            ServerInfo = new() { Name = config.Server.ServerInfo.Name, Version = "1.0.0" }
        });
        Assert.Null(services.GetService<OpenAIClient>());
        foreach (var key in new[] { "first-caller-key", "second-caller-key" })
        {
            using var scope = services.CreateScope();
            scope.ServiceProvider.GetRequiredService<HeaderProvider>().Headers = new() { ["authorization"] = "Bearer " + key };
            var client = OpenAIImageClientResolver.Resolve(scope.ServiceProvider, server);
            var image = await client.GetImageClient("gpt-image-1").GenerateImageAsync("test");
            Assert.Equal(Png, image.Value.ImageBytes.ToArray());
        }
        Assert.Equal(new[] { "first-caller-key", "second-caller-key" }, sentKeys);
    }

    [Fact]
    public async Task Missing_caller_key_never_falls_back_to_configured_operator_client()
    {
        var config = ServerHeaderAuthenticationTests.ImageServer();
        using var services = Services(config, new ServerAccessPolicy()).AddSingleton(new OpenAIClient("operator-key")).BuildServiceProvider();
        await using var server = McpServer.Create(new StreamServerTransport(Stream.Null, Stream.Null), new()
        {
            ServerInfo = new() { Name = config.Server.ServerInfo.Name, Version = "1.0.0" }
        });
        Assert.Throws<UnauthorizedAccessException>(() => OpenAIImageClientResolver.Resolve(services, server));
    }

    [Fact]
    public async Task Delegated_access_preserves_configured_client_and_rejects_caller_key_resolution()
    {
        var config = ServerHeaderAuthenticationTests.ImageServer();
        var policy = new ServerAccessPolicy(ServerHeaderAuthenticationTests.OAuth());
        var configured = new OpenAIClient("operator-key");
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "subject")], "validated"));
        context.Items[typeof(ServerRequestAccess)] = new ServerRequestAccess(config, ServerAccessMode.Delegated,
            new Dictionary<string, string> { ["Authorization"] = "Bearer identity-token" });
        using var services = Services(config, policy).AddSingleton(configured)
            .AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = context }).BuildServiceProvider();
        await using var server = McpServer.Create(new StreamServerTransport(Stream.Null, Stream.Null), new()
        {
            ServerInfo = new() { Name = config.Server.ServerInfo.Name, Version = "1.0.0" }
        });
        Assert.Same(configured, OpenAIImageClientResolver.Resolve(services, server));
        Assert.Throws<UnauthorizedAccessException>(() => services.GetProviderCredential(server, "openai_api_key"));
    }

    [Fact]
    public async Task Inline_MCP_image_output_never_invokes_upload()
    {
        var result = await ImageOutputService.CreateAsync([new("test.png", BinaryData.FromBytes(Png), "image/png")], false,
            (_, _) => throw new InvalidOperationException("OBO must not be called"));
        var image = Assert.IsType<ImageContentBlock>(Assert.Single(result.Content));
        Assert.Equal("image/png", image.MimeType);
        var json = JsonSerializer.Serialize(result);
        Assert.Equal(Convert.ToBase64String(Png), JsonDocument.Parse(json).RootElement.GetProperty("content")[0].GetProperty("data").GetString());
        Assert.False(result.IsError == true);
    }

    [Fact]
    public async Task Delegated_output_preserves_uploaded_resource_links_and_errors_do_not_downgrade()
    {
        var calls = 0;
        var output = new ImageOutput("test.png", BinaryData.FromBytes(Png), "image/png");
        var link = new ResourceLinkBlock { Uri = "https://tenant.sharepoint.com/test.png", Name = "test.png", MimeType = "image/png" };
        var result = await ImageOutputService.CreateAsync([output], true, (_, _) => { calls++; return Task.FromResult<ResourceLinkBlock?>(link); });
        Assert.Same(link, Assert.Single(result.Content));
        Assert.Equal(1, calls);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ImageOutputService.CreateAsync([output], true, (_, _) => Task.FromResult<ResourceLinkBlock?>(null)));
    }

    [Fact]
    public async Task Inline_output_is_bounded()
        => await Assert.ThrowsAsync<InvalidOperationException>(() => ImageOutputService.CreateAsync(
            [new("large.png", BinaryData.FromBytes(new byte[ImageOutputService.MaxInlineBytes + 1]), "image/png")], false,
            (_, _) => throw new InvalidOperationException()));

    [Theory]
    [InlineData("https://127.0.0.1/image.png")]
    [InlineData("https://169.254.169.254/image.png")]
    [InlineData("https://10.0.0.1/image.png")]
    [InlineData("https://[::1]/image.png")]
    [InlineData("https://tenant.sharepoint.com/image.png")]
    [InlineData("https://1drv.ms/image")]
    [InlineData("https://graph.microsoft.com/v1.0/me/photo/$value")]
    [InlineData("http://public.test/image.png")]
    [InlineData("https://user:password@public.test/image.png")]
    public void Direct_image_inputs_reject_private_or_protected_locations(string url)
        => Assert.Throws<UnauthorizedAccessException>(() => PublicImageDownloader.ValidateUri(new Uri(url)));

    [Theory]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("192.168.1.1", false)]
    [InlineData("100.100.100.200", false)]
    [InlineData("168.63.129.16", false)]
    [InlineData("::ffff:127.0.0.1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("2001:db8::1", false)]
    [InlineData("8.8.8.8", true)]
    [InlineData("2606:4700:4700::1111", true)]
    public void DNS_destination_validation_excludes_non_public_ranges(string address, bool expected)
        => Assert.Equal(expected, PublicImageDownloader.IsPublicAddress(IPAddress.Parse(address)));

    [Fact]
    public void Image_bytes_must_match_content_type()
    {
        PublicImageDownloader.ValidateImage(Png, "image/png");
        Assert.Throws<InvalidOperationException>(() => PublicImageDownloader.ValidateImage("not an image"u8.ToArray(), "image/png"));
    }

    private static IServiceCollection Services(ServerConfig config, ServerAccessPolicy policy)
        => new ServiceCollection().AddSingleton<IReadOnlyList<ServerConfig>>(new[] { config }).AddSingleton(policy).AddScoped<HeaderProvider>();

    private sealed class CaptureHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
