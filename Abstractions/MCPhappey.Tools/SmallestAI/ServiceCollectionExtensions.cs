using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.SmallestAI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSmallestAI(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var key = headers?
            .FirstOrDefault(h => h.Key.Equals("waves-api.smallest.ai", StringComparison.OrdinalIgnoreCase))
            .Value?
            .FirstOrDefault(h => h.Key.Equals(HeaderNames.Authorization, StringComparison.OrdinalIgnoreCase))
            .Value?
            .GetBearerToken();

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new SmallestAISettings { ApiKey = key });
        return services;
    }
}

public sealed class SmallestAISettings
{
    public string ApiKey { get; set; } = default!;
}

