using System.Net.Http.Headers;
using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.Qomplement;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddQomplement(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var domainHeaders = headers?
            .FirstOrDefault(h => h.Key.Equals("developer-api.qomplement.com", StringComparison.OrdinalIgnoreCase))
            .Value
            ?? headers?
                .FirstOrDefault(h => h.Key.Equals("qomplement.com", StringComparison.OrdinalIgnoreCase))
                .Value;

        var apiKey = domainHeaders?
            .FirstOrDefault(h => h.Key.Equals(HeaderNames.Authorization, StringComparison.OrdinalIgnoreCase))
            .Value?
            .GetBearerToken()
            ?? domainHeaders?
                .FirstOrDefault(h => h.Key.Equals("x-api-key", StringComparison.OrdinalIgnoreCase))
                .Value;

        if (string.IsNullOrWhiteSpace(apiKey))
            return services;

        services.AddSingleton(new QomplementSettings { ApiKey = apiKey });

        services.AddHttpClient<QomplementClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<QomplementSettings>();
            client.BaseAddress = new Uri("https://developer-api.qomplement.com/v1/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });

        return services;
    }
}

public sealed class QomplementSettings
{
    public string ApiKey { get; set; } = default!;
}

