using System.Text.Json;
using MCPhappey.Auth;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.OpenAI.Decisions;

public static class OpenAIDecisionsClientResolver
{
    public static async Task<JsonElement> CreateAsync(IServiceProvider services, McpServer server,
        OpenAIDecisionsRequest request, CancellationToken cancellationToken = default)
    {
        if (services.GetEffectiveAccessMode(server) == ServerAccessMode.Delegated)
        {
            var configured = services.GetService<OpenAIDecisionsClient>()
                ?? throw new InvalidOperationException("OpenAI Decisions provider credentials have not been configured for delegated access. Register AddOpenAIDecisions with the operator's OpenAI domain headers.");
            return await configured.CreateAsync(request, cancellationToken);
        }

        var apiKey = services.GetProviderCredential(server, "openai_api_key");
        using var http = services.GetRequiredService<IHttpClientFactory>().CreateClient(AgentProviderHttp.ClientName);
        return await new OpenAIDecisionsClient(http).CreateWithApiKeyAsync(request, apiKey, cancellationToken);
    }
}
