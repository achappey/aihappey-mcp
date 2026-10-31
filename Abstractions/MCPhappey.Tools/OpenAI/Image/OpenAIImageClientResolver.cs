using MCPhappey.Auth;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using OpenAI;

namespace MCPhappey.Tools.OpenAI.Image;

public static class OpenAIImageClientResolver
{
    public static OpenAIClient Resolve(IServiceProvider services, McpServer server)
        => services.GetEffectiveAccessMode(server) == ServerAccessMode.Delegated
            ? services.GetService<OpenAIClient>() ?? throw new InvalidOperationException("OpenAI provider credentials have not been configured for delegated access.")
            : new OpenAIClient(services.GetProviderCredential(server, "openai_api_key"));
}
