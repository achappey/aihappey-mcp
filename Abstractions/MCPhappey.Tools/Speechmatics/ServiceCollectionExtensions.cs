using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.Speechmatics;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSpeechmatics(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var key = headers?
            .FirstOrDefault(h => h.Key == "asr.api.speechmatics.com")
            .Value?
            .FirstOrDefault(h => h.Key.Equals(HeaderNames.Authorization, StringComparison.OrdinalIgnoreCase))
            .Value?
            .GetBearerToken();

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new SpeechmaticsSettings { ApiKey = key });
        return services;
    }
}

public class SpeechmaticsSettings
{
    public string ApiKey { get; set; } = default!;
}

