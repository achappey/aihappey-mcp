using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.Privatemode;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPrivatemode(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var domainHeaders = headers?
            .FirstOrDefault(h => h.Key.Equals("api.privatemode.ai", StringComparison.OrdinalIgnoreCase))
            .Value
            ?? headers?
                .FirstOrDefault(h => h.Key.Equals("privatemode.ai", StringComparison.OrdinalIgnoreCase))
                .Value;

        var key = domainHeaders?
            .FirstOrDefault(h => h.Key.Equals(HeaderNames.Authorization, StringComparison.OrdinalIgnoreCase))
            .Value?
            .GetBearerToken();

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new PrivatemodeSettings
        {
            ApiKey = key
        });

        return services;
    }
}

public sealed class PrivatemodeSettings
{
    public string ApiKey { get; set; } = default!;
}

