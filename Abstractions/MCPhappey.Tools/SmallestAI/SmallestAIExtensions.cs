using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.SmallestAI;

public static class SmallestAIExtensions
{
    public static HttpClient CreateSmallestAIClient(this IServiceProvider serviceProvider, string accept = MimeTypes.Json)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var settings = serviceProvider.GetRequiredService<SmallestAISettings>();
        var factory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        var client = factory.CreateClient();
        client.BaseAddress ??= new Uri("https://waves-api.smallest.ai/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));

        return client;
    }
}

