using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.Telnyx;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTelnyx(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var key = headers?
            .FirstOrDefault(h => h.Key == "api.telnyx.com")
            .Value?
            .FirstOrDefault(h => h.Key.Equals(HeaderNames.Authorization, StringComparison.OrdinalIgnoreCase))
            .Value?
            .GetBearerToken();

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new TelnyxSettings { ApiKey = key });
        return services;
    }
}

public class TelnyxSettings
{
    public string ApiKey { get; set; } = default!;
}

