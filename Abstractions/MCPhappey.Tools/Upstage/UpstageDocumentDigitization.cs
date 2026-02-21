using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Upstage;

public static class UpstageDocumentDigitization
{
    [Description("Parse documents using Upstage Document Parsing API (/document-digitization, document-parse). Supports SharePoint/OneDrive fileUrl input.")]
    [McpServerTool(Name = "upstage_document_digitization_parse", Title = "Upstage document parsing", IconSource = UpstageConstants.ICON_SOURCE, ReadOnly = true)]
    public static async Task<CallToolResult?> Upstage_Document_Digitization_Parse(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Document file URL (SharePoint/OneDrive/HTTPS).")]
        string fileUrl,
        [Description("Model alias/version. Default: document-parse.")]
        string model = "document-parse",
        [Description("Parsing mode: standard, enhanced, auto.")]
        string mode = "standard",
        [Description("Enable chart recognition.")]
        bool chartRecognition = true,
        [Description("Merge tables split over multiple pages.")]
        bool mergeMultipageTables = false,
        [Description("OCR mode: auto or force.")]
        string ocr = "auto",
        [Description("Output formats (provider format), e.g. ['html'] or ['text','markdown'].")]
        string? outputFormats = null,
        [Description("Include coordinates in output.")]
        bool coordinates = true,
        [Description("Base64 encoding categories (provider format), e.g. ['table'].")]
        string? base64Encoding = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                if (string.IsNullOrWhiteSpace(fileUrl))
                    throw new ArgumentException("fileUrl is required.");

                var downloadService = serviceProvider.GetRequiredService<DownloadService>();
                var upstage = serviceProvider.GetRequiredService<UpstageClient>();
                var file = await DownloadSingleAsync(serviceProvider, requestContext, downloadService, fileUrl, cancellationToken);

                using var form = new MultipartFormDataContent();

                var fileContent = new ByteArrayContent(file.Contents.ToArray());
                fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.MimeType);
                form.Add(fileContent, "document", file.Filename ?? "document.bin");

                form.Add(new StringContent(model), "model");
                form.Add(new StringContent(mode), "mode");
                form.Add(new StringContent(chartRecognition ? "true" : "false"), "chart_recognition");
                form.Add(new StringContent(mergeMultipageTables ? "true" : "false"), "merge_multipage_tables");
                form.Add(new StringContent(ocr), "ocr");
                form.Add(new StringContent(coordinates ? "true" : "false"), "coordinates");

                if (!string.IsNullOrWhiteSpace(outputFormats))
                    form.Add(new StringContent(outputFormats), "output_formats");

                if (!string.IsNullOrWhiteSpace(base64Encoding))
                    form.Add(new StringContent(base64Encoding), "base64_encoding");

                using var req = new HttpRequestMessage(HttpMethod.Post, "document-digitization") { Content = form };
                return await upstage.SendAsync(req, cancellationToken);
            }));

    [Description("Run OCR using Upstage Document OCR API (/document-digitization, ocr). Supports SharePoint/OneDrive fileUrl input. Schema is loaded from schemaFileUrl.")]
    [McpServerTool(Name = "upstage_document_digitization_ocr", Title = "Upstage document OCR", IconSource = UpstageConstants.ICON_SOURCE, ReadOnly = true)]
    public static async Task<CallToolResult?> Upstage_Document_Digitization_Ocr(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Document file URL (SharePoint/OneDrive/HTTPS).")]
        string fileUrl,
        [Description("Model alias/version. Default: ocr.")]
        string model = "ocr",
        [Description("Optional schema file URL. Value in file should resolve to clova or google.")]
        string? schemaFileUrl = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                if (string.IsNullOrWhiteSpace(fileUrl))
                    throw new ArgumentException("fileUrl is required.");

                var downloadService = serviceProvider.GetRequiredService<DownloadService>();
                var upstage = serviceProvider.GetRequiredService<UpstageClient>();
                var file = await DownloadSingleAsync(serviceProvider, requestContext, downloadService, fileUrl, cancellationToken);

                using var form = new MultipartFormDataContent();

                var fileContent = new ByteArrayContent(file.Contents.ToArray());
                fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.MimeType);
                form.Add(fileContent, "document", file.Filename ?? "document.bin");

                form.Add(new StringContent(model), "model");

                if (!string.IsNullOrWhiteSpace(schemaFileUrl))
                {
                    var schema = await DownloadPrimitiveStringAsync(serviceProvider, requestContext, downloadService, schemaFileUrl!, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(schema))
                        form.Add(new StringContent(schema), "schema");
                }

                using var req = new HttpRequestMessage(HttpMethod.Post, "document-digitization") { Content = form };
                return await upstage.SendAsync(req, cancellationToken);
            }));

    private static async Task<MCPhappey.Common.Models.FileItem> DownloadSingleAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        DownloadService downloadService,
        string url,
        CancellationToken cancellationToken)
    {
        var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, url, cancellationToken);
        return files.FirstOrDefault() ?? throw new Exception($"No file content could be downloaded from: {url}");
    }

    private static async Task<string> DownloadPrimitiveStringAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        DownloadService downloadService,
        string fileUrl,
        CancellationToken cancellationToken)
    {
        var file = await DownloadSingleAsync(serviceProvider, requestContext, downloadService, fileUrl, cancellationToken);
        var raw = file.Contents.ToString().Trim();

        try
        {
            var node = JsonNode.Parse(raw);
            if (node is JsonValue value && value.TryGetValue<string>(out var stringValue))
                return stringValue;

            if (node is JsonObject obj)
            {
                if (obj["schema"] is JsonValue schemaValue && schemaValue.TryGetValue<string>(out var schema))
                    return schema;
                if (obj["value"] is JsonValue valueField && valueField.TryGetValue<string>(out var valueText))
                    return valueText;
            }
        }
        catch
        {
            // Fall back to raw text
        }

        return raw.Trim('"');
    }
}

