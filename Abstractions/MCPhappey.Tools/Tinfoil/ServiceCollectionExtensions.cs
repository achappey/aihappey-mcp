using System.Net.Http.Headers;
using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.Tinfoil;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTinfoil(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var key = headers?
            .FirstOrDefault(h => h.Key == "inference.tinfoil.sh")
            .Value?
            .FirstOrDefault(h => h.Key == HeaderNames.Authorization)
            .Value?
            .GetBearerToken();

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new TinfoilSettings { ApiKey = key });

        services.AddHttpClient<TinfoilClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<TinfoilSettings>();
            client.BaseAddress = new Uri("https://inference.tinfoil.sh/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        });

        return services;
    }
}
