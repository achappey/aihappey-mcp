using System.Net.Http.Headers;
using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.ContextualAI;

public static class ContextualAIExtensions
{
    public static IServiceCollection AddContextualAI(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var key = headers?
            .FirstOrDefault(h => h.Key == "api.contextual.ai")
            .Value?
            .FirstOrDefault(h => h.Key == HeaderNames.Authorization)
            .Value?
            .GetBearerToken();

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new ContextualAISettings { ApiKey = key });
        return services;
    }

    public static HttpClient CreateContextualAIClient(this IServiceProvider serviceProvider, string accept = MimeTypes.Json)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var settings = serviceProvider.GetRequiredService<ContextualAISettings>();
        var factory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        var client = factory.CreateClient();
        client.BaseAddress = new Uri("https://api.contextual.ai/v1/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));

        return client;
    }
}

public class ContextualAISettings
{
    public string ApiKey { get; set; } = default!;
}

