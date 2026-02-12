using System.Net.Http.Headers;
using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.DeepL;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDeepL(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var proKey = headers?
            .FirstOrDefault(h => h.Key == "api.deepl.com")
            .Value?
            .FirstOrDefault(h => h.Key == HeaderNames.Authorization)
            .Value?
            .GetBearerToken();

        var freeKey = headers?
            .FirstOrDefault(h => h.Key == "api-free.deepl.com")
            .Value?
            .FirstOrDefault(h => h.Key == HeaderNames.Authorization)
            .Value?
            .GetBearerToken();

        var key = !string.IsNullOrWhiteSpace(proKey) ? proKey : freeKey;

        if (string.IsNullOrWhiteSpace(key))
            return services;

        var baseUrl = !string.IsNullOrWhiteSpace(proKey)
            ? "https://api.deepl.com/"
            : "https://api-free.deepl.com/";

        services.AddSingleton(new DeepLSettings
        {
            ApiKey = key,
            BaseUrl = baseUrl
        });

        services.AddHttpClient<DeepLClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<DeepLSettings>();
            client.BaseAddress = new Uri(settings.BaseUrl);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("DeepL-Auth-Key", settings.ApiKey);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });

        return services;
    }
}

