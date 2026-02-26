using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.UnrealSpeech;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUnrealSpeech(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var key = headers?
            .FirstOrDefault(h => h.Key.Contains("unrealspeech.com", StringComparison.OrdinalIgnoreCase))
            .Value?
            .FirstOrDefault(h => h.Key.Equals(HeaderNames.Authorization, StringComparison.OrdinalIgnoreCase))
            .Value?
            .GetBearerToken();

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new UnrealSpeechSettings { ApiKey = key });
        return services;
    }
}

public sealed class UnrealSpeechSettings
{
    public string ApiKey { get; set; } = default!;
}

