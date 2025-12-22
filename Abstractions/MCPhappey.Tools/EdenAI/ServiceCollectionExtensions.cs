using System.Net.Http.Headers;
using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.EdenAI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEdenAI(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var key = headers?
            .FirstOrDefault(h => h.Key == "api.edenai.run")
            .Value?
            .FirstOrDefault(h => h.Key == HeaderNames.Authorization)
            .Value?
            .GetBearerToken();

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new EdenAISettings { ApiKey = key });

        services.AddHttpClient<EdenAIClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<EdenAISettings>();
            client.BaseAddress = new Uri("https://api.edenai.run/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        });

        return services;
    }
}
