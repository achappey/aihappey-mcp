using MCPhappey.Tools.Supadata;
using Microsoft.Extensions.DependencyInjection;

namespace MCPhappey.Tools.Supadata;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSupadata(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var key = headers?
            .FirstOrDefault(h => h.Key == "api.supadata.ai")
            .Value?
            .FirstOrDefault(h => h.Key == "x-api-key")
            .Value?
            .Split(" ")
            .LastOrDefault();

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new SupadataSettings { ApiKey = key });

        services.AddHttpClient<SupadataClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<SupadataSettings>();
            client.BaseAddress = new Uri("https://api.supadata.ai/v1/");
            if (!client.DefaultRequestHeaders.Contains("x-api-key"))
                client.DefaultRequestHeaders.Add("x-api-key", settings.ApiKey);
            client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        });

        return services;
    }
}
