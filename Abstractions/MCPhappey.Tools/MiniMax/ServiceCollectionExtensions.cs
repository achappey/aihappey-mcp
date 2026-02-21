using System.Net.Http.Headers;
using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.MiniMax;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMiniMax(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var key = headers?
            .FirstOrDefault(h => h.Key == "api.minimax.io")
            .Value?
            .FirstOrDefault(h => h.Key == HeaderNames.Authorization)
            .Value?
            .GetBearerToken();

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new MiniMaxSettings { ApiKey = key });

        services.AddHttpClient<MiniMaxClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<MiniMaxSettings>();
            client.BaseAddress = new Uri("https://api.minimax.io/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
        });

        return services;
    }
}

