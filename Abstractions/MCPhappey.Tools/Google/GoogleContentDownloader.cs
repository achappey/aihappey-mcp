using MCPhappey.Auth;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.Google.Agents;
using Microsoft.Extensions.DependencyInjection;

namespace MCPhappey.Tools.Google;

/// <summary>Intercepts Google resource reads and header-mode file inputs before any operator scraper.</summary>
public sealed class GoogleContentDownloader : IRequestContentDownloader
{
    public bool Supports(ServerConfig config) => GoogleProviderAccess.IsDualModeServer(config);

    public async Task<IEnumerable<FileItem>?> DownloadAsync(IServiceProvider services, ServerConfig config,
        string url, CancellationToken cancellationToken = default)
    {
        var mode = services.GetEffectiveAccessMode(config);
        var uri = new Uri(url, UriKind.Absolute);
        if (config.Server.ServerInfo.Name.Equals("Google-Agents", StringComparison.OrdinalIgnoreCase)
            && uri.Host.Equals(GoogleProviderAccess.ApiHost, StringComparison.OrdinalIgnoreCase))
        {
            GoogleProviderAccess.ValidateAgentUrl(url);
            var json = await services.GetRequiredService<GoogleAgentsClient>().ReadAsync(url, config, cancellationToken);
            return [new FileItem { Uri = url, Filename = "agents.json", MimeType = "application/json", Contents = BinaryData.FromString(json.ToJsonString()) }];
        }
        if (mode == ServerAccessMode.Delegated)
            return null;
        if (mode != ServerAccessMode.Headers)
            throw new UnauthorizedAccessException("Google provider headers or delegated authentication are required.");

        services.GetProviderCredential(config, GoogleProviderAccess.ApiKeyVariable);
        if (config.Server.OBO?.Keys.Any(host => uri.Host.Equals(host.TrimStart('.'), StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith('.' + host.TrimStart('.'), StringComparison.OrdinalIgnoreCase)) == true)
            throw new UnauthorizedAccessException("This file URL requires delegated authentication.");
        return [await services.GetRequiredService<PublicFileDownloader>().DownloadAsync(url, cancellationToken)];
    }
}
