using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.StabilityAI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.FishAudio;

public static class FishAudioTranscriptions
{
    private const string AsrPath = "/v1/asr";

    [Description("Transcribe audio from fileUrl using Fish Audio and return structured content.")]
    [McpServerTool(
        Title = "Fish Audio Speech-to-Text",
        Name = "fishaudio_transcriptions_transcribe_fileurl",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> FishAudio_Transcriptions_Transcribe(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Audio file URL to transcribe. Supports SharePoint/OneDrive/HTTP.")]
        string fileUrl,
        [Description("Optional language code hint (e.g. en, nl).")]
        string? language = null,
        [Description("Whether to ignore timestamps. Default: true.")]
        bool ignore_timestamps = true,
        [Description("Output filename without extension.")]
        string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new FishAudioTranscriptionRequest
                {
                    FileUrl = fileUrl,
                    Language = NormalizeOptional(language),
                    IgnoreTimestamps = ignore_timestamps,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null)
                return notAccepted;

            if (typed == null)
                return "No input data provided".ToErrorCallToolResponse();

            ArgumentException.ThrowIfNullOrWhiteSpace(typed.FileUrl);

            var downloadService = serviceProvider.GetRequiredService<DownloadService>();
            var downloads = await downloadService.DownloadContentAsync(
                serviceProvider,
                requestContext.Server,
                typed.FileUrl,
                cancellationToken);

            var media = downloads.FirstOrDefault()
                ?? throw new InvalidOperationException("Failed to download media from fileUrl.");

            using var form = new MultipartFormDataContent
            {
                {
                    new StreamContent(media.Contents.ToStream())
                    {
                        Headers =
                        {
                            ContentType = new MediaTypeHeaderValue(
                                string.IsNullOrWhiteSpace(media.MimeType)
                                    ? "application/octet-stream"
                                    : media.MimeType)
                        }
                    },
                    "audio",
                    string.IsNullOrWhiteSpace(media.Filename) ? "input.bin" : media.Filename
                },
                "ignore_timestamps".NamedField(typed.IgnoreTimestamps.ToString().ToLowerInvariant())
            };

            if (!string.IsNullOrWhiteSpace(typed.Language))
                form.Add("language".NamedField(typed.Language));

            using var client = serviceProvider.CreateFishAudioClient();
            using var resp = await client.PostAsync(AsrPath, form, cancellationToken);
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"Fish Audio ASR failed ({(int)resp.StatusCode}): {body}");

            var text = ExtractText(body);
            var safeName = typed.Filename.ToOutputFileName();
            var uploaded = await requestContext.Server.Upload(
                serviceProvider,
                $"{safeName}.txt",
                BinaryData.FromString(text),
                cancellationToken);

            var structured = BuildStructuredResponse(typed, body, text, uploaded);

            return new CallToolResult
            {
                StructuredContent = structured,
                Content =
                [
                    text.ToTextContentBlock(),
                    uploaded!
                ]
            };
        });

    [Description("Please fill in the Fish Audio transcription request.")]
    public sealed class FishAudioTranscriptionRequest
    {
        [JsonPropertyName("fileUrl")]
        [Required]
        [Description("Audio file URL to transcribe.")]
        public string FileUrl { get; set; } = default!;

        [JsonPropertyName("language")]
        [Description("Optional language hint.")]
        public string? Language { get; set; }

        [JsonPropertyName("ignore_timestamps")]
        [Description("Whether to ignore timestamps in ASR result.")]
        public bool IgnoreTimestamps { get; set; } = true;

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string ExtractText(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("text", out var textElement)
                && textElement.ValueKind == JsonValueKind.String)
            {
                return textElement.GetString() ?? string.Empty;
            }

            return responseBody;
        }
        catch
        {
            return responseBody;
        }
    }

    private static JsonObject BuildStructuredResponse(
        FishAudioTranscriptionRequest typed,
        string rawBody,
        string text,
        ResourceLinkBlock? uploaded)
    {
        JsonNode rawNode;
        try
        {
            rawNode = JsonNode.Parse(rawBody) ?? JsonValue.Create(rawBody)!;
        }
        catch
        {
            rawNode = JsonValue.Create(rawBody)!;
        }

        return new JsonObject
        {
            ["provider"] = "fishaudio",
            ["type"] = "transcription",
            ["fileUrl"] = typed.FileUrl,
            ["language"] = typed.Language,
            ["ignore_timestamps"] = typed.IgnoreTimestamps,
            ["text"] = text,
            ["output"] = new JsonObject
            {
                ["transcriptFileUri"] = uploaded?.Uri,
                ["transcriptFileName"] = uploaded?.Name,
                ["transcriptFileMimeType"] = uploaded?.MimeType
            },
            ["raw"] = rawNode
        };
    }
}

