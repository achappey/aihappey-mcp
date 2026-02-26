using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.Daglo;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDaglo(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var key = headers?
            .FirstOrDefault(h => h.Key == "apis.daglo.ai")
            .Value?
            .FirstOrDefault(h => h.Key.Equals(HeaderNames.Authorization, StringComparison.OrdinalIgnoreCase))
            .Value?
            .GetBearerToken();

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new DagloSettings { ApiKey = key });
        services.AddHttpClient<DagloClient>();
        return services;
    }
}

public sealed class DagloSettings
{
    public string ApiKey { get; set; } = default!;
}

