using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.Monica;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMonica(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var key = headers?
            .FirstOrDefault(h => h.Key == "openapi.monica.im")
            .Value?
            .FirstOrDefault(h => h.Key.Equals(HeaderNames.Authorization, StringComparison.OrdinalIgnoreCase))
            .Value?
            .GetBearerToken();

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new MonicaSettings
        {
            ApiKey = key
        });

        return services;
    }
}

public sealed class MonicaSettings
{
    public string ApiKey { get; set; } = default!;
}

