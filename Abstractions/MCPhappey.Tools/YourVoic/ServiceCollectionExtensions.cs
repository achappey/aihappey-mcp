using Microsoft.Extensions.DependencyInjection;

namespace MCPhappey.Tools.YourVoic;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddYourVoic(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var domainHeaders = headers?
            .FirstOrDefault(h => h.Key.Equals("yourvoic.com", StringComparison.OrdinalIgnoreCase))
            .Value
            ?? headers?
                .FirstOrDefault(h => h.Key.Equals("api.yourvoic.com", StringComparison.OrdinalIgnoreCase))
                .Value;

        var key = domainHeaders?
            .FirstOrDefault(h => h.Key.Equals("X-API-Key", StringComparison.OrdinalIgnoreCase))
            .Value;

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new YourVoicSettings { ApiKey = key });
        return services;
    }
}

public sealed class YourVoicSettings
{
    public string ApiKey { get; set; } = default!;
}
