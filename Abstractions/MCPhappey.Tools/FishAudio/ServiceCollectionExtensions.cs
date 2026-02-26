using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.FishAudio;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFishAudio(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var domainHeaders = headers?
            .FirstOrDefault(h => h.Key.Equals("api.fish.audio", StringComparison.OrdinalIgnoreCase))
            .Value
            ?? headers?
                .FirstOrDefault(h => h.Key.Equals("fish.audio", StringComparison.OrdinalIgnoreCase))
                .Value;

        var key = domainHeaders?
            .FirstOrDefault(h => h.Key.Equals(HeaderNames.Authorization, StringComparison.OrdinalIgnoreCase))
            .Value?
            .GetBearerToken();

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new FishAudioSettings
        {
            ApiKey = key
        });

        return services;
    }
}

public sealed class FishAudioSettings
{
    public string ApiKey { get; set; } = default!;
}

