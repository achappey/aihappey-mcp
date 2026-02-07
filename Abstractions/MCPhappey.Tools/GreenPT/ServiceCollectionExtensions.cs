using System.Net.Http.Headers;
using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.GreenPT;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGreenPT(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var key = headers?
            .FirstOrDefault(h => h.Key == "api.greenpt.ai")
            .Value?
            .FirstOrDefault(h => h.Key == HeaderNames.Authorization)
            .Value?
            .GetBearerToken();

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new GreenPTSettings { ApiKey = key });

        services.AddHttpClient<GreenPTClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<GreenPTSettings>();
            client.BaseAddress = new Uri("https://api.greenpt.ai/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        });

        return services;
    }
}

