using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;

namespace MCPhappey.Tools.TinyFish;

public static class TinyFishExtensions
{
    public static HttpClient CreateTinyFishClient(this IServiceProvider serviceProvider, string accept = "text/event-stream")
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var settings = serviceProvider.GetRequiredService<TinyFishSettings>();
        var factory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        var client = factory.CreateClient();
        client.BaseAddress ??= new Uri("https://agent.tinyfish.ai/");
        client.DefaultRequestHeaders.Remove("X-API-Key");
        client.DefaultRequestHeaders.Add("X-API-Key", settings.ApiKey);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));

        return client;
    }
}

