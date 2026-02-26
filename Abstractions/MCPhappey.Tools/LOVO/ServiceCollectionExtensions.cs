using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.LOVO;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLOVO(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var domainHeaders = headers?
            .FirstOrDefault(h => h.Key.Equals("api.genny.lovo.ai", StringComparison.OrdinalIgnoreCase))
            .Value
            ?? headers?
                .FirstOrDefault(h => h.Key.Equals("genny.lovo.ai", StringComparison.OrdinalIgnoreCase))
                .Value;

        var key = domainHeaders?
            .FirstOrDefault(h => h.Key.Equals("X-API-KEY", StringComparison.OrdinalIgnoreCase))
            .Value;

        key ??= domainHeaders?
            .FirstOrDefault(h => h.Key.Equals(HeaderNames.Authorization, StringComparison.OrdinalIgnoreCase))
            .Value?
            .GetBearerToken();

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new LOVOSettings { ApiKey = key });
        return services;
    }
}

public sealed class LOVOSettings
{
    public string ApiKey { get; set; } = default!;
}

