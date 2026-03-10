using System.Net.Http.Headers;
using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.Valyu;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddValyu(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var domainHeaders = headers?
            .FirstOrDefault(h => h.Key.Equals("api.valyu.ai", StringComparison.OrdinalIgnoreCase))
            .Value
            ?? headers?
                .FirstOrDefault(h => h.Key.Equals("valyu.ai", StringComparison.OrdinalIgnoreCase))
                .Value;

        var key = domainHeaders?
            .FirstOrDefault(h => h.Key.Equals("X-API-Key", StringComparison.OrdinalIgnoreCase))
            .Value;

        if (string.IsNullOrWhiteSpace(key))
            key = domainHeaders?
                .FirstOrDefault(h => h.Key.Equals(HeaderNames.Authorization, StringComparison.OrdinalIgnoreCase))
                .Value?
                .GetBearerToken();

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new ValyuSettings { ApiKey = key });

        services.AddHttpClient<ValyuClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<ValyuSettings>();
            client.BaseAddress = new Uri("https://api.valyu.ai/");
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", settings.ApiKey);
        });

        return services;
    }
}
