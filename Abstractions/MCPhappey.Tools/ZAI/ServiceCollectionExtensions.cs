using System.Net.Http.Headers;
using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.ZAI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddZAI(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var key = headers?
            .FirstOrDefault(h => h.Key == "api.z.ai")
            .Value?
            .FirstOrDefault(h => h.Key == HeaderNames.Authorization)
            .Value?
            .GetBearerToken();

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new ZAISettings { ApiKey = key });

        services.AddHttpClient<ZAIClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<ZAISettings>();
            client.BaseAddress = new Uri("https://api.z.ai/api/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
        });

        return services;
    }
}
