using System.Text;
using MCPhappey.Common.Extensions;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Core.Services;

public class ResourceService(DownloadService downloadService, IServerDataProvider? dynamicDataService = null)
{
    public async Task<ListResourceTemplatesResult> GetServerResourceTemplates(ServerConfig serverConfig,
          CancellationToken cancellationToken = default) => serverConfig.SourceType switch
          {
              ServerSourceType.Static => await Task.FromResult(serverConfig?.ResourceTemplateList
                                                ?? new()),
              ServerSourceType.Dynamic => await dynamicDataService!.GetResourceTemplatesAsync(serverConfig.Server.ServerInfo.Name,
                cancellationToken),
              _ => await Task.FromResult(serverConfig?.ResourceTemplateList
                                                ?? new()),
          };

    public async Task<ListResourcesResult> GetServerResources(ServerConfig serverConfig,
        CancellationToken cancellationToken = default) => serverConfig.SourceType switch
        {
            ServerSourceType.Static => await Task.FromResult(serverConfig?.ResourceList
                                              ?? new()),
            ServerSourceType.Dynamic => await dynamicDataService!.GetResourcesAsync(serverConfig.Server.ServerInfo.Name, cancellationToken),
            _ => await Task.FromResult(serverConfig?.ResourceList
                                              ?? new()),
        };

    public async Task<ReadResourceResult> GetServerResource(IServiceProvider serviceProvider,
        McpServer mcpServer,
        string uri,
        string? cursor = null,
        int? limit = 100,
        CancellationToken cancellationToken = default)
    {
        var serverConfig = serviceProvider.GetServerConfig(mcpServer);

        if (uri.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            var resources = await GetServerResources(serverConfig!, cancellationToken);

            var widgetResource = resources.Resources
                .FirstOrDefault(a => a.MimeType?.Equals("text/html+skybridge", StringComparison.OrdinalIgnoreCase) == true
                    && a.Uri.Equals(uri, StringComparison.OrdinalIgnoreCase));

            if (widgetResource != null)
            {
                var download = await downloadService.DownloadContentAsync(serviceProvider, mcpServer, uri,
                               cancellationToken);

                if (!download.Any())
                {
                    throw new Exception($"Resource {uri} not found");
                }

                var item = download.First();

                var httpContextAccessor = serviceProvider.GetRequiredService<IHttpContextAccessor>();
                var request = httpContextAccessor.HttpContext?.Request;
                var baseUrl = request != null
                    ? $"{request.Scheme}://{request.Host.Value}"
                    : null;

                var html = Encoding.UTF8.GetString(item.Contents);

                return new ReadResourceResult()
                {
                    Contents = [new TextResourceContents() {
                    Text = html.Replace("%HOST_URL%", baseUrl),
                    MimeType = "text/html+skybridge",
                    Uri = uri,
                    Meta = new System.Text.Json.Nodes.JsonObject() {
                        ["openai/widgetDescription"] = widgetResource.Description
                    }
                }]
                };
            }
            else
            {
                var download = await downloadService.DownloadContentAsync(serviceProvider, mcpServer, uri,
                              cancellationToken);

                if (!download.Any())
                {
                    throw new Exception($"Resource {uri} not found");
                }

                var item = download.First();

                return new ReadResourceResult()
                {
                    Contents = [new TextResourceContents() {
                    Text = Encoding.UTF8.GetString(item.Contents),
                    MimeType = "text/html",
                    Uri = uri
                }]
                };
            }
        }

        var fileItem = await downloadService.ScrapeContentAsync(serviceProvider, mcpServer, uri,
                       cancellationToken);

        var fileItems = fileItem.ToList();
        var (pagedItems, nextCursor) = fileItems.ApplyPaging(cursor, limit);

        var result = pagedItems.ToReadResourceResult();

        if (nextCursor is not null)
        {
            foreach (var content in result.Contents.OfType<TextResourceContents>())
            {
                content.Meta ??= [];
                content.Meta["nextCursor"] = nextCursor;
            }
        }

        return result;
    }


}


public static class ResourcePaging
{

    private static readonly HashSet<string> ExtraTextMimeTypes =
        [
            "application/json",
            "application/xml",
            "application/javascript",
            "application/x-javascript",
            "application/sql",
            "application/csv"
        ];

    private static bool IsTextMimeType(string? mimeType)
    {
        if (string.IsNullOrWhiteSpace(mimeType))
            return false;

        if (mimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
            return true;

        return ExtraTextMimeTypes.Contains(mimeType);
    }

    private static bool CanApplyPaging(IEnumerable<FileItem> items)
    {
        var list = items.ToList();
        return list.Count > 0 && list.All(x => IsTextMimeType(x.MimeType));
    }

    public static (IReadOnlyList<FileItem> Items, string? NextCursor) ApplyPaging(
        this IEnumerable<FileItem> items,
        string? cursor,
        int? limit)
    {
        var list = items.ToList();

        if (!CanApplyPaging(list))
            return (list, null);

        var startLine = ParseCursor(cursor);
        var takeLines = Math.Clamp(limit ?? 100, 1, 1000);

        var paged = new List<FileItem>();
        var currentLine = 0;
        string? nextCursor = null;

        foreach (var item in list)
        {
            var text = item.Contents.ToString();
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            if (currentLine + lines.Length <= startLine)
            {
                currentLine += lines.Length;
                continue;
            }

            var localStart = Math.Max(0, startLine - currentLine);
            var remaining = takeLines - paged.Sum(x => x.Contents.ToString().Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Length);

            if (remaining <= 0)
                break;

            var slice = lines.Skip(localStart).Take(remaining).ToArray();
            if (slice.Length > 0)
            {
                paged.Add(new FileItem
                {
                    Uri = item.Uri,
                    Filename = item.Filename,
                    MimeType = item.MimeType,
                    Contents = BinaryData.FromString(string.Join("\n", slice))
                });
            }

            currentLine += lines.Length;

            var takenSoFar = paged.Sum(x => x.Contents.ToString().Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Length);
            if (takenSoFar >= takeLines)
            {
                nextCursor = $"line:{startLine + takenSoFar}";
                break;
            }
        }

        return (paged.Count > 0 ? paged : [], nextCursor);
    }

    private static int ParseCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return 0;

        if (cursor.StartsWith("line:", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(cursor[5..], out var line) &&
            line >= 0)
        {
            return line;
        }

        return 0;
    }
}
