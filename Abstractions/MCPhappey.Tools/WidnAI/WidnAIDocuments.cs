using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.WidnAI;

public static class WidnAIDocuments
{
    [Description("Translate a document end-to-end with WidnAI from fileUrl (SharePoint/OneDrive/HTTPS): upload source, start translation, poll until completed, download translated output, upload it to SharePoint/OneDrive, and return only a resource link block.")]
    [McpServerTool(
        Name = "widnai_documents_translate_file",
        Title = "WidnAI translate document",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> WidnAI_Documents_TranslateFile(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Input file URL to translate (supports secured SharePoint/OneDrive and HTTPS links).")]
        string fileUrl,
        [Description("Source language locale (e.g., en, nl-NL).")]
        string sourceLocale,
        [Description("Target language locale (e.g., pt-PT, fr-FR).")]
        string targetLocale,
        [Description("Translation model identifier. Default: vesuvius.")]
        string model = "vesuvius",
        [Description("Optional glossary id.")]
        string? glossaryId = null,
        [Description("Optional translation instructions.")]
        string? instructions = null,
        [Description("Optional tone, e.g., formal, casual.")]
        string? tone = null,
        [Description("JSON string for few-shot examples array. Default: [].")]
        string fewshotExamplesJson = "[]",
        [Description("Optional output filename without extension.")]
        string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new WidnAITranslateFileRequest
                {
                    FileUrl = fileUrl,
                    SourceLocale = sourceLocale,
                    TargetLocale = targetLocale,
                    Model = model,
                    GlossaryId = glossaryId,
                    Instructions = instructions,
                    Tone = tone,
                    FewshotExamplesJson = fewshotExamplesJson,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            ArgumentException.ThrowIfNullOrWhiteSpace(typed.FileUrl);
            ArgumentException.ThrowIfNullOrWhiteSpace(typed.SourceLocale);
            ArgumentException.ThrowIfNullOrWhiteSpace(typed.TargetLocale);


            var downloadService = serviceProvider.GetRequiredService<DownloadService>();
            var widn = serviceProvider.GetRequiredService<WidnAIClient>();

            var inputFiles = await downloadService.DownloadContentAsync(
                serviceProvider,
                requestContext.Server,
                typed.FileUrl,
                cancellationToken);

            var inputFile = inputFiles.FirstOrDefault()
                ?? throw new InvalidOperationException("Failed to download input file from fileUrl.");

            var uploadResult = await UploadSourceFileAsync(widn, inputFile, cancellationToken);
            if (string.IsNullOrWhiteSpace(uploadResult.FileId))
                throw new InvalidOperationException("WidnAI upload succeeded but did not return fileId.");

            var translateBody = BuildTranslateBody(typed);
            await widn.PostJsonNoContentAsync($"translate-file/{Uri.EscapeDataString(uploadResult.FileId)}/translate", translateBody, cancellationToken);

            var finalStatus = await WaitForCompletionAsync(
                widn,
                uploadResult.FileId,
                3,
                900,
                requestContext,
                cancellationToken);

            if (string.Equals(finalStatus, "failed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(finalStatus, "error", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(finalStatus, "canceled", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(finalStatus, "cancelled", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"WidnAI translation failed with status '{finalStatus}'.");
            }

            var (bytes, contentType) = await widn.DownloadAsync($"translate-file/{Uri.EscapeDataString(uploadResult.FileId)}/download", cancellationToken);
            if (bytes.Length == 0)
                throw new InvalidOperationException("Downloaded translated file is empty.");

            var ext = ResolveExtension(inputFile.Filename, contentType);
            var outputName = typed.Filename.EndsWith($".{ext}", StringComparison.OrdinalIgnoreCase)
                ? typed.Filename
                : $"{typed.Filename}.{ext}";

            var uploaded = await requestContext.Server.Upload(
                serviceProvider,
                outputName,
                BinaryData.FromBytes(bytes),
                cancellationToken);

            return uploaded?.ToResourceLinkCallToolResponse();
        });

    private static object BuildTranslateBody(WidnAITranslateFileRequest request)
    {
        var examples = ParseFewShotExamples(request.FewshotExamplesJson);

        return new
        {
            config = new
            {
                sourceLocale = request.SourceLocale,
                targetLocale = request.TargetLocale,
                model = string.IsNullOrWhiteSpace(request.Model) ? "vesuvius" : request.Model,
                glossaryId = string.IsNullOrWhiteSpace(request.GlossaryId) ? null : request.GlossaryId,
                instructions = string.IsNullOrWhiteSpace(request.Instructions) ? null : request.Instructions,
                tone = string.IsNullOrWhiteSpace(request.Tone) ? null : request.Tone,
                fewshotExamples = examples
            }
        };
    }

    private static JsonArray ParseFewShotExamples(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
            return [];

        try
        {
            var node = JsonNode.Parse(rawJson);
            if (node is JsonArray arr)
                return arr;

            return [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static async Task<WidnUploadResponse> UploadSourceFileAsync(
        WidnAIClient widn,
        MCPhappey.Common.Models.FileItem file,
        CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(file.Contents.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(file.MimeType)
            ? "application/octet-stream"
            : file.MimeType);
        form.Add(content, "file", string.IsNullOrWhiteSpace(file.Filename) ? "document.bin" : file.Filename);

        var result = await widn.PostMultipartAsync("translate-file", form, cancellationToken)
            ?? throw new InvalidOperationException("WidnAI returned no upload response.");

        var fileId = result["fileId"]?.GetValue<string>();
        var encryptionKey = result["encryptionKey"]?.GetValue<string>();
        return new WidnUploadResponse(fileId ?? string.Empty, encryptionKey);
    }

    private static async Task<string> WaitForCompletionAsync(
        WidnAIClient widn,
        string fileId,
        int pollIntervalSeconds,
        int maxWaitSeconds,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var poll = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (DateTimeOffset.UtcNow - startedAt > TimeSpan.FromSeconds(maxWaitSeconds))
                throw new TimeoutException($"WidnAI translation timed out after {maxWaitSeconds}s.");

            var node = await widn.GetJsonAsync($"translate-file/{Uri.EscapeDataString(fileId)}", cancellationToken)
                ?? throw new InvalidOperationException("WidnAI status response was empty.");

            var status = node["status"]?.GetValue<string>() ?? string.Empty;
            var percentage = node["statusPercentage"]?.GetValue<int?>();

            poll++;
            await requestContext.Server.SendMessageNotificationAsync(
                $"WidnAI translation status: {status} ({(percentage.HasValue ? $"{percentage.Value}%" : "n/a")}, poll #{poll})",
                LoggingLevel.Info,
                cancellationToken);

            if (IsFinalStatus(status))
                return status;

            await Task.Delay(TimeSpan.FromSeconds(pollIntervalSeconds), cancellationToken);
        }
    }

    private static bool IsFinalStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return false;

        return status.Equals("completed", StringComparison.OrdinalIgnoreCase)
               || status.Equals("done", StringComparison.OrdinalIgnoreCase)
               || status.Equals("failed", StringComparison.OrdinalIgnoreCase)
               || status.Equals("error", StringComparison.OrdinalIgnoreCase)
               || status.Equals("canceled", StringComparison.OrdinalIgnoreCase)
               || status.Equals("cancelled", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveExtension(string? filename, string? contentType)
    {
        if (!string.IsNullOrWhiteSpace(filename))
        {
            var ext = Path.GetExtension(filename);
            if (!string.IsNullOrWhiteSpace(ext))
                return ext.TrimStart('.');
        }

        return contentType?.ToLowerInvariant() switch
        {
            "application/pdf" => "pdf",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => "docx",
            "application/vnd.openxmlformats-officedocument.presentationml.presentation" => "pptx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => "xlsx",
            "text/plain" => "txt",
            _ => "bin"
        };
    }

    private sealed record WidnUploadResponse(string FileId, string? EncryptionKey);

    [Description("WidnAI document translation input.")]
    private sealed class WidnAITranslateFileRequest
    {
        [Description("Input file URL (SharePoint/OneDrive/HTTPS).")]
        public string FileUrl { get; set; } = string.Empty;

        [Description("Source locale, e.g., en.")]
        public string SourceLocale { get; set; } = string.Empty;

        [Description("Target locale, e.g., nl-NL.")]
        public string TargetLocale { get; set; } = string.Empty;

        [Description("Model identifier. Default: vesuvius.")]
        public string Model { get; set; } = "vesuvius";

        [Description("Optional glossary id.")]
        public string? GlossaryId { get; set; }

        [Description("Optional translation instructions.")]
        public string? Instructions { get; set; }

        [Description("Optional tone.")]
        public string? Tone { get; set; }

        [Description("Few-shot examples array in JSON string form.")]
        public string FewshotExamplesJson { get; set; } = "[]";

        [Description("Output filename without extension.")]
        public string Filename { get; set; } = string.Empty;
    }
}

