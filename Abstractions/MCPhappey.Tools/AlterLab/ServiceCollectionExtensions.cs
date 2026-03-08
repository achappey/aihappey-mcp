using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.AlterLab;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAlterLab(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
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

        var key = GetApiKeyFrom(headers, "api.alterlab.io")
            ?? GetApiKeyFrom(headers, "alterlab.io")
            ?? GetBearerFrom(headers, "api.alterlab.io")
            ?? GetBearerFrom(headers, "alterlab.io");

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new AlterLabSettings { ApiKey = key.Trim() });
        services.AddHttpClient<AlterLabClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<AlterLabSettings>();
            client.BaseAddress = new Uri("https://api.alterlab.io/api/v1/");
            client.DefaultRequestHeaders.Add("X-API-Key", settings.ApiKey);
            client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        });

        return services;
    }
}

public sealed class AlterLabSettings
{
    public string ApiKey { get; set; } = default!;
}
