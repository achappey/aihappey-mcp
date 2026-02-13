using System.Net.Http.Headers;
using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.Recraft;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRecraft(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var key = headers?
            .FirstOrDefault(h => h.Key == "external.api.recraft.ai")
            .Value?
            .FirstOrDefault(h => h.Key == HeaderNames.Authorization)
            .Value?
            .GetBearerToken();

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new RecraftSettings { ApiKey = key });

        services.AddHttpClient<RecraftClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<RecraftSettings>();
            client.BaseAddress = new Uri("https://external.api.recraft.ai/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        });

        return services;
    }
}

