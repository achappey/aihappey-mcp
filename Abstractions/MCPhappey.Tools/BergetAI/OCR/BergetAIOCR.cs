using System.ComponentModel;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;

namespace MCPhappey.Tools.BergetAI.OCR;

public static class BergetAIOCR
{
    [Description("Extract text content from a document using Berget AI OCR. The file is downloaded from fileUrl first (supports SharePoint and OneDrive).")]
    [McpServerTool(Name = "bergetai_ocr", Title = "Berget AI OCR")]
    public static async Task<CallToolResult?> BergetAI_OCR(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("File URL or SharePoint/OneDrive reference")] string fileUrl,
        [Description("Model identifier to use. Default: docling-v1")] string model = "docling-v1",
        [Description("Table extraction mode: accurate, fast, or none")] string tableMode = "accurate",
        [Description("OCR method: easyocr, doctr, tesseract, or auto")] string ocrMethod = "easyocr",
        [Description("Perform OCR. Default: true")] bool doOcr = true,
        [Description("Extract table structure. Default: true")] bool doTableStructure = true,
        [Description("Output format. Default: md")] string outputFormat = "md",
        [Description("Include images in output. Default: false")] bool includeImages = false,
        [Description("When true, saves the OCR result beside the source file using the same filename plus .LLMs.<ext> when possible, otherwise falls back to the default MCP output location, and returns only a resource link.")] bool saveOutput = false,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            {
                var result = await ExecuteOcrAsync(serviceProvider, requestContext, fileUrl, model, tableMode, ocrMethod, doOcr, doTableStructure, outputFormat, includeImages, cancellationToken);

                if (saveOutput)
                {
                    var savedOutput = result.ToSavedOutput(outputFormat, outputFormat, "content", "output", "result", "markdown", "text", "html", "doctags");
                    return await requestContext.SaveOutputAsync(serviceProvider, savedOutput.Content, savedOutput.Extension, cancellationToken: cancellationToken);
                }

                return new CallToolResult
                {
                    Meta = await requestContext.GetToolMeta(),
                    StructuredContent = (result ?? new JsonElement()).ToJsonElement()
                };
            });

    private static async Task<JsonElement?> ExecuteOcrAsync(
      IServiceProvider serviceProvider,
      RequestContext<CallToolRequestParams> requestContext,
      string fileUrl,
      string model,
      string tableMode,
      string ocrMethod,
      bool doOcr,
      bool doTableStructure,
      string outputFormat,
      bool includeImages,
      CancellationToken cancellationToken)
    {
        var berget = serviceProvider.GetRequiredService<BergetAIClient>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();

        var files = await downloadService.DownloadContentAsync(
            serviceProvider,
            requestContext.Server,
            fileUrl,
            cancellationToken);

        var file = files.FirstOrDefault()
            ?? throw new Exception("No file found for Berget AI OCR input.");

        var documentUri = file.ToDataUri();
        var inputFormat = ResolveInputFormat(file.Filename, file.MimeType);

        var body = new
        {
            model,
            document = new
            {
                url = documentUri,
                type = "document"
            },
            @async = false,
            options = new
            {
                tableMode,
                ocrMethod,
                doOcr,
                doTableStructure,
                inputFormat = new[] { inputFormat },
                outputFormat,
                includeImages
            }
        };

        return await berget.PostJsonAsync("v1/ocr", body, cancellationToken);
    }
    private static string ResolveInputFormat(string? filename, string? mimeType)
    {
        var extension = Path.GetExtension(filename ?? string.Empty).TrimStart('.').ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(extension))
            return extension;

        if (string.IsNullOrWhiteSpace(mimeType))
            return "pdf";

        return mimeType.ToLowerInvariant() switch
        {
            "application/pdf" => "pdf",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => "docx",
            "application/vnd.openxmlformats-officedocument.presentationml.presentation" => "pptx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => "xlsx",
            "text/html" => "html",
            "text/markdown" => "md",
            "text/csv" => "csv",
            _ when mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) => "image",
            _ => "pdf"
        };
    }
}

