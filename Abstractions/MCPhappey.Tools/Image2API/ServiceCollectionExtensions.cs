using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.Image2API;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddImage2API(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        static string? GetBearerFrom(Dictionary<string, Dictionary<string, string>>? src, string host)
            => src?
                .FirstOrDefault(h => h.Key.Equals(host, StringComparison.OrdinalIgnoreCase))
                .Value?
                .FirstOrDefault(h => h.Key.Equals(HeaderNames.Authorization, StringComparison.OrdinalIgnoreCase))
                .Value?
                .GetBearerToken();

        var key = GetBearerFrom(headers, "image2api.kastana.software")
            ?? GetBearerFrom(headers, "www.image2api.kastana.software");

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new Image2APISettings { ApiKey = key.Trim() });
        services.AddHttpClient<Image2APIClient>();

        return services;
    }
}
