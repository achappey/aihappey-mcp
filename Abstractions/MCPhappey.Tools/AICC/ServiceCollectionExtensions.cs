using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.AICC;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAICC(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        static string? GetBearerFrom(Dictionary<string, Dictionary<string, string>>? src, string host)
            => src?
                .FirstOrDefault(h => h.Key.Equals(host, StringComparison.OrdinalIgnoreCase))
                .Value?
                .FirstOrDefault(h => h.Key.Equals(HeaderNames.Authorization, StringComparison.OrdinalIgnoreCase))
                .Value?
                .GetBearerToken();

        var key = GetBearerFrom(headers, "api.ai.cc");

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new AICCSettings { ApiKey = key });
        services.AddHttpClient<AICCClient>();

        return services;
    }
}

