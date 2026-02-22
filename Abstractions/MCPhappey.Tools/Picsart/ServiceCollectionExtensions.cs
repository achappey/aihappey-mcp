using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MCPhappey.Tools.Picsart;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPicsart(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {

        var key = headers?
         .FirstOrDefault(h => h.Key == "api.picsart.io")
         .Value?
         .FirstOrDefault(h => h.Key.Equals("x-picsart-api-key", StringComparison.OrdinalIgnoreCase))
         .Value;

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new PicsartSettings { ApiKey = key });
        return services;
    }
}

public sealed class PicsartSettings
{
    public string ApiKey { get; set; } = default!;
}
