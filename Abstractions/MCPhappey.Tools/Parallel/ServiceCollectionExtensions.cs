using System.Net.Http.Headers;
using MCPhappey.Tools.Parallel.Clients;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.Parallel;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddParallel(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var key = headers?
            .FirstOrDefault(h => h.Key == "api.parallel.ai")
            .Value?
            .FirstOrDefault(h => h.Key == "x-api-key")
            .Value?
            .Split(" ")
            .LastOrDefault();

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new ParallelSettings { ApiKey = key });

        services.AddHttpClient<ParallelClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<ParallelSettings>();
            client.BaseAddress = new Uri("https://api.parallel.ai/");
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
        });

        return services;
    }
}
