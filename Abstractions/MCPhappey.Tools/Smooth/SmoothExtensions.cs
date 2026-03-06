using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;

namespace MCPhappey.Tools.Smooth;

public static class SmoothExtensions
{
    public static HttpClient CreateSmoothClient(this IServiceProvider serviceProvider, string accept = "application/json")
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var settings = serviceProvider.GetRequiredService<SmoothSettings>();
        var factory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        var client = factory.CreateClient();
        client.BaseAddress ??= new Uri("https://api.smooth.sh/api/v1/");
        client.DefaultRequestHeaders.Remove("apikey");
        client.DefaultRequestHeaders.Add("apikey", settings.ApiKey);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));

        return client;
    }
}

