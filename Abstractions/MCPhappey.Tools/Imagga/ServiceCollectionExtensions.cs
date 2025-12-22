using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.Imagga;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddImagga(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var key = headers?
            .FirstOrDefault(h => h.Key == "api.imagga.com")
            .Value?
            .FirstOrDefault(h => h.Key == HeaderNames.Authorization)
            .Value?
            .Split(" ")
            .LastOrDefault();

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new ImaggaSettings { ApiKey = key });

        services.AddHttpClient<ImaggaClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<ImaggaSettings>();
            client.BaseAddress = new Uri("https://api.imagga.com/v2/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", settings.ApiKey);
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
        });

        return services;
    }
}
