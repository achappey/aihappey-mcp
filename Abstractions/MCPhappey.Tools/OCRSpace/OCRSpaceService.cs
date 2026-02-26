using System.ComponentModel;
using System.Net.Http.Headers;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.OCRSpace;

public static class OCRSpaceService
{
    [Description("Extract text from image/PDF files using OCR.space. The file is downloaded from fileUrl first (supports SharePoint/OneDrive).")]
    [McpServerTool(Name = "ocrspace_parse", Title = "OCR.space parse")]
    public static async Task<CallToolResult?> OCRSpace_Parse(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("File URL or SharePoint/OneDrive reference.")] string fileUrl,
        [Description("OCR language (3-letter code), e.g. eng, nld, deu, auto.")] string language = "eng",
        [Description("OCR engine version: 1, 2, or 3.")] int ocrEngine = 1,
        [Description("Return word overlay coordinates.")] bool isOverlayRequired = false,
        [Description("Auto-detect and rotate text orientation.")] bool detectOrientation = false,
        [Description("Treat document as table-like content.")] bool isTable = false,
        [Description("Enable internal upscaling for low-resolution scans.")] bool scale = false,
        [Description("Generate searchable PDF output link.")] bool isCreateSearchablePdf = false,
        [Description("Hide text layer in generated searchable PDF.")] bool isSearchablePdfHideTextLayer = false,
        [Description("Optional filetype override: PDF, GIF, PNG, JPG, TIF, BMP.")] string? filetype = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var client = serviceProvider.GetRequiredService<OCRSpaceClient>();
                var downloadService = serviceProvider.GetRequiredService<DownloadService>();

                var files = await downloadService.DownloadContentAsync(
                    serviceProvider,
                    requestContext.Server,
                    fileUrl,
                    cancellationToken);

                var file = files.FirstOrDefault()
                    ?? throw new Exception("No file found for OCR.space input.");

                using var form = new MultipartFormDataContent();

                var fileContent = new ByteArrayContent(file.Contents.ToArray());
                fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.MimeType ?? "application/octet-stream");
                form.Add(fileContent, "file", file.Filename ?? "input.bin");

                form.Add(new StringContent(language), "language");
                form.Add(new StringContent(ocrEngine.ToString()), "OCREngine");
                form.Add(new StringContent(isOverlayRequired ? "true" : "false"), "isOverlayRequired");
                form.Add(new StringContent(detectOrientation ? "true" : "false"), "detectOrientation");
                form.Add(new StringContent(isTable ? "true" : "false"), "isTable");
                form.Add(new StringContent(scale ? "true" : "false"), "scale");
                form.Add(new StringContent(isCreateSearchablePdf ? "true" : "false"), "isCreateSearchablePdf");
                form.Add(new StringContent(isSearchablePdfHideTextLayer ? "true" : "false"), "isSearchablePdfHideTextLayer");

                if (!string.IsNullOrWhiteSpace(filetype))
                    form.Add(new StringContent(filetype), "filetype");

                using var request = new HttpRequestMessage(HttpMethod.Post, "parse/image")
                {
                    Content = form
                };

                return await client.SendAsync(request, cancellationToken);
            }));
}

