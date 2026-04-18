using System.Net.Http.Headers;
using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.SyntheticSearch;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSyntheticSearch(this IServiceCollection services, Dictionary<string, Dictionary<string, string>>? headers)
    {
        var key = headers?
            .FirstOrDefault(h => h.Key.Equals("api.synthetic.new", StringComparison.OrdinalIgnoreCase))
            .Value?
            .FirstOrDefault(h => h.Key.Equals(HeaderNames.Authorization, StringComparison.OrdinalIgnoreCase))
            .Value?
            .GetBearerToken();

        if (string.IsNullOrWhiteSpace(key))
            return services;

        services.AddSingleton(new SyntheticSearchSettings { ApiKey = key });

        services.AddHttpClient<SyntheticSearchClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<SyntheticSearchSettings>();
            client.BaseAddress = new Uri("https://api.synthetic.new/");
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
        });

        return services;
    }
}
