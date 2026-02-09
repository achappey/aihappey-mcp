using System.ComponentModel;
using System.Net.Http.Headers;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.OpperAI.OCR;

public static class OpperAIOCR
{
    [Description("Extract text from an image or PDF using Opper OCR. The file is downloaded from fileUrl first.")]
    [McpServerTool(Name = "opperai_ocr", Title = "Opper AI OCR")]
    public static async Task<CallToolResult?> OpperAI_OCR(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("File URL or SharePoint/OneDrive reference")] string fileUrl,
        [Description("OCR model name, e.g. mistral/ocr-latest")] string model = "mistral/ocr-latest",
        [Description("Optional language code, e.g. en or nl")] string? language = null,
        [Description("Include image base64 in response where supported")] bool includeImageBase64 = false,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var opper = serviceProvider.GetRequiredService<OpperAIClient>();
                var downloadService = serviceProvider.GetRequiredService<DownloadService>();

                var files = await downloadService.DownloadContentAsync(
                    serviceProvider,
                    requestContext.Server,
                    fileUrl,
                    cancellationToken);

                var file = files.FirstOrDefault()
                    ?? throw new Exception("No file found for Opper OCR input.");

                using var form = new MultipartFormDataContent();
                var fileContent = new ByteArrayContent(file.Contents.ToArray());
                fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.MimeType ?? "application/octet-stream");

                form.Add(fileContent, "file", file.Filename ?? "input.bin");
                form.Add(new StringContent(model), "model");
                form.Add(new StringContent(includeImageBase64 ? "true" : "false"), "include_image_base64");

                if (!string.IsNullOrWhiteSpace(language))
                    form.Add(new StringContent(language), "language");

                using var request = new HttpRequestMessage(HttpMethod.Post, "ocr")
                {
                    Content = form
                };

                return await opper.SendAsync(request, cancellationToken);
            }));
}

