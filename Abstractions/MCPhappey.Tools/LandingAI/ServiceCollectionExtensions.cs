using System.Net.Http.Headers;
using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.LandingAI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLandingAI(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var key = headers?
            .FirstOrDefault(h => h.Key == "api.va.landing.ai")
            .Value?
            .FirstOrDefault(h => h.Key == HeaderNames.Authorization)
            .Value?
            .GetBearerToken();

        if (string.IsNullOrWhiteSpace(key))
            key = headers?
                .FirstOrDefault(h => h.Key == "api.va.eu-west-1.landing.ai")
                .Value?
                .FirstOrDefault(h => h.Key == HeaderNames.Authorization)
                .Value?
                .GetBearerToken();

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new LandingAISettings { ApiKey = key });
        return services;
    }

    public static HttpClient CreateLandingAIClient(this IServiceProvider serviceProvider, string region = "us")
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var settings = serviceProvider.GetRequiredService<LandingAISettings>();
        var factory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        var normalizedRegion = (region ?? "us").Trim().ToLowerInvariant();
        var baseUrl = normalizedRegion == "eu"
            ? "https://api.va.eu-west-1.landing.ai/v1/"
            : "https://api.va.landing.ai/v1/";

        var client = factory.CreateClient();
        client.BaseAddress = new Uri(baseUrl);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return client;
    }
}

public sealed class LandingAISettings
{
    public string ApiKey { get; set; } = default!;
}

