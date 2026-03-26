using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.NoizAI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNoizAI(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var key = headers?
            .FirstOrDefault(h => h.Key == "noiz.ai")
            .Value?
            .FirstOrDefault(h => h.Key.Equals(HeaderNames.Authorization, StringComparison.OrdinalIgnoreCase))
            .Value?
            .Trim();

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new NoizAISettings { ApiKey = key });
        return services;
    }
}

public sealed class NoizAISettings
{
    public string ApiKey { get; set; } = default!;
}
