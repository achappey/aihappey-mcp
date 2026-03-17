using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.DumplingAI;

public static class DumplingAIDocumentProcessing
{
    [Description("Convert a PDF or DOCX document into plain text using a public URL or base64 file content.")]
    [McpServerTool(Title = "DumplingAI doc to text", Name = "dumplingai_doc_to_text", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> DumplingAI_DocToText(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The document file URL. SharePoint and OneDrive URLs are supported and will be downloaded server-side before sending to DumplingAI.")] string fileUrl,
        [Description("Optional page selection string such as 1-3,5 or !1.")] string? pages = null,
        [Description("Optional DumplingAI request source.")] string? requestSource = null,
        CancellationToken cancellationToken = default)
        => await ExecuteSingleFileAsync(
            serviceProvider,
            requestContext,
            "/doc-to-text",
            fileUrl,
            new JsonObject
            {
                ["pages"] = pages,
                ["requestSource"] = requestSource
            },
            cancellationToken,
            "DumplingAI document-to-text conversion completed.");

    [Description("Convert a supported document or image into a PDF using a public URL or base64 file content.")]
    [McpServerTool(Title = "DumplingAI convert to PDF", Name = "dumplingai_convert_to_pdf", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> DumplingAI_ConvertToPdf(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The source file URL. SharePoint and OneDrive URLs are supported and will be downloaded server-side before sending to DumplingAI.")] string fileUrl,
        CancellationToken cancellationToken = default)
        => await ExecuteSingleFileAsync(
            serviceProvider,
            requestContext,
            "/convert-to-pdf",
            fileUrl,
            null,
            cancellationToken,
            "DumplingAI PDF conversion completed.");

    [Description("Merge multiple PDF files into one PDF using public URLs or base64 file contents.")]
    [McpServerTool(Title = "DumplingAI merge PDFs", Name = "dumplingai_merge_pdfs", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> DumplingAI_MergePdfs(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The PDF file URLs to merge. Each fileUrl is downloaded server-side, so SharePoint and OneDrive URLs are supported. Provide at least two.")] string[] fileUrls,
        [Description("Optional PDF metadata to embed in the merged file.")] string? metadataJson = null,
        [Description("Optional PDF/A compliance level: PDF/A-1b, PDF/A-2b, or PDF/A-3b.")] string? pdfa = null,
        [Description("Enable PDF/UA compliance when true.")] bool? pdfua = null,
        [Description("Optional DumplingAI request source.")] string? requestSource = null,
        CancellationToken cancellationToken = default)
        => await ExecuteMultipleFilesAsync(
            serviceProvider,
            requestContext,
            "/merge-pdfs",
            fileUrls,
            new JsonObject
            {
                ["metadata"] = ParseJsonObject(metadataJson, "metadataJson"),
                ["pdfa"] = pdfa,
                ["pdfua"] = pdfua,
                ["requestSource"] = requestSource
            },
            cancellationToken,
            "DumplingAI PDF merge completed.",
            minimumFiles: 2);

    [Description("Read metadata embedded in one or more PDF files using public URLs or base64 file contents.")]
    [McpServerTool(Title = "DumplingAI read PDF metadata", Name = "dumplingai_read_pdf_metadata", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> DumplingAI_ReadPdfMetadata(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The PDF file URLs to inspect. Each fileUrl is downloaded server-side, so SharePoint and OneDrive URLs are supported.")] string[] fileUrls,
        [Description("Optional DumplingAI request source.")] string? requestSource = null,
        CancellationToken cancellationToken = default)
        => await ExecuteMultipleFilesAsync(
            serviceProvider,
            requestContext,
            "/read-pdf-metadata",
            fileUrls,
            new JsonObject
            {
                ["requestSource"] = requestSource
            },
            cancellationToken,
            "DumplingAI PDF metadata read completed.");

    [Description("Write metadata into one or more PDF files using public URLs or base64 file contents.")]
    [McpServerTool(Title = "DumplingAI write PDF metadata", Name = "dumplingai_write_pdf_metadata", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> DumplingAI_WritePdfMetadata(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The PDF file URLs to update. Each fileUrl is downloaded server-side, so SharePoint and OneDrive URLs are supported.")] string[] fileUrls,
        [Description("Metadata object as JSON, for example {\"Title\":\"Quarterly Report\",\"Author\":\"MCPhappey\"}.")] string metadataJson = "{}",
        CancellationToken cancellationToken = default)
        => await ExecuteMultipleFilesAsync(
            serviceProvider,
            requestContext,
            "/write-pdf-metadata",
            fileUrls,
            new JsonObject
            {
                ["metadata"] = EnsureNonEmptyJsonObject(ParseJsonObject(metadataJson, "metadataJson"), "metadataJson")
            },
            cancellationToken,
            "DumplingAI PDF metadata write completed.");

    [Description("Extract structured data from one or more documents using a prompt and public URLs or base64 file contents.")]
    [McpServerTool(Title = "DumplingAI extract document", Name = "dumplingai_extract_document", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> DumplingAI_ExtractDocument(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The extraction prompt describing what to capture from the document files.")] string prompt,
        [Description("The document file URLs to process. Each fileUrl is downloaded server-side, so SharePoint and OneDrive URLs are supported.")] string[] fileUrls,
        [Description("Optional file extension such as .pdf, .docx, or autodetect.")] string? fileExtension = null,
        [Description("Request JSON-formatted output when true.")] bool? jsonMode = null,
        [Description("Optional DumplingAI request source.")] string? requestSource = null,
        CancellationToken cancellationToken = default)
        => await ExecuteMultipleFilesAsync(
            serviceProvider,
            requestContext,
            "/extract-document",
            fileUrls,
            new JsonObject
            {
                ["fileExtension"] = fileExtension,
                ["prompt"] = prompt,
                ["jsonMode"] = jsonMode,
                ["requestSource"] = requestSource
            },
            cancellationToken,
            "DumplingAI document extraction completed.",
            validate: () => EnsureRequired(prompt, "prompt"));

    [Description("Extract OCR text or structured insights from one or more images using a prompt and public URLs or base64 image contents.")]
    [McpServerTool(Title = "DumplingAI extract image", Name = "dumplingai_extract_image", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> DumplingAI_ExtractImage(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The extraction prompt describing what to capture from the images.")] string prompt,
        [Description("The image file URLs to process. Each fileUrl is downloaded server-side, so SharePoint and OneDrive URLs are supported.")] string[] fileUrls,
        [Description("Request JSON-formatted output when true.")] bool? jsonMode = null,
        [Description("Optional DumplingAI request source.")] string? requestSource = null,
        CancellationToken cancellationToken = default)
        => await ExecuteArrayPayloadAsync(
            serviceProvider,
            requestContext,
            "/extract-image",
            "images",
            fileUrls,
            new JsonObject
            {
                ["prompt"] = prompt,
                ["jsonMode"] = jsonMode,
                ["requestSource"] = requestSource
            },
            cancellationToken,
            "DumplingAI image extraction completed.",
            validate: () => EnsureRequired(prompt, "prompt"));

    [Description("Extract transcripts or structured insights from an audio file using a prompt and either a public URL or base64 audio content.")]
    [McpServerTool(Title = "DumplingAI extract audio", Name = "dumplingai_extract_audio", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> DumplingAI_ExtractAudio(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The extraction prompt describing what to capture from the audio.")] string prompt,
        [Description("The audio file URL. SharePoint and OneDrive URLs are supported and will be downloaded server-side before sending to DumplingAI.")] string fileUrl,
        [Description("Request JSON-formatted output when true.")] bool? jsonMode = null,
        [Description("Optional DumplingAI request source.")] string? requestSource = null,
        CancellationToken cancellationToken = default)
        => await ExecuteNamedSingleInputAsync(
            serviceProvider,
            requestContext,
            "/extract-audio",
            "audio",
            fileUrl,
            new JsonObject
            {
                ["prompt"] = prompt,
                ["jsonMode"] = jsonMode,
                ["requestSource"] = requestSource
            },
            cancellationToken,
            "DumplingAI audio extraction completed.",
            validate: () => EnsureRequired(prompt, "prompt"));

    [Description("Extract transcripts or structured insights from a video file using a prompt and either a public URL or base64 video content.")]
    [McpServerTool(Title = "DumplingAI extract video", Name = "dumplingai_extract_video", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> DumplingAI_ExtractVideo(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The extraction prompt describing what to capture from the video.")] string prompt,
        [Description("The video file URL. SharePoint and OneDrive URLs are supported and will be downloaded server-side before sending to DumplingAI.")] string fileUrl,
        [Description("Request JSON-formatted output when true.")] bool? jsonMode = null,
        [Description("Optional DumplingAI request source.")] string? requestSource = null,
        CancellationToken cancellationToken = default)
        => await ExecuteNamedSingleInputAsync(
            serviceProvider,
            requestContext,
            "/extract-video",
            "video",
            fileUrl,
            new JsonObject
            {
                ["prompt"] = prompt,
                ["jsonMode"] = jsonMode,
                ["requestSource"] = requestSource
            },
            cancellationToken,
            "DumplingAI video extraction completed.",
            validate: () => EnsureRequired(prompt, "prompt"));

    [Description("Trim a public MP4 video URL to a start and end timestamp and return the trimmed asset.")]
    [McpServerTool(Title = "DumplingAI trim video", Name = "dumplingai_trim_video", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> DumplingAI_TrimVideo(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The public MP4 URL to trim.")] string videoUrl,
        [Description("The start timestamp in HH:MM:SS or HH:MM:SS.mmm format.")] string startTimestamp,
        [Description("The end timestamp in HH:MM:SS or HH:MM:SS.mmm format.")] string endTimestamp,
        [Description("Optional DumplingAI request source.")] string? requestSource = null,
        CancellationToken cancellationToken = default)
        => await ExecuteAsync(
            serviceProvider,
            requestContext,
            "/trim-video",
            new JsonObject
            {
                ["videoUrl"] = videoUrl,
                ["startTimestamp"] = startTimestamp,
                ["endTimestamp"] = endTimestamp,
                ["requestSource"] = requestSource
            }.WithoutNulls(),
            cancellationToken,
            "DumplingAI video trim completed.",
            () =>
            {
                EnsureRequired(videoUrl, "videoUrl");
                EnsureRequired(startTimestamp, "startTimestamp");
                EnsureRequired(endTimestamp, "endTimestamp");
            });

    private static async Task<CallToolResult?> ExecuteSingleFileAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string endpoint,
        string fileUrl,
        JsonObject? additionalPayload,
        CancellationToken cancellationToken,
        string summary)
        => await ExecuteNamedSingleInputAsync(
            serviceProvider,
            requestContext,
            endpoint,
            "file",
            fileUrl,
            additionalPayload,
            cancellationToken,
            summary);

    private static async Task<CallToolResult?> ExecuteNamedSingleInputAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string endpoint,
        string payloadKey,
        string fileUrl,
        JsonObject? additionalPayload,
        CancellationToken cancellationToken,
        string summary,
        Action? validate = null)
    {
        EnsureRequired(fileUrl, "fileUrl");
        var file = await DumplingAIHelpers.BuildRequiredBase64FileAsync(
            serviceProvider,
            requestContext,
            fileUrl,
            cancellationToken);

        return await ExecuteAsync(
            serviceProvider,
            requestContext,
            endpoint,
            Merge(
                additionalPayload,
                new JsonObject
                {
                    ["inputMethod"] = "base64",
                    [payloadKey] = file
                }),
            cancellationToken,
            summary,
            validate);
    }

    private static async Task<CallToolResult?> ExecuteMultipleFilesAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string endpoint,
        string[] fileUrls,
        JsonObject? additionalPayload,
        CancellationToken cancellationToken,
        string summary,
        int minimumFiles = 1,
        Action? validate = null)
        => await ExecuteArrayPayloadAsync(
            serviceProvider,
            requestContext,
            endpoint,
            "files",
            fileUrls,
            additionalPayload,
            cancellationToken,
            summary,
            minimumFiles,
            validate);

    private static async Task<CallToolResult?> ExecuteArrayPayloadAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string endpoint,
        string payloadKey,
        string[] fileUrls,
        JsonObject? additionalPayload,
        CancellationToken cancellationToken,
        string summary,
        int minimumFiles = 1,
        Action? validate = null)
    {
        var values = await ResolveDownloadedArrayInput(
            serviceProvider,
            requestContext,
            fileUrls,
            payloadKey,
            minimumFiles,
            cancellationToken);

        return await ExecuteAsync(
            serviceProvider,
            requestContext,
            endpoint,
            Merge(
                additionalPayload,
                new JsonObject
                {
                    ["inputMethod"] = "base64",
                    [payloadKey] = new JsonArray(values.Select(v => JsonValue.Create(v)).ToArray())
                }),
            cancellationToken,
            summary,
            validate);
    }

    private static async Task<string[]> ResolveDownloadedArrayInput(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string[] fileUrls,
        string label,
        int minimumFiles,
        CancellationToken cancellationToken)
    {
        var selected = Normalize(fileUrls);

        if (selected.Length < minimumFiles)
            throw new ValidationException(minimumFiles == 1
                ? $"Provide at least one {label} value."
                : $"Provide at least {minimumFiles} {label} values.");

        return await DumplingAIHelpers.BuildRequiredBase64FilesAsync(
            serviceProvider,
            requestContext,
            selected,
            cancellationToken);
    }

    private static string[] Normalize(IEnumerable<string>? values)
        => values?
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .ToArray()
            ?? [];

    private static JsonObject Merge(JsonObject? left, JsonObject right)
    {
        if (left is not null)
        {
            foreach (var pair in left)
                right[pair.Key] = pair.Value?.DeepClone();
        }

        return right.WithoutNulls();
    }

    private static JsonObject? ParseJsonObject(string? json, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            if (JsonNode.Parse(json) is JsonObject obj)
                return obj;
        }
        catch (Exception ex)
        {
            throw new ValidationException($"{parameterName} must be a valid JSON object.", ex);
        }

        throw new ValidationException($"{parameterName} must be a valid JSON object.");
    }

    private static JsonObject EnsureNonEmptyJsonObject(JsonObject? obj, string parameterName)
    {
        if (obj is null || obj.Count == 0)
            throw new ValidationException($"{parameterName} must contain at least one property.");

        return obj;
    }

    private static void EnsureRequired(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ValidationException($"{name} is required.");
    }

    private static async Task<CallToolResult?> ExecuteAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string endpoint,
        JsonObject payload,
        CancellationToken cancellationToken,
        string summary,
        Action? validate = null)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                validate?.Invoke();

                var client = serviceProvider.GetRequiredService<DumplingAIClient>();
                var response = await client.PostAsync(endpoint, payload, cancellationToken);
                var structured = DumplingAIHelpers.CreateStructuredResponse(endpoint, payload, response);

                return new CallToolResult
                {
                    Meta = await requestContext.GetToolMeta(),
                    StructuredContent = structured,
                    Content = [summary.ToTextContentBlock()]
                };
            }));
}
