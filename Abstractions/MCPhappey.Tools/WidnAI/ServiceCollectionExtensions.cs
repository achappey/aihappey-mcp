using System.Net.Http.Headers;
using MCPhappey.Auth.Extensions;
using Microsoft.KernelMemory.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.WidnAI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWidnAI(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var domainHeaders = headers?
            .FirstOrDefault(h => h.Key.Equals("api.widn.ai", StringComparison.OrdinalIgnoreCase))
            .Value
            ?? headers?
                .FirstOrDefault(h => h.Key.Equals("widn.ai", StringComparison.OrdinalIgnoreCase))
                .Value;

        var authHeader = domainHeaders?
            .FirstOrDefault(h => h.Key.Equals(HeaderNames.Authorization, StringComparison.OrdinalIgnoreCase))
            .Value;

        var apiKey = authHeader?.GetBearerToken()
            ?? domainHeaders?
                .FirstOrDefault(h => h.Key.Equals("x-api-key", StringComparison.OrdinalIgnoreCase))
                .Value;

        if (string.IsNullOrWhiteSpace(apiKey))
            return services;

        services.AddSingleton(new WidnAISettings { ApiKey = apiKey });

        services.AddHttpClient<WidnAIClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<WidnAISettings>();
            client.BaseAddress = new Uri("https://api.widn.ai/v1/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
        });

        return services;
    }
}

public sealed class WidnAISettings
{
    public string ApiKey { get; set; } = default!;
}

