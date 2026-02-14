using System.Net.Http.Headers;
using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.Scaleway;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddScaleway(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var key = headers?
            .FirstOrDefault(h => h.Key == "api.scaleway.ai")
            .Value?
            .FirstOrDefault(h => h.Key == HeaderNames.Authorization)
            .Value?
            .GetBearerToken();

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new ScalewaySettings { ApiKey = key });

        services.AddHttpClient<ScalewayClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<ScalewaySettings>();
            client.BaseAddress = new Uri("https://api.scaleway.ai/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        });

        return services;
    }
}

