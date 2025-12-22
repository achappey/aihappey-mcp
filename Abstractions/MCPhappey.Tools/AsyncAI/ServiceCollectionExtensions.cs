using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.AsyncAI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAsyncAI(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var key = headers?
            .FirstOrDefault(h => h.Key == "api.async.ai")
            .Value?
            .FirstOrDefault(h => h.Key == "x-api-key")
            .Value;

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new AsyncAISettings { ApiKey = key });

        services.AddHttpClient<AsyncAIClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<AsyncAISettings>();
            client.BaseAddress = new Uri("https://api.async.ai/");
            client.DefaultRequestHeaders.Add("x-api-key", settings.ApiKey);
            client.DefaultRequestHeaders.Add("version", "v1");
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));

        });

        return services;
    }
}
