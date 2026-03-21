using System.Net.Http.Headers;
using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.Relace;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRelace(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var apiKey = headers?
            .FirstOrDefault(h => h.Key == "api.relace.run")
            .Value?
            .FirstOrDefault(h => h.Key == HeaderNames.Authorization)
            .Value?
            .GetBearerToken();

        if (string.IsNullOrWhiteSpace(apiKey))
            return services;

        services.AddSingleton(new RelaceSettings
        {
            ApiKey = apiKey,
            BaseUrl = "https://api.relace.run/"
        });

        services.AddHttpClient<RelaceClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<RelaceSettings>();
            client.BaseAddress = new Uri(settings.BaseUrl);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });

        return services;
    }
}
