using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MCPhappey.Tools.BlackForestLabs;

public sealed class BlackForestLabsSettings
{
    public string ApiKey { get; set; } = string.Empty;
}

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBlackForestLabs(this IServiceCollection services,
        Dictionary<string, Dictionary<string, string>>? headers = null)
    {
        var key = headers?.FirstOrDefault(host => host.Key.Equals("api.bfl.ai", StringComparison.OrdinalIgnoreCase))
            .Value?.FirstOrDefault(header => header.Key.Equals("x-key", StringComparison.OrdinalIgnoreCase)).Value;
        if (!string.IsNullOrWhiteSpace(key))
            services.AddSingleton(new BlackForestLabsSettings { ApiKey = key.Trim() });
        if (services.Any(descriptor => descriptor.ServiceType == typeof(Registration))) return services;
        services.AddSingleton<Registration>();
        services.TryAddSingleton<PublicFileDownloader>();
        services.AddHttpClient<BlackForestLabsClient>(client =>
        {
            client.BaseAddress = new Uri(BlackForestLabsClient.BaseUrl);
            client.Timeout = TimeSpan.FromMinutes(2);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        return services;
    }

    private sealed class Registration;
}
