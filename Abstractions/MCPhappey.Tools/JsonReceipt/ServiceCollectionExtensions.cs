using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.JsonReceipt;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddJsonReceipt(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var domainHeaders = headers?
            .FirstOrDefault(h => h.Key.Equals("jsonreceipts.com", StringComparison.OrdinalIgnoreCase))
            .Value;

        var apiKey = domainHeaders?
            .FirstOrDefault(h => h.Key.Equals("X-API-Key", StringComparison.OrdinalIgnoreCase))
            .Value;

        apiKey ??= domainHeaders?
            .FirstOrDefault(h => h.Key.Equals(HeaderNames.Authorization, StringComparison.OrdinalIgnoreCase))
            .Value?
            .GetBearerToken();

        if (string.IsNullOrWhiteSpace(apiKey))
            return services;

        services.AddSingleton(new JsonReceiptSettings { ApiKey = apiKey });
        services.AddHttpClient<JsonReceiptClient>();

        return services;
    }
}

