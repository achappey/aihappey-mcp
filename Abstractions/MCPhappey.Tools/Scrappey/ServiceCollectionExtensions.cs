using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.Scrappey;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddScrappey(
        this IServiceCollection services,
        Dictionary<string, Dictionary<string, string>>? headers,
        Dictionary<string, Dictionary<string, string>>? queries = null)
    {
        static string? GetApiKeyFromHeaders(Dictionary<string, Dictionary<string, string>>? src, string host)
            => src?
                .FirstOrDefault(h => h.Key.Equals(host, StringComparison.OrdinalIgnoreCase))
                .Value?
                .FirstOrDefault(h => h.Key.Equals("X-API-Key", StringComparison.OrdinalIgnoreCase))
                .Value;

        static string? GetBearerFromHeaders(Dictionary<string, Dictionary<string, string>>? src, string host)
            => src?
                .FirstOrDefault(h => h.Key.Equals(host, StringComparison.OrdinalIgnoreCase))
                .Value?
                .FirstOrDefault(h => h.Key.Equals(HeaderNames.Authorization, StringComparison.OrdinalIgnoreCase))
                .Value?
                .GetBearerToken();

        static string? GetApiKeyFromQueries(Dictionary<string, Dictionary<string, string>>? src, string host)
            => src?
                .FirstOrDefault(h => h.Key.Equals(host, StringComparison.OrdinalIgnoreCase))
                .Value?
                .FirstOrDefault(h => h.Key.Equals("key", StringComparison.OrdinalIgnoreCase))
                .Value;

        var key = GetApiKeyFromQueries(queries, "publisher.scrappey.com")
            ?? GetApiKeyFromHeaders(headers, "publisher.scrappey.com")
            ?? GetApiKeyFromHeaders(headers, "scrappey.com")
            ?? GetBearerFromHeaders(headers, "publisher.scrappey.com")
            ?? GetBearerFromHeaders(headers, "scrappey.com");

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new ScrappeyApiSettings { ApiKey = key.Trim() });
        services.AddHttpClient<ScrappeyClient>((sp, client) =>
        {
            client.BaseAddress = new Uri("https://publisher.scrappey.com/");
            client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        });

        return services;
    }
}
