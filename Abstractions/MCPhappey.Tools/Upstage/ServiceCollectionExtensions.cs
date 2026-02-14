using System.Net.Http.Headers;
using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.Upstage;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUpstage(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var key = headers?
            .FirstOrDefault(h => h.Key == "api.upstage.ai")
            .Value?
            .FirstOrDefault(h => h.Key == HeaderNames.Authorization)
            .Value?
            .GetBearerToken();

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new UpstageSettings { ApiKey = key });

        services.AddHttpClient<UpstageClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<UpstageSettings>();
            client.BaseAddress = new Uri("https://api.upstage.ai/v1/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        });

        return services;
    }
}

