using System.Text;
using MCPhappey.Auth.Extensions;
using MCPhappey.Common;
using MCPhappey.Common.Extensions;
using MCPhappey.Common.Models;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using OpenAI;

namespace MCPhappey.Tools.OpenAI.Containers;

public class ContainerScraper : IContentScraper
{

    public bool SupportsHost(ServerConfig serverConfig, string url)
        => url.StartsWith(ContainerExtensions.BASE_URL, StringComparison.OrdinalIgnoreCase);

    public async Task<IEnumerable<FileItem>?> GetContentAsync(McpServer mcpServer,
        IServiceProvider serviceProvider, string url, CancellationToken cancellationToken = default)
    {
        var openAiClient = serviceProvider.GetRequiredService<OpenAIClient>();
        var userId = serviceProvider.GetUserId();
        var client = openAiClient
                    .GetContainerClient();

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return [];

        var segments = uri.AbsolutePath.TrimEnd('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        string? containerId = null;
        string? fileId = null;

        // Case 1: Container files list
        if (segments.Length >= 4 &&
            segments[0] == "v1" &&
            segments[1] == "containers" &&
            segments[^1] == "files")
        {
            containerId = segments[2];
            var files = await client.GetContainerFilesAsync(containerId, cancellationToken: cancellationToken).MaterializeToListAsync(cancellationToken);

            return files.Select(f => f.ToFileItem(url));
        }

        // Case 2: File content
        bool isContentEndpoint =
            segments.Length >= 6 &&
            segments[0] == "v1" &&
            segments[1] == "containers" &&
            segments[3] == "files" &&
            segments[^1] == "content";

        if (isContentEndpoint)
        {
            containerId = segments[2];
            fileId = segments[4];

            var response = await client.DownloadContainerFileAsync(containerId, fileId, cancellationToken);
            var raw = response.GetRawResponse();

            var encoding = raw.Headers.TryGetValue("Content-Type", out var ct) &&
                           ct?.Contains("charset=", StringComparison.OrdinalIgnoreCase) == true
                ? Encoding.GetEncoding(ct.Split("charset=")[1].Trim())
                : Encoding.UTF8;

            Stream contentStream = raw.ContentStream
                  ?? throw new InvalidOperationException("No content stream in response.");

            using var ms = new MemoryStream();
            await contentStream.CopyToAsync(ms, cancellationToken);
            // string text = encoding.GetString(bytes);
            return [(await BinaryData.FromStreamAsync(ms, cancellationToken)).ToFileItem(url, ct ?? "application/octet-stream")];
        }

        // Case 3: File metadata
        bool isFileEndpoint =
            segments.Length >= 5 &&
            segments[0] == "v1" &&
            segments[1] == "containers" &&
            segments[3] == "files";

        if (isFileEndpoint)
        {
            containerId = segments[2];
            fileId = segments[4];

            var response = await client.GetContainerFileAsync(containerId, fileId);
            var raw = response.GetRawResponse();

            var encoding = raw.Headers.TryGetValue("Content-Type", out var ct) &&
                           ct?.Contains("charset=", StringComparison.OrdinalIgnoreCase) == true
                ? Encoding.GetEncoding(ct.Split("charset=")[1].Trim())
                : Encoding.UTF8;

            Stream contentStream = raw.ContentStream
                  ?? throw new InvalidOperationException("No content stream in response.");

            using var ms = new MemoryStream();
            await contentStream.CopyToAsync(ms, cancellationToken);
            var bytes = ms.ToArray();
            string text = encoding.GetString(bytes);
            return [text.ToFileItem(url)];
        }

        return [];

    }
}
