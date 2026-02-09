using Microsoft.Extensions.DependencyInjection;

namespace MCPhappey.Tools.Gladia;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGladia(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var key = headers?
            .FirstOrDefault(h => h.Key == "api.gladia.io")
            .Value?
            .FirstOrDefault(h => h.Key.Equals("x-gladia-key", StringComparison.OrdinalIgnoreCase))
            .Value;

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new GladiaSettings { ApiKey = key });
        return services;
    }
}

public class GladiaSettings
{
    public string ApiKey { get; set; } = default!;
}

