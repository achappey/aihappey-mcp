using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace MCPhappey.Tools.Ideogram;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIdeogram(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var headerValues = headers?
            .FirstOrDefault(h => h.Key.Equals("api.ideogram.ai", StringComparison.OrdinalIgnoreCase))
            .Value;

        var apiKey = headerValues?
            .FirstOrDefault(h => h.Key.Equals("Api-Key", StringComparison.OrdinalIgnoreCase))
            .Value;

        apiKey = apiKey?.GetBearerToken() ?? apiKey;

        if (string.IsNullOrWhiteSpace(apiKey))
            return services;

        services.AddSingleton(new IdeogramSettings
        {
            ApiKey = apiKey
        });

        return services;
    }
}

public sealed class IdeogramSettings
{
    public string ApiKey { get; set; } = default!;
}
