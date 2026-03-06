using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.WebsearchAPI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWebsearchAPI(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var key = headers?
            .FirstOrDefault(h => h.Key.Equals("api.websearchapi.ai", StringComparison.OrdinalIgnoreCase))
            .Value?
            .FirstOrDefault(h => h.Key.Equals(HeaderNames.Authorization, StringComparison.OrdinalIgnoreCase))
            .Value?
            .GetBearerToken();

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new WebsearchAPISettings { ApiKey = key });
        return services;
    }
}

public sealed class WebsearchAPISettings
{
    public string ApiKey { get; set; } = default!;
}

