using System.ComponentModel;
using System.Net.Http.Headers;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Tinfoil;

public static class TinfoilDocuments
{
    [Description("Convert documents into structured formats using Tinfoil's Docling-compatible document processing service.")]
    [McpServerTool(Title = "Tinfoil convert document", Name = "tinfoil_convert_document", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> Tinfoil_Convert_Document(
        [Description("File url of the input document or image. This tool can access secure SharePoint and OneDrive links.")] string fileUrl,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Model identifier. Default is doc-upload.")] string? model = null,
        [Description("Output formats. Allowed values: md, json, yaml, html, text, doctags. Default is [md].")] List<string>? toFormats = null,
        [Description("Input formats. Allowed values: pdf, docx, pptx, xlsx, html, image, asciidoc, md, csv.")] List<string>? fromFormats = null,
        [Description("Processing pipeline. Default is standard.")] string? pipeline = null,
        [Description("Enable OCR for scanned documents.")] bool? doOcr = null,
        [Description("Include images in output.")] bool? includeImages = null,
        [Description("Image handling: placeholder, embedded, referenced.")] string? imageExportMode = null,
        [Description("Extract table structure.")] bool? doTableStructure = null,
        [Description("Extract formulas and enrich math output.")] bool? doFormulaEnrichment = null,
        [Description("OCR engine selection (Docling-compatible).")]
        string? ocrEngine = null,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var client = serviceProvider.GetRequiredService<TinfoilClient>();
                var downloadService = serviceProvider.GetRequiredService<DownloadService>();

                var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
                if (files is null || !files.Any())
                    throw new Exception("No file found for Tinfoil document input.");

                using var form = new MultipartFormDataContent();

                var index = 0;
                foreach (var file in files)
                {
                    var fileName = string.IsNullOrWhiteSpace(file.Filename) ? $"file-{++index}" : file.Filename;
                    var fileContent = new ByteArrayContent(file.Contents.ToArray());
                    fileContent.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(file.MimeType)
                        ? "application/octet-stream"
                        : file.MimeType);
                    form.Add(fileContent, "files", fileName);
                }

                var resolvedModel = string.IsNullOrWhiteSpace(model) ? "doc-upload" : model.Trim();
                form.Add(new StringContent(resolvedModel), "model");

                var resolvedPipeline = string.IsNullOrWhiteSpace(pipeline) ? "standard" : pipeline.Trim();
                form.Add(new StringContent(resolvedPipeline), "pipeline");

                var requestedToFormats = (toFormats ?? [])
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (requestedToFormats.Count == 0)
                {
                    form.Add(new StringContent("md"), "to_formats");
                }
                else
                {
                    foreach (var format in requestedToFormats)
                        form.Add(new StringContent(format), "to_formats");
                }

                var requestedFromFormats = (fromFormats ?? [])
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var format in requestedFromFormats)
                    form.Add(new StringContent(format), "from_formats");

                if (doOcr.HasValue)
                    form.Add(new StringContent(doOcr.Value ? "true" : "false"), "do_ocr");
                if (includeImages.HasValue)
                    form.Add(new StringContent(includeImages.Value ? "true" : "false"), "include_images");
                if (!string.IsNullOrWhiteSpace(imageExportMode))
                    form.Add(new StringContent(imageExportMode), "image_export_mode");
                if (doTableStructure.HasValue)
                    form.Add(new StringContent(doTableStructure.Value ? "true" : "false"), "do_table_structure");
                if (doFormulaEnrichment.HasValue)
                    form.Add(new StringContent(doFormulaEnrichment.Value ? "true" : "false"), "do_formula_enrichment");
                if (!string.IsNullOrWhiteSpace(ocrEngine))
                    form.Add(new StringContent(ocrEngine), "ocr_engine");

                return await client.PostMultipartAsync("v1/convert/file", form, cancellationToken)
                    ?? throw new Exception("Tinfoil returned no response.");
            }));
}
