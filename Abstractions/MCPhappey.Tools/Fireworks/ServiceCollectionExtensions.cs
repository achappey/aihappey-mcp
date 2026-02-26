using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.Fireworks;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFireworks(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var key = GetApiKey(headers);

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new FireworksSettings { ApiKey = key });
        return services;
    }

    private static string? GetApiKey(Dictionary<string, Dictionary<string, string>>? headers)
    {
        var hostKeys = new[]
        {
            "api.fireworks.ai",
            "audio-prod.api.fireworks.ai",
            "audio-turbo.api.fireworks.ai"
        };

        foreach (var host in hostKeys)
        {
            var headerMap = headers?
                .FirstOrDefault(h => h.Key.Equals(host, StringComparison.OrdinalIgnoreCase))
                .Value;

            if (headerMap == null)
                continue;

            var auth = headerMap
                .FirstOrDefault(h => h.Key.Equals(HeaderNames.Authorization, StringComparison.OrdinalIgnoreCase))
                .Value;

            if (string.IsNullOrWhiteSpace(auth))
                continue;

            var bearer = auth.GetBearerToken();
            if (!string.IsNullOrWhiteSpace(bearer))
                return bearer;

            return auth.Trim();
        }

        return null;
    }
}

public sealed class FireworksSettings
{
    public string ApiKey { get; set; } = default!;
}
