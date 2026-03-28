using System.ComponentModel;
using System.Net.Http.Headers;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.GreenPT;

public static class GreenPTDocuments
{
    [Description("Process a document or image into AI-ready output using GreenPT Documents with OCR and table extraction support.")]
    [McpServerTool(Title = "GreenPT convert document", Name = "greenpt_documents_convert", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> GreenPT_Documents_Convert(
        [Description("File url of the input document or image. This tool can access secure SharePoint and OneDrive links.")] string fileUrl,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional output formats. Allowed values: md, json, html, html_split_page, text, doctags. Default is [md].")] List<string>? toFormats = null,
        [Description("Enable OCR processing for bitmap content. Default true.")] bool? doOcr = null,
        [Description("When true, replace existing text with OCR-generated text. Default false.")] bool? forceOcr = null,
        [Description("Extract table structure from documents. Default true.")] bool? doTableStructure = null,
        [Description("Table detection mode. Allowed values: fast, accurate. Default accurate.")] string? tableMode = null,
        [Description("Extract images from the document. Default true.")] bool? includeImages = null,
        [Description("When true, saves the converted output beside the source file using the same filename plus .LLMs.<ext> when possible, otherwise falls back to the default MCP output location, and returns only a resource link.")] bool saveOutput = false,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
            {
                var requestedFormat = ResolveRequestedFormat(toFormats, "md");
                var result = await ExecuteConvertAsync(serviceProvider, requestContext, fileUrl, toFormats, doOcr, forceOcr, doTableStructure, tableMode, includeImages, cancellationToken);

                if (saveOutput)
                {
                    var savedOutput = result.ToSavedOutput(requestedFormat, requestedFormat, "content", "output", "result", "markdown", "text", "html", "doctags");
                    return await requestContext.SaveOutputAsync(serviceProvider, savedOutput.Content, savedOutput.Extension, cancellationToken: cancellationToken);
                }

                return new CallToolResult
                {
                    Meta = await requestContext.GetToolMeta(),
                    StructuredContent = result
                };
            });

    private static async Task<System.Text.Json.Nodes.JsonNode> ExecuteConvertAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string fileUrl,
        List<string>? toFormats,
        bool? doOcr,
        bool? forceOcr,
        bool? doTableStructure,
        string? tableMode,
        bool? includeImages,
        CancellationToken cancellationToken)
    {
        var client = serviceProvider.GetRequiredService<GreenPTClient>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();

        var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
        if (files is null || !files.Any())
            throw new Exception("No file found for GreenPT Documents input.");

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

        var requestedFormats = (toFormats ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (requestedFormats.Count == 0)
        {
            form.Add(new StringContent("md"), "to_formats");
        }
        else
        {
            foreach (var format in requestedFormats)
                form.Add(new StringContent(format), "to_formats");
        }

        if (doOcr.HasValue)
            form.Add(new StringContent(doOcr.Value ? "true" : "false"), "do_ocr");
        if (forceOcr.HasValue)
            form.Add(new StringContent(forceOcr.Value ? "true" : "false"), "force_ocr");
        if (doTableStructure.HasValue)
            form.Add(new StringContent(doTableStructure.Value ? "true" : "false"), "do_table_structure");
        if (!string.IsNullOrWhiteSpace(tableMode))
            form.Add(new StringContent(tableMode), "table_mode");
        if (includeImages.HasValue)
            form.Add(new StringContent(includeImages.Value ? "true" : "false"), "include_images");

        return await client.PostMultipartAsync("v1/tools/documents/convert/file", form, cancellationToken)
            ?? throw new Exception("GreenPT returned no response.");
    }

    private static string ResolveRequestedFormat(List<string>? formats, string fallback)
        => formats?
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?
            .Trim()
            ?? fallback;
}
