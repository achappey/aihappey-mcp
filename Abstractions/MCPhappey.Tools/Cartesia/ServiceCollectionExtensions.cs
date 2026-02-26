using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.Cartesia;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCartesia(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var key = headers?
            .FirstOrDefault(h => h.Key.Equals("api.cartesia.ai", StringComparison.OrdinalIgnoreCase))
            .Value?
            .FirstOrDefault(h => h.Key.Equals(HeaderNames.Authorization, StringComparison.OrdinalIgnoreCase))
            .Value?
            .GetBearerToken();

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new CartesiaSettings
        {
            ApiKey = key,
            ApiVersion = "2025-04-16"
        });

        return services;
    }
}

public sealed class CartesiaSettings
{
    public string ApiKey { get; set; } = default!;
    public string ApiVersion { get; set; } = "2025-04-16";
}

