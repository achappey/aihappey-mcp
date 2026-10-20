using System.Net;
using System.Text;
using MCPhappey.Common;
using MCPhappey.Scrapers.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace MCPhappey.Tests;

public sealed class ResourceCredentialIsolationTests
{
    [Theory]
    [InlineData("Bearer service-api-key")]
    [InlineData("Bearer azure-delegated-token")]
    public async Task Dynamic_resource_sends_only_service_authentication_and_preserves_AzureAuth(string authorization)
    {
        var requests = 0;
        using var http = new HttpClient(new CaptureHandler(request =>
        {
            requests++;
            Assert.Equal(authorization, request.Headers.Authorization?.ToString());
            Assert.False(request.Headers.Contains("X-OpenAI-Key"));
            Assert.False(request.Headers.Contains("X-Anthropic-Key"));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        }));
        var headers = new HeaderProvider { Headers = new() { ["authorization"] = authorization, ["X-OpenAI-Key"] = "unrelated-openai", ["X-Anthropic-Key"] = "unrelated-anthropic" } };
        using var services = new ServiceCollection().AddSingleton(headers).BuildServiceProvider();
        var scraper = new DynamicHeaderScraper(new Factory(http));
        await scraper.GetContentAsync(null!, services, "https://app.declaree.com/api/v4/organizations");
        Assert.Equal(1, requests);
        Assert.Equal(3, headers.Headers.Count);
        await Assert.ThrowsAsync<ArgumentException>(() => scraper.GetContentAsync(null!, services, "https://other.test/resource"));
        Assert.Equal(1, requests);
    }

    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
    private sealed class CaptureHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
