using System.Net.Http.Headers;
using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.AgentSandbox;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAgentSandbox(
        this IServiceCollection services,
        Dictionary<string, Dictionary<string, string>>? headers)
    {
        static string? GetBearer(
            Dictionary<string, Dictionary<string, string>>? source,
            string host)
            => source?
                .FirstOrDefault(entry => entry.Key.Equals(host, StringComparison.OrdinalIgnoreCase))
                .Value?
                .FirstOrDefault(entry => entry.Key.Equals(HeaderNames.Authorization, StringComparison.OrdinalIgnoreCase))
                .Value?
                .GetBearerToken();

        var apiKey = GetBearer(headers, "api.agentsandbox.co")
            ?? GetBearer(headers, "agentsandbox.co");

        if (string.IsNullOrWhiteSpace(apiKey))
            return services;

        services.AddSingleton(new AgentSandboxSettings
        {
            ApiKey = apiKey.Trim()
        });

        services.AddHttpClient<AgentSandboxClient>((serviceProvider, client) =>
        {
            var settings = serviceProvider.GetRequiredService<AgentSandboxSettings>();
            client.BaseAddress = new Uri(settings.BaseUrl);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });

        return services;
    }
}
