using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using System.Net.Http.Headers;

namespace MCPhappey.Tools.Rijkswaterstaat;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRijkswaterstaat(this IServiceCollection services)
    {
        services.AddHttpClient<WaterDataClient>((_, client) =>
        {
            client.BaseAddress = new Uri("https://waterwebservices.beta.rijkswaterstaat.nl/test/");
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
        });

        return services;
    }
}
