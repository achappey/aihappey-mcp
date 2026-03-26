using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.ImageRouter;

public static class ImageRouterExtensions
{
    public static HttpClient CreateImageRouterClient(this IServiceProvider serviceProvider, string accept = MimeTypes.Json)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var settings = serviceProvider.GetRequiredService<ImageRouterSettings>();
        var factory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        var client = factory.CreateClient();
        client.BaseAddress ??= new Uri("https://api.imagerouter.io/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));

        return client;
    }
}

