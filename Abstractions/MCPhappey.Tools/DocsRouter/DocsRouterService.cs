using System.ComponentModel;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.DocsRouter;

public static class DocsRouterService
{
    [Description("Extract OCR text from a fileUrl using DocsRouter native OCR. The file is downloaded first, converted to raw base64, and sent to DocsRouter.")]
    [McpServerTool(
        Name = "docsrouter_ocr",
        Title = "DocsRouter OCR",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true)]
    public static async Task<CallToolResult?> DocsRouter_OCR(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("File URL or SharePoint/OneDrive reference to download and OCR")] string fileUrl,
        [Description("Vision model to use. Default: google/gemini-2.0-flash-001")] string model = "google/gemini-2.0-flash-001",
        [Description("If true, extract table structures when possible. Default: false")] bool extractTables = false,
        [Description("Language hint for the document, e.g. en, de, es. Default: auto")] string language = "auto",
        [Description("Preferred output format: text, json, or markdown. Default: text")] string outputFormat = "text",
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var docsRouter = serviceProvider.GetRequiredService<DocsRouterClient>();
                var downloadService = serviceProvider.GetRequiredService<DownloadService>();

                var files = await downloadService.DownloadContentAsync(
                    serviceProvider,
                    requestContext.Server,
                    fileUrl,
                    cancellationToken);

                var file = files.FirstOrDefault()
                    ?? throw new InvalidOperationException("No file found for DocsRouter OCR input.");

                var base64 = Convert.ToBase64String(file.Contents.ToArray());

                var body = new
                {
                    base64,
                    model,
                    options = new
                    {
                        extract_tables = extractTables,
                        language,
                        output_format = outputFormat
                    }
                };

                return await docsRouter.PostJsonAsync("v1/ocr", body, cancellationToken);
            }));
}
