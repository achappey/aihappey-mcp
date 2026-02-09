using Microsoft.Extensions.DependencyInjection;

namespace MCPhappey.Tools.Audixa;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAudixa(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var key = headers?
            .FirstOrDefault(h => h.Key == "api.audixa.ai")
            .Value?
            .FirstOrDefault(h => h.Key == "x-api-key")
            .Value;

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new AudixaSettings { ApiKey = key });
        return services;
    }
}
