using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.Deepgram;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDeepgram(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var auth = headers?
            .FirstOrDefault(h => h.Key == "api.deepgram.com")
            .Value?
            .FirstOrDefault(h => h.Key.Equals(HeaderNames.Authorization, StringComparison.OrdinalIgnoreCase))
            .Value;

        if (string.IsNullOrWhiteSpace(auth))
            return services;

        services.AddSingleton(new DeepgramSettings { ApiKey = auth });
        return services;
    }
}

public class DeepgramSettings
{
    public string ApiKey { get; set; } = default!;
}

