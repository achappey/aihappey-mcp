using System.Net.Http.Headers;
using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.Nebius;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNebius(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var key = headers?
            .FirstOrDefault(h => h.Key.Equals("api.tokenfactory.nebius.com", StringComparison.OrdinalIgnoreCase))
            .Value?
            .FirstOrDefault(h => h.Key.Equals(HeaderNames.Authorization, StringComparison.OrdinalIgnoreCase))
            .Value?
            .GetBearerToken();

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new NebiusSettings
        {
            ApiKey = key,
            BaseUrl = "https://api.tokenfactory.nebius.com/"
        });

        services.AddHttpClient<NebiusClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<NebiusSettings>();
            client.BaseAddress = new Uri(settings.BaseUrl);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
        });

        return services;
    }
}

public sealed class NebiusSettings
{
    public string ApiKey { get; set; } = default!;
    public string BaseUrl { get; set; } = "https://api.tokenfactory.nebius.com/";
}
