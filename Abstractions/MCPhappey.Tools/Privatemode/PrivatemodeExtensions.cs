using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.Privatemode;

public static class PrivatemodeExtensions
{
    public static HttpClient CreatePrivatemodeClient(this IServiceProvider serviceProvider, string accept = MimeTypes.Json)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var settings = serviceProvider.GetRequiredService<PrivatemodeSettings>();
        var factory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        var client = factory.CreateClient();
        client.BaseAddress ??= new Uri("https://api.privatemode.ai/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));

        return client;
    }
}

