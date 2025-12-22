using System.Net.Http.Headers;
using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.Runware;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRunware(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var key = headers?
            .FirstOrDefault(h => h.Key == "api.runware.ai")
            .Value?
            .FirstOrDefault(h => h.Key == HeaderNames.Authorization)
            .Value?
            .GetBearerToken();

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new RunwareSettings { ApiKey = key });

        services.AddHttpClient<RunwareClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<RunwareSettings>();
            client.BaseAddress = new Uri("https://api.runware.ai/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        });

        return services;
    }
}
