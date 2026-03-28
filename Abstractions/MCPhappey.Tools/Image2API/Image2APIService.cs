using System.ComponentModel;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Image2API;

public static class Image2APIService
{
    [Description("Extract text or structured content from an image using Image2API one-off extraction. The file is downloaded from fileUrl first and sent as a base64 data URL.")]
    [McpServerTool(
        Name = "image2api_extract",
        Title = "Image2API OCR",
        ReadOnly = true,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Image2API_Extract(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("File URL or SharePoint/OneDrive reference to download and extract from.")] string fileUrl,
        [Description("When true, uploads the OCR JSON result and returns only a resource link instead of inline JSON.")] bool saveOutput = false,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            {
                var result = await ExecuteExtractAsync(serviceProvider, requestContext, fileUrl, cancellationToken);
                if (saveOutput)
                    return await requestContext.SaveOutputAsync(serviceProvider, BinaryData.FromString(result?.ToJsonString() ?? "{}"), "json", cancellationToken: cancellationToken);

                return new CallToolResult
                {
                    Meta = await requestContext.GetToolMeta(),
                    StructuredContent = result ?? new JsonObject()
                };
            });

    [Description("Extract text or structured content from an image using Image2API one-off extraction, always save the JSON result, and optionally store it directly in a SharePoint or OneDrive folder.")]
    [McpServerTool(
        Name = "image2api_extract_save",
        Title = "Image2API OCR Save",
        ReadOnly = true,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Image2API_ExtractSave(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("File URL or SharePoint/OneDrive reference to download and extract from.")] string fileUrl,
        [Description("Optional SharePoint or OneDrive folder URL to store the OCR JSON result in directly. When omitted, the default MCP output location is used.")] string? folderUrl = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            {
                var result = await ExecuteExtractAsync(serviceProvider, requestContext, fileUrl, cancellationToken);
                return await requestContext.SaveOutputAsync(serviceProvider, BinaryData.FromString(result?.ToJsonString() ?? "{}"), "json", folderUrl, cancellationToken);
            });

    private static async Task<JsonNode?> ExecuteExtractAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string fileUrl,
        CancellationToken cancellationToken)
    {
        var image2Api = serviceProvider.GetRequiredService<Image2APIClient>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();

        var files = await downloadService.DownloadContentAsync(
            serviceProvider,
            requestContext.Server,
            fileUrl,
            cancellationToken);

        var file = files.FirstOrDefault()
            ?? throw new InvalidOperationException("No file found for Image2API input.");

        var mimeType = string.IsNullOrWhiteSpace(file.MimeType)
            ? "application/octet-stream"
            : file.MimeType;

        var base64 = Convert.ToBase64String(file.Contents.ToArray());
        var dataUrl = $"data:{mimeType};base64,{base64}";

        var body = new
        {
            image = dataUrl
        };

        return await image2Api.PostJsonAsync("api/v1/extract", body, cancellationToken);
    }
}
