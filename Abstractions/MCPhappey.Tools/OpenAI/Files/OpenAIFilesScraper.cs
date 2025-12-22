using MCPhappey.Auth.Extensions;
using MCPhappey.Common;
using MCPhappey.Common.Extensions;
using MCPhappey.Common.Models;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using OpenAI;

namespace MCPhappey.Tools.OpenAI.Files;

public class OpenAIFilesScraper : IContentScraper
{
    public const string BASE_URL = "https://api.openai.com/v1/files";

    public bool SupportsHost(ServerConfig serverConfig, string url)
        => url.StartsWith(BASE_URL, StringComparison.OrdinalIgnoreCase);

    public async Task<IEnumerable<FileItem>?> GetContentAsync(
        McpServer mcpServer,
        IServiceProvider serviceProvider,
        string url,
        CancellationToken cancellationToken = default)
    {
        var openAiClient = serviceProvider.GetRequiredService<OpenAIClient>();
        var _ = serviceProvider.GetUserId(); // currently unused, keep if you’ll scope later
        var client = openAiClient.GetOpenAIFileClient();

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return [];

        var segments = uri.AbsolutePath.TrimEnd('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        // 1) /v1/files/{fileId}/content  -> return raw bytes (your existing behavior)
        var isContentEndpoint =
            segments.Length >= 4 &&
            segments[0] == "v1" && segments[1] == "files" &&
            segments[^1] == "content";

        if (isContentEndpoint)
        {
            var fileId = segments[2];
            var file = await client.GetFileAsync(fileId, cancellationToken: cancellationToken);
            var bytes = await client.DownloadFileAsync(fileId, cancellationToken: cancellationToken);

            return
            [
                new FileItem
                {
                    Contents = bytes.Value,
                    Filename = file.Value.Filename,
                    Uri = url
                }
            ];
        }

        // 2) /v1/files/{fileId}          -> return metadata as JSON (NOT the content)
        var isMetadataEndpoint =
            segments.Length == 3 &&
            segments[0] == "v1" && segments[1] == "files";

        if (isMetadataEndpoint)
        {
            var fileId = segments[2];
            var file = await client.GetFileAsync(fileId, cancellationToken: cancellationToken);

            return
            [
                file.ToFileItem(url)
            ];
        }

        return [];
    }
}
