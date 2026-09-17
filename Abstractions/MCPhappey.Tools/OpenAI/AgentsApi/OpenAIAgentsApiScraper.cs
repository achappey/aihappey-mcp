using System.ComponentModel.DataAnnotations;
using MCPhappey.Common;
using MCPhappey.Common.Models;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.OpenAI.AgentsApi;

public sealed class OpenAIAgentsApiScraper : IContentScraper
{
    private static readonly string[] SupportedPrefixes =
    [
        $"{OpenAIAgentsHttp.ApiBaseUrl}/agents",
        $"{OpenAIAgentsHttp.ApiBaseUrl}/vaults"
    ];

    public bool SupportsHost(ServerConfig serverConfig, string url)
        => SupportedPrefixes.Any(prefix => url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    public async Task<IEnumerable<FileItem>?> GetContentAsync(
        McpServer mcpServer,
        IServiceProvider serviceProvider,
        string url,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Host, "api.openai.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("Invalid OpenAI Agents API resource URL.");
        }

        var response = await OpenAIAgentsHttp.SendAsync(
            serviceProvider,
            HttpMethod.Get,
            url,
            null,
            cancellationToken);

        return [response.ToJsonString().ToJsonFileItem(url)];
    }
}
