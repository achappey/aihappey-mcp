using System.Net.Http.Headers;
using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.RelaxAI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRelaxAI(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        static string? GetBearerFrom(Dictionary<string, Dictionary<string, string>>? src, string host)
            => src?
                .FirstOrDefault(h => h.Key.Equals(host, StringComparison.OrdinalIgnoreCase))
                .Value?
                .FirstOrDefault(h => h.Key.Equals(HeaderNames.Authorization, StringComparison.OrdinalIgnoreCase))
                .Value?
                .GetBearerToken();

        var apiKey = GetBearerFrom(headers, "api.relax.ai")
            ?? GetBearerFrom(headers, "relax.ai");

        if (string.IsNullOrWhiteSpace(apiKey))
            return services;

        services.AddSingleton(new RelaxAISettings
        {
            ApiKey = apiKey,
            BaseUrl = "https://api.relax.ai/v1/"
        });

        services.AddHttpClient<RelaxAIClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<RelaxAISettings>();
            client.BaseAddress = new Uri(settings.BaseUrl);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
        });

        return services;
    }
}
