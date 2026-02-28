using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.LLMLayer;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLLMLayer(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var key = headers?
            .FirstOrDefault(h => h.Key.Equals("api.llmlayer.dev", StringComparison.OrdinalIgnoreCase))
            .Value?
            .FirstOrDefault(h => h.Key.Equals(HeaderNames.Authorization, StringComparison.OrdinalIgnoreCase))
            .Value?
            .GetBearerToken();

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new LLMLayerSettings { ApiKey = key.Trim() });
        services.AddHttpClient<LLMLayerClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<LLMLayerSettings>();
            client.BaseAddress = new Uri("https://api.llmlayer.dev/api/v2/");
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.ApiKey);
            client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        });

        return services;
    }
}

public sealed class LLMLayerSettings
{
    public string ApiKey { get; set; } = default!;
}

