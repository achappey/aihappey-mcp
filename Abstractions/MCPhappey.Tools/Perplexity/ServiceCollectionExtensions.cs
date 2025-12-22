using System.Net.Http.Headers;
using MCPhappey.Auth.Extensions;
using MCPhappey.Tools.Perplexity.Clients;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.Perplexity;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPerplexity(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var key = headers?
            .FirstOrDefault(h => h.Key == "api.perplexity.ai")
            .Value?
            .FirstOrDefault(h => h.Key == HeaderNames.Authorization)
            .Value?
            .GetBearerToken();

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new PerplexitySettings { ApiKey = key });

        services.AddHttpClient<PerplexityClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<PerplexitySettings>();
            client.BaseAddress = new Uri("https://api.perplexity.ai/");
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
        });

        return services;
    }
}
