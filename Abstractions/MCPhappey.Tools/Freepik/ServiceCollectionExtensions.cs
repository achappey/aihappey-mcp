using Microsoft.Extensions.DependencyInjection;

namespace MCPhappey.Tools.Freepik;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFreepik(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var apiKey = headers?
            .FirstOrDefault(h => h.Key == "api.freepik.com")
            .Value?
            .FirstOrDefault(h => h.Key == "x-freepik-api-key")
            .Value;

        if (string.IsNullOrWhiteSpace(apiKey))
            return services;

        services.AddSingleton(new FreepikSettings { ApiKey = apiKey });

        services.AddHttpClient<FreepikClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<FreepikSettings>();
            client.BaseAddress = new Uri("https://api.freepik.com/");
            client.DefaultRequestHeaders.Add("x-freepik-api-key", settings.ApiKey);
        });

        return services;
    }
}
