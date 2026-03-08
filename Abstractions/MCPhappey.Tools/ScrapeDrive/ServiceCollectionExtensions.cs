using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.ScrapeDrive;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddScrapeDrive(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        static string? GetApiKeyFrom(Dictionary<string, Dictionary<string, string>>? src, string host)
            => src?
                .FirstOrDefault(h => h.Key.Equals(host, StringComparison.OrdinalIgnoreCase))
                .Value?
                .FirstOrDefault(h => h.Key.Equals("X-API-Key", StringComparison.OrdinalIgnoreCase))
                .Value;

        static string? GetBearerFrom(Dictionary<string, Dictionary<string, string>>? src, string host)
            => src?
                .FirstOrDefault(h => h.Key.Equals(host, StringComparison.OrdinalIgnoreCase))
                .Value?
                .FirstOrDefault(h => h.Key.Equals(HeaderNames.Authorization, StringComparison.OrdinalIgnoreCase))
                .Value?
                .GetBearerToken();

        var key = GetApiKeyFrom(headers, "api.scrapedrive.com")
            ?? GetApiKeyFrom(headers, "scrapedrive.com")
            ?? GetApiKeyFrom(headers, "sync.scrapedrive.com")
            ?? GetBearerFrom(headers, "api.scrapedrive.com")
            ?? GetBearerFrom(headers, "scrapedrive.com")
            ?? GetBearerFrom(headers, "sync.scrapedrive.com");

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new ScrapeDriveSettings { ApiKey = key.Trim() });
        services.AddHttpClient<ScrapeDriveClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<ScrapeDriveSettings>();
            client.BaseAddress = new Uri("https://api.scrapedrive.com/api/v1/");
            client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Add("X-API-Key", settings.ApiKey);
        });

        return services;
    }
}

public sealed class ScrapeDriveSettings
{
    public string ApiKey { get; set; } = default!;
}
