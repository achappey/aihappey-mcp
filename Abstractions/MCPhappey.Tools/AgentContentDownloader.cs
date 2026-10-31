using MCPhappey.Auth;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MCPhappey.Tools;

/// <summary>Runs before static operator scrapers so a caller-key server cannot borrow operator resources.</summary>
public sealed class AgentContentDownloader : IRequestContentDownloader
{
    public bool Supports(ServerConfig config)
        => AgentProviderHttp.IsAgentServer(config)
            || config.Server.ServerInfo.Name.Equals("OpenAI-Decisions", StringComparison.OrdinalIgnoreCase);

    public async Task<IEnumerable<FileItem>?> DownloadAsync(IServiceProvider services, ServerConfig config,
        string url, CancellationToken cancellationToken = default)
    {
        var mode = services.GetEffectiveAccessMode(config);
        var uri = new Uri(url, UriKind.Absolute);
        if (AgentProviderHttp.IsAgentServer(config)
            && uri.Host.Equals(AgentProviderHttp.GetProviderHost(config), StringComparison.OrdinalIgnoreCase))
        {
            var json = await AgentProviderHttp.SendAsync(services, config, HttpMethod.Get, url, null, cancellationToken);
            return [new FileItem { Uri = url, Filename = "agents.json", MimeType = "application/json", Contents = BinaryData.FromString(json.ToJsonString()) }];
        }
        if (mode == ServerAccessMode.Delegated)
            return null;
        if (mode != ServerAccessMode.Headers)
            throw new UnauthorizedAccessException("Provider headers or delegated authentication are required.");

        services.GetProviderCredential(config,
            config.Server.ServerInfo.Name.Equals("Anthropic-Agents", StringComparison.OrdinalIgnoreCase)
                ? "anthropic_api_key" : "openai_api_key");
        if (config.Server.OBO?.Keys.Any(host => uri.Host.Equals(host.TrimStart('.'), StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith('.' + host.TrimStart('.'), StringComparison.OrdinalIgnoreCase)) == true)
            throw new UnauthorizedAccessException("This file URL requires delegated authentication.");
        return [await services.GetRequiredService<PublicFileDownloader>().DownloadAsync(url, cancellationToken)];
    }
}
