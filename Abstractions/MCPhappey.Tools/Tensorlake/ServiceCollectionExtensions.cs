using System.Net.Http.Headers;
using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.Tensorlake;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTensorlake(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var domainHeaders = headers?
            .FirstOrDefault(h => h.Key.Equals("api.tensorlake.ai", StringComparison.OrdinalIgnoreCase))
            .Value
            ?? headers?
                .FirstOrDefault(h => h.Key.Equals("tensorlake.ai", StringComparison.OrdinalIgnoreCase))
                .Value;

        var apiKey = domainHeaders?
            .FirstOrDefault(h => h.Key.Equals(HeaderNames.Authorization, StringComparison.OrdinalIgnoreCase))
            .Value?
            .GetBearerToken();

        apiKey ??= domainHeaders?
            .FirstOrDefault(h => h.Key.Equals("x-api-key", StringComparison.OrdinalIgnoreCase))
            .Value;

        if (string.IsNullOrWhiteSpace(apiKey))
            return services;

        services.AddSingleton(new TensorlakeSettings { ApiKey = apiKey });

        services.AddHttpClient<TensorlakeClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<TensorlakeSettings>();
            client.BaseAddress = new Uri("https://api.tensorlake.ai/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });

        return services;
    }
}

public sealed class TensorlakeSettings
{
    public string ApiKey { get; set; } = default!;
}
