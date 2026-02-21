using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.Decart;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDecart(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var domainHeaders = headers?
            .FirstOrDefault(h => h.Key.Equals("api.decart.ai", StringComparison.OrdinalIgnoreCase))
            .Value;

        var key = domainHeaders?
            .FirstOrDefault(h => h.Key.Equals("x-api-key", StringComparison.OrdinalIgnoreCase))
            .Value;

        key ??= domainHeaders?
            .FirstOrDefault(h => h.Key.Equals(HeaderNames.Authorization, StringComparison.OrdinalIgnoreCase))
            .Value?
            .GetBearerToken();

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new DecartSettings
        {
            ApiKey = key
        });

        return services;
    }
}

