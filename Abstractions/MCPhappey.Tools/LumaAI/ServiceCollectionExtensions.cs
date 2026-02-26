using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.LumaAI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLumaAI(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        static string? GetBearerFrom(Dictionary<string, Dictionary<string, string>>? src, string host)
            => src?
                .FirstOrDefault(h => h.Key.Equals(host, StringComparison.OrdinalIgnoreCase))
                .Value?
                .FirstOrDefault(h => h.Key.Equals(HeaderNames.Authorization, StringComparison.OrdinalIgnoreCase))
                .Value?
                .GetBearerToken();

        var key = GetBearerFrom(headers, "api.lumalabs.ai")
            ?? GetBearerFrom(headers, "lumalabs.ai");

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new LumaAISettings { ApiKey = key });
        services.AddHttpClient<LumaAIClient>();

        return services;
    }
}

