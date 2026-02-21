using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.Parasail;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddParasail(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        static string? GetBearerFrom(Dictionary<string, Dictionary<string, string>>? src, string host)
            => src?
                .FirstOrDefault(h => h.Key.Equals(host, StringComparison.OrdinalIgnoreCase))
                .Value?
                .FirstOrDefault(h => h.Key.Equals(HeaderNames.Authorization, StringComparison.OrdinalIgnoreCase))
                .Value?
                .GetBearerToken();

        var key = GetBearerFrom(headers, "api.parasail.io")
            ?? GetBearerFrom(headers, "parasail.io");

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new ParasailSettings { ApiKey = key });
        return services;
    }
}

public sealed class ParasailSettings
{
    public string ApiKey { get; set; } = default!;
}

