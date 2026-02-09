using System.Net.Http.Headers;
using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.OpperAI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOpperAI(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var key = headers?
            .FirstOrDefault(h => h.Key == "api.opper.ai")
            .Value?
            .FirstOrDefault(h => h.Key == HeaderNames.Authorization)
            .Value?
            .GetBearerToken();

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new OpperAISettings { ApiKey = key });

        services.AddHttpClient<OpperAIClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<OpperAISettings>();
            client.BaseAddress = new Uri("https://api.opper.ai/v2/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        });

        return services;
    }
}

