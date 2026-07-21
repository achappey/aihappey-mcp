using System.Net.Http.Headers;
using MCPhappey.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Tools.OpenAI.Responses;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOpenAIResponses(
        this IServiceCollection services,
        Dictionary<string, Dictionary<string, string>>? headers)
    {
        var apiKey = headers?
            .FirstOrDefault(host => host.Key.Equals("api.openai.com", StringComparison.OrdinalIgnoreCase))
            .Value?
            .FirstOrDefault(header => header.Key.Equals(HeaderNames.Authorization, StringComparison.OrdinalIgnoreCase))
            .Value?
            .GetBearerToken();

        if (string.IsNullOrWhiteSpace(apiKey))
            return services;

        services.AddHttpClient<OpenAIResponsesClient>(client =>
        {
            client.BaseAddress = new Uri("https://api.openai.com/v1/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });

        return services;
    }
}
