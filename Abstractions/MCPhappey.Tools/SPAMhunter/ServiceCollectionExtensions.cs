using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.SPAMhunter;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSPAMhunter(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var key = headers?
            .FirstOrDefault(h => h.Key == "api.spamhunter.io")
            .Value?
            .FirstOrDefault(h => h.Key == "X-API-Key")
            .Value;

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new SPAMhunterSettings { ApiKey = key });

        services.AddHttpClient<SPAMhunterClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<SPAMhunterSettings>();
            client.BaseAddress = new Uri("https://api.spamhunter.io/v1/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("X-API-Key", settings.ApiKey);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
        });

        return services;
    }
}
