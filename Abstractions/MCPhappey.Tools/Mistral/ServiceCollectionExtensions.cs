using System.Net.Http.Headers;
using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.Mistral;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMistral(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        // 🧠 1) Extract API key from your existing config helper
        var key = headers?
            .FirstOrDefault(h => h.Key == "api.mistral.ai")
            .Value?
            .FirstOrDefault(h => h.Key == HeaderNames.Authorization)
            .Value?
            .GetBearerToken();

        if (string.IsNullOrWhiteSpace(key))
            return services;

        // 🧠 2) Register settings
        services.AddSingleton(new MistralSettings { ApiKey = key });

        // 🧠 3) Register typed HttpClient
        services.AddHttpClient<MistralClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<MistralSettings>();
            client.BaseAddress = new Uri("https://api.mistral.ai/v1/");
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
        });

        return services;
    }
}
