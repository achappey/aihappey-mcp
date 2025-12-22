using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.Together;

public static class TogetherExtensions
{
    /// <summary>
    /// Creates a configured Together AI HttpClient with Authorization and JSON headers.
    /// </summary>
    public static HttpClient CreateTogetherClient(this IServiceProvider serviceProvider, string accept = MimeTypes.Json)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var settings = serviceProvider.GetRequiredService<TogetherSettings>();
        var factory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue(accept));

        return client;
    }

}

public class TogetherSettings
{
    public string ApiKey { get; set; } = default!;
}
