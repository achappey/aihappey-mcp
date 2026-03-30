using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
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
        [Description("When true, saves the OCR JSON result beside the source file using the same filename plus .LLMs.json when possible, otherwise falls back to the default MCP output location, and returns only a resource link.")] bool saveOutput = false,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            {
                var result = await ExecuteOcrAsync(serviceProvider, requestContext, fileUrl, model, language, includeImageBase64, cancellationToken);

                if (saveOutput)
                    return await requestContext.SaveOutputAsync(serviceProvider, BinaryData.FromString(result?.ToJsonString() ?? "{}"), "json", cancellationToken: cancellationToken);

                return new CallToolResult
                {
                    Meta = await requestContext.GetToolMeta(),
                    StructuredContent = (result ?? new JsonObject()).ToJsonElement()
                };
            });

    private static async Task<JsonNode?> ExecuteOcrAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string fileUrl,
        string model,
        string? language,
        bool includeImageBase64,
        CancellationToken cancellationToken)
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
    }
}

