using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.EuropeanUnion;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEuropeanUnionVies(this IServiceCollection services)
    {
        services.AddHttpClient<EuropeanUnionClient>(client =>
        {
            client.BaseAddress = new Uri("https://ec.europa.eu/taxation_customs/vies/rest-api/");
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
        });

        return services;
    }
}
