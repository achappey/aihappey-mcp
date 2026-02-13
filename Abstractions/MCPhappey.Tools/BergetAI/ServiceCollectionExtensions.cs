using System.Net.Http.Headers;
using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.BergetAI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBergetAI(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var key = headers?
            .FirstOrDefault(h => h.Key == "api.berget.ai")
            .Value?
            .FirstOrDefault(h => h.Key == HeaderNames.Authorization)
            .Value?
            .GetBearerToken();

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new BergetAISettings { ApiKey = key });

        services.AddHttpClient<BergetAIClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<BergetAISettings>();
            client.BaseAddress = new Uri("https://api.berget.ai/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        });

        return services;
    }
}

