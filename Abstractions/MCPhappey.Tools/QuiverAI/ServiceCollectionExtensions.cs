using System.Net.Http.Headers;
using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.QuiverAI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddQuiverAI(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var key = headers?
            .FirstOrDefault(h => h.Key == "api.quiver.ai")
            .Value?
            .FirstOrDefault(h => h.Key == HeaderNames.Authorization)
            .Value?
            .GetBearerToken();

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new QuiverAISettings { ApiKey = key });

        services.AddHttpClient<QuiverAIClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<QuiverAISettings>();
            client.BaseAddress = new Uri("https://api.quiver.ai/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        });

        return services;
    }
}

