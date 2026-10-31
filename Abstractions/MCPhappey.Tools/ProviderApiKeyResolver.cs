using MCPhappey.Auth;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;

namespace MCPhappey.Tools;

/// <summary>Resolves a provider key without sharing caller credentials or downgrading delegated access.</summary>
public static class ProviderApiKeyResolver
{
    public static string Resolve(IServiceProvider services, ServerConfig config, string variableName,
        Func<string?> configuredKey)
    {
        var key = services.GetEffectiveAccessMode(config) switch
        {
            ServerAccessMode.Headers => services.GetProviderCredential(config, variableName),
            ServerAccessMode.Delegated => configuredKey(),
            _ => throw new UnauthorizedAccessException("This server requires provider headers or delegated authentication.")
        };
        return !string.IsNullOrWhiteSpace(key) ? key.Trim()
            : throw new InvalidOperationException("Provider credentials have not been configured for delegated access.");
    }
}
