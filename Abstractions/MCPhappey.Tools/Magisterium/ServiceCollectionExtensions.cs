using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.Magisterium;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMagisterium(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var key = headers?
            .FirstOrDefault(h => h.Key.Equals("www.magisterium.com", StringComparison.OrdinalIgnoreCase))
            .Value?
            .FirstOrDefault(h => h.Key.Equals(HeaderNames.Authorization, StringComparison.OrdinalIgnoreCase))
            .Value?
            .GetBearerToken();

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new MagisteriumSettings { ApiKey = key });
        return services;
    }
}

public sealed class MagisteriumSettings
{
    public string ApiKey { get; set; } = default!;
}
