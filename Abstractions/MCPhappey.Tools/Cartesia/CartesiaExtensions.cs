using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.Cartesia;

public static class CartesiaExtensions
{
    public static HttpClient CreateCartesiaClient(this IServiceProvider serviceProvider, string accept = MimeTypes.Json)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var settings = serviceProvider.GetRequiredService<CartesiaSettings>();
        var factory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        var client = factory.CreateClient();
        client.BaseAddress ??= new Uri("https://api.cartesia.ai/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        client.DefaultRequestHeaders.Remove("Cartesia-Version");
        client.DefaultRequestHeaders.Add("Cartesia-Version", string.IsNullOrWhiteSpace(settings.ApiVersion) ? "2025-04-16" : settings.ApiVersion);

        return client;
    }
}

