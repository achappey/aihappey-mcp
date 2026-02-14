using System.Net.Http.Headers;
using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.SiliconFlow;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSiliconFlow(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var key = headers?
            .FirstOrDefault(h => h.Key == "api.siliconflow.com")
            .Value?
            .FirstOrDefault(h => h.Key == HeaderNames.Authorization)
            .Value?
            .GetBearerToken();

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new SiliconFlowSettings { ApiKey = key });

        services.AddHttpClient<SiliconFlowClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<SiliconFlowSettings>();
            client.BaseAddress = new Uri("https://api.siliconflow.com/v1/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        });

        return services;
    }
}

