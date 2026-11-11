using System.Net.Http.Headers;
using MCPhappey.Core.Services;
using MCPhappey.Tools.Google.Agents;
using MCPhappey.Tools.Google.Interactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MCPhappey.Tools.Google;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGoogleAIFromHeaders(this IServiceCollection services,
        Dictionary<string, Dictionary<string, string>>? headers)
    {
        var key = headers?.FirstOrDefault(host => host.Key.Equals(GoogleProviderAccess.ApiHost, StringComparison.OrdinalIgnoreCase))
            .Value?.FirstOrDefault(header => header.Key.Equals(GoogleProviderAccess.ApiKeyHeader, StringComparison.OrdinalIgnoreCase)).Value;
        return services.AddGoogleAI(key);
    }

    public static IServiceCollection AddGoogleAI(this IServiceCollection services, string? apiKey = null)
    {
        if (!string.IsNullOrWhiteSpace(apiKey))
            services.AddSingleton(new GoogleSettings { ApiKey = apiKey.Trim() });

        if (services.Any(descriptor => descriptor.ServiceType == typeof(GoogleRegistration)))
            return services;
        services.AddSingleton<GoogleRegistration>();
        services.TryAddSingleton<PublicFileDownloader>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRequestContentDownloader, GoogleContentDownloader>());
        services.AddHttpClient<GoogleInteractionsClient>(ConfigureClient)
            .ConfigurePrimaryHttpMessageHandler(CreateHandler);
        services.AddHttpClient<GoogleAgentsClient>(ConfigureClient)
            .ConfigurePrimaryHttpMessageHandler(CreateHandler);
        return services;
    }

    private static void ConfigureClient(HttpClient client)
    {
        client.BaseAddress = new Uri(GoogleProviderAccess.BaseUrl);
        client.Timeout = TimeSpan.FromMinutes(15);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private static HttpMessageHandler CreateHandler()
        => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };

    private sealed class GoogleRegistration { }
}
