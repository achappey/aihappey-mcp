using System.Net.Http.Headers;
using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.Loreto;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLoreto(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var key = headers?
            .FirstOrDefault(h => h.Key.Equals("api.loreto.io", StringComparison.OrdinalIgnoreCase))
            .Value?
            .FirstOrDefault(h => h.Key == HeaderNames.Authorization)
            .Value?
            .GetBearerToken();

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new LoretoSettings { ApiKey = key.Trim() });

        services.AddHttpClient<LoretoClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<LoretoSettings>();
            client.BaseAddress = new Uri("https://api.loreto.io/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });

        return services;
    }
}
