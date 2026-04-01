using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.BlinkUtilities;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBlinkUtilities(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var key = headers?
            .FirstOrDefault(h => h.Key.Equals("core.blink.new", StringComparison.OrdinalIgnoreCase))
            .Value?
            .FirstOrDefault(h => h.Key.Equals(HeaderNames.Authorization, StringComparison.OrdinalIgnoreCase))
            .Value?
            .GetBearerToken();

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new BlinkUtilitiesSettings { ApiKey = key });
        return services;
    }
}

public sealed class BlinkUtilitiesSettings
{
    public string ApiKey { get; set; } = default!;
}

