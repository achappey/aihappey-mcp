using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.Kirha;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKirha(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        static string? GetBearerFrom(Dictionary<string, Dictionary<string, string>>? src, string host)
            => src?
                .FirstOrDefault(h => h.Key.Equals(host, StringComparison.OrdinalIgnoreCase))
                .Value?
                .FirstOrDefault(h => h.Key.Equals(HeaderNames.Authorization, StringComparison.OrdinalIgnoreCase))
                .Value?
                .GetBearerToken();

        var key = GetBearerFrom(headers, "api.kirha.ai")
            ?? GetBearerFrom(headers, "api.kirha.com")
            ?? GetBearerFrom(headers, "kirha.ai")
            ?? GetBearerFrom(headers, "kirha.com");

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new KirhaSettings { ApiKey = key.Trim() });
        services.AddHttpClient<KirhaClient>();

        return services;
    }
}

public sealed class KirhaSettings
{
    public string ApiKey { get; set; } = default!;
}
