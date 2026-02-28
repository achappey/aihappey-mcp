using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.TinyFish;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTinyFish(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var domainHeaders = headers?
            .FirstOrDefault(h => h.Key.Equals("agent.tinyfish.ai", StringComparison.OrdinalIgnoreCase))
            .Value
            ?? headers?
                .FirstOrDefault(h => h.Key.Equals("tinyfish.ai", StringComparison.OrdinalIgnoreCase))
                .Value;

        var xApiKey = domainHeaders?
            .FirstOrDefault(h => h.Key.Equals("X-API-Key", StringComparison.OrdinalIgnoreCase))
            .Value;

        var auth = domainHeaders?
            .FirstOrDefault(h => h.Key.Equals(HeaderNames.Authorization, StringComparison.OrdinalIgnoreCase))
            .Value;

        var bearer = string.IsNullOrWhiteSpace(auth) ? null : auth.GetBearerToken();
        var key = !string.IsNullOrWhiteSpace(xApiKey)
            ? xApiKey.Trim()
            : !string.IsNullOrWhiteSpace(bearer)
                ? bearer.Trim()
                : null;

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new TinyFishSettings { ApiKey = key });
        return services;
    }
}

public sealed class TinyFishSettings
{
    public string ApiKey { get; set; } = default!;
}

