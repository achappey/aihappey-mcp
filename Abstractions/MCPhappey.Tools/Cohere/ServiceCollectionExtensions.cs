using System.Net.Http.Headers;
using System.Reflection;
using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.Cohere;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCohere(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var key = headers?
            .FirstOrDefault(h => h.Key == "api.cohere.com")
            .Value?
            .FirstOrDefault(h => h.Key == HeaderNames.Authorization)
            .Value?
            .GetBearerToken();

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new CohereSettings { ApiKey = key });

        services.AddHttpClient<CohereClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<CohereSettings>();
            client.BaseAddress = new Uri("https://api.cohere.com/v2/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
            var assemblyName = Assembly.GetExecutingAssembly().GetName().Name;
            var clientName = assemblyName?.Split('.').Last() ?? "Unknown";
            client.DefaultRequestHeaders.Add("X-Client-Name", clientName);
        });

        return services;
    }
}
