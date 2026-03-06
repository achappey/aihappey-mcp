using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.NimbleWay;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNimbleWay(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        static string? GetBearerFrom(Dictionary<string, Dictionary<string, string>>? src, string host)
            => src?
                .FirstOrDefault(h => h.Key.Equals(host, StringComparison.OrdinalIgnoreCase))
                .Value?
                .FirstOrDefault(h => h.Key.Equals(HeaderNames.Authorization, StringComparison.OrdinalIgnoreCase))
                .Value?
                .GetBearerToken();

        var key = GetBearerFrom(headers, "sdk.nimbleway.com")
            ?? GetBearerFrom(headers, "api.nimbleway.com")
            ?? GetBearerFrom(headers, "nimbleway.com");

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new NimbleWaySettings { ApiKey = key });
        return services;
    }
}

public sealed class NimbleWaySettings
{
    public string ApiKey { get; set; } = default!;
}

