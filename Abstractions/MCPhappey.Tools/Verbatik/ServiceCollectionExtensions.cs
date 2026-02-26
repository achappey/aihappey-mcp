using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.Verbatik;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVerbatik(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var key = headers?
            .FirstOrDefault(h => h.Key == "api.verbatik.com")
            .Value?
            .FirstOrDefault(h => h.Key.Equals(HeaderNames.Authorization, StringComparison.OrdinalIgnoreCase))
            .Value?
            .GetBearerToken();

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new VerbatikSettings { ApiKey = key });
        return services;
    }
}

public sealed class VerbatikSettings
{
    public string ApiKey { get; set; } = default!;
}

