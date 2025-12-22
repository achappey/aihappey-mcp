using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.AzureMaps;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAzureMaps(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var key = headers?
            .FirstOrDefault(h => h.Key == "atlas.microsoft.com")
            .Value?
            .FirstOrDefault(h => h.Key == "Subscription-Key")
            .Value;

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new AzureMapsSettings { ApiKey = key });

        services.AddHttpClient<AzureMapsClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<AzureMapsSettings>();
            client.BaseAddress = new Uri("https://atlas.microsoft.com/");
            client.DefaultRequestHeaders.Add("Subscription-Key", settings.ApiKey);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
        });

        return services;
    }
}
