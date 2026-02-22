using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.ExtendAI;

public static class ExtendAIExtensions
{
    public static IServiceCollection AddExtendAI(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var key = headers?
            .FirstOrDefault(h => h.Key == "api.extend.ai")
            .Value?
            .FirstOrDefault(h => h.Key == HeaderNames.Authorization)
            .Value?
            .GetBearerToken();

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new ExtendAISettings { ApiKey = key });

        services.AddHttpClient<ExtendAIClient>();
        services.AddSingleton<ExtendAIFileService>();

        return services;
    }
}
