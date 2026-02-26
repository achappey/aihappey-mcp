using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.Pinecone;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPinecone(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var apiKey = headers?
            .FirstOrDefault(h => h.Key == "api.pinecone.io")
            .Value?
            .FirstOrDefault(h => h.Key.Equals("Api-Key", StringComparison.OrdinalIgnoreCase))
            .Value;

        apiKey = apiKey?.GetBearerToken() ?? apiKey;

        if (string.IsNullOrWhiteSpace(apiKey))
            return services;

        services.AddSingleton(new PineconeSettings { ApiKey = apiKey });
        services.AddHttpClient<PineconeClient>();

        return services;
    }
}
