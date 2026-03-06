using Microsoft.Extensions.DependencyInjection;

namespace MCPhappey.Tools.Smooth;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSmooth(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var domainHeaders = headers?
            .FirstOrDefault(h => h.Key.Equals("api.smooth.sh", StringComparison.OrdinalIgnoreCase))
            .Value;

        var apiKey = domainHeaders?
            .FirstOrDefault(h => h.Key.Equals("apikey", StringComparison.OrdinalIgnoreCase))
            .Value?
            .Trim();

        if (string.IsNullOrWhiteSpace(apiKey))
            return services;

        services.AddSingleton(new SmoothSettings { ApiKey = apiKey });
        services.AddSingleton<SmoothClient>();
        return services;
    }
}

public sealed class SmoothSettings
{
    public string ApiKey { get; set; } = default!;
}

