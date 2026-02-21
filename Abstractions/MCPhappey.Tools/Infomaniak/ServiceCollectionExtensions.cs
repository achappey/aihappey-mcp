using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.Infomaniak;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfomaniak(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var domainHeaders = headers?
            .FirstOrDefault(h => h.Key == "api.infomaniak.com")
            .Value;

        var key = domainHeaders?
            .FirstOrDefault(h => h.Key.Equals(HeaderNames.Authorization, StringComparison.OrdinalIgnoreCase))
            .Value?
            .GetBearerToken();

        if (string.IsNullOrWhiteSpace(key))
            return services;

        int? defaultProductId = null;
        var configuredProductId = domainHeaders?
            .FirstOrDefault(h => h.Key.Equals("x-infomaniak-product-id", StringComparison.OrdinalIgnoreCase))
            .Value;

        if (int.TryParse(configuredProductId, out var parsedProductId))
            defaultProductId = parsedProductId;

        services.AddSingleton(new InfomaniakSettings
        {
            ApiKey = key,
            DefaultProductId = defaultProductId
        });

        return services;
    }
}

public sealed class InfomaniakSettings
{
    public string ApiKey { get; set; } = default!;
    public int? DefaultProductId { get; set; }
}

