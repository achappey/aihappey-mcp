using System.Net.Http.Headers;
using MCPhappey.Tools.Anthropic.Skills;
using Microsoft.Extensions.DependencyInjection;

namespace MCPhappey.Tools.Anthropic.Messages;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAnthropicMessages(this IServiceCollection services)
    {
        services.AddHttpClient<AnthropicMessagesClient>((serviceProvider, client) =>
        {
            var settings = serviceProvider.GetRequiredService<AnthropicSettings>();
            client.BaseAddress = new Uri($"{AnthropicHeaders.ApiBaseUrl}/");
            client.DefaultRequestHeaders.Add(AnthropicHeaders.ApiKeyHeader, settings.ApiKey);
            client.DefaultRequestHeaders.Add(AnthropicHeaders.AnthropicVersionHeader, AnthropicHeaders.AnthropicVersion);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });
        return services;
    }
}
