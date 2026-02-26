using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.StabilityAI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Cartesia;

public static class CartesiaTranscriptions
{
    private const string SttPath = "stt";

    [Description("Transcribe audio from fileUrl using Cartesia batch speech-to-text and return structured transcription content.")]
    [McpServerTool(
        Title = "Cartesia Speech-to-Text",
        Name = "cartesia_transcriptions_create",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Cartesia_Transcriptions_Create(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Audio file URL (.flac/.m4a/.mp3/.mp4/.mpeg/.mpga/.oga/.ogg/.wav/.webm). Supports SharePoint/OneDrive/HTTP.")]
        string fileUrl,
        [Description("Model ID. Default: ink-whisper.")]
        string model = "ink-whisper",
        [Description("Optional language code (ISO-639-1), e.g. en, nl, de.")]
        string? language = null,
        [Description("Optional audio encoding hint: pcm_s16le, pcm_s32le, pcm_f16le, pcm_f32le, pcm_mulaw, pcm_alaw.")]
        string? encoding = null,
        [Description("Optional audio sample rate in Hz.")]
        int? sampleRate = null,
        [Description("Include word timestamps in output. Default: true.")]
        bool includeWordTimestamps = true,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                    new CartesiaTranscriptionRequest
                    {
                        FileUrl = fileUrl,
                        Model = string.IsNullOrWhiteSpace(model) ? "ink-whisper" : model.Trim(),
                        Language = NormalizeOptional(language),
                        Encoding = NormalizeEncodingOrNull(encoding),
                        SampleRate = sampleRate,
                        IncludeWordTimestamps = includeWordTimestamps
                    },
                    cancellationToken);

                if (notAccepted != null)
                    return notAccepted;

                if (typed == null)
                    return "No input data provided".ToErrorCallToolResponse();

                ValidateRequest(typed);

                var downloader = serviceProvider.GetRequiredService<DownloadService>();
                var files = await downloader.DownloadContentAsync(serviceProvider, requestContext.Server, typed.FileUrl, cancellationToken);
                var media = files.FirstOrDefault() ?? throw new InvalidOperationException("Failed to download audio content from fileUrl.");

                using var form = new MultipartFormDataContent();

                var streamContent = new StreamContent(media.Contents.ToStream());
                streamContent.Headers.ContentType = new MediaTypeHeaderValue(
                    string.IsNullOrWhiteSpace(media.MimeType) ? "application/octet-stream" : media.MimeType);

                form.Add(streamContent, "file", string.IsNullOrWhiteSpace(media.Filename) ? "input.bin" : media.Filename);
                form.Add("model".NamedField(typed.Model));

                if (!string.IsNullOrWhiteSpace(typed.Language))
                    form.Add("language".NamedField(typed.Language));

                if (typed.IncludeWordTimestamps)
                    form.Add("timestamp_granularities[]".NamedField("word"));

                var path = BuildPathWithQuery(typed.Encoding, typed.SampleRate);

                using var client = serviceProvider.CreateCartesiaClient();
                using var response = await client.PostAsync(path, form, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException($"Cartesia STT failed ({(int)response.StatusCode}): {body}");

                var structured = BuildStructuredResponse(typed, body);
                var text = structured["text"]?.GetValue<string>() ?? string.Empty;

                return new CallToolResult
                {
                    StructuredContent = structured,
                    Content = [text.ToTextContentBlock()]
                };
            }));

    private static void ValidateRequest(CartesiaTranscriptionRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FileUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Model);

        if (request.SampleRate.HasValue && request.SampleRate <= 0)
            throw new ValidationException("sampleRate must be greater than 0 when provided.");
    }

    private static string BuildPathWithQuery(string? encoding, int? sampleRate)
    {
        var hasEncoding = !string.IsNullOrWhiteSpace(encoding);
        var hasSampleRate = sampleRate.HasValue && sampleRate.Value > 0;

        if (!hasEncoding && !hasSampleRate)
            return SttPath;

        var query = new List<string>();
        if (hasEncoding)
            query.Add($"encoding={Uri.EscapeDataString(encoding!)}");
        if (hasSampleRate)
            query.Add($"sample_rate={sampleRate!.Value.ToString(CultureInfo.InvariantCulture)}");

        return $"{SttPath}?{string.Join("&", query)}";
    }

    private static JsonObject BuildStructuredResponse(CartesiaTranscriptionRequest typed, string rawBody)
    {
        JsonNode parsed;
        try
        {
            parsed = JsonNode.Parse(rawBody) ?? new JsonObject();
        }
        catch
        {
            parsed = new JsonObject
            {
                ["text"] = rawBody
            };
        }

        var text = parsed["text"]?.GetValue<string>() ?? string.Empty;

        return new JsonObject
        {
            ["provider"] = "cartesia",
            ["type"] = "transcription",
            ["model"] = typed.Model,
            ["fileUrl"] = typed.FileUrl,
            ["language"] = parsed["language"]?.GetValue<string>() ?? typed.Language,
            ["duration"] = parsed["duration"],
            ["includeWordTimestamps"] = typed.IncludeWordTimestamps,
            ["text"] = text,
            ["words"] = parsed["words"],
            ["raw"] = parsed
        };
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeEncodingOrNull(string? encoding)
    {
        if (string.IsNullOrWhiteSpace(encoding))
            return null;

        var normalized = encoding.Trim().ToLowerInvariant();
        return normalized is "pcm_s16le" or "pcm_s32le" or "pcm_f16le" or "pcm_f32le" or "pcm_mulaw" or "pcm_alaw"
            ? normalized
            : null;
    }

    [Description("Please fill in the Cartesia transcription request.")]
    public sealed class CartesiaTranscriptionRequest
    {
        [JsonPropertyName("fileUrl")]
        [Required]
        public string FileUrl { get; set; } = default!;

        [JsonPropertyName("model")]
        [Required]
        public string Model { get; set; } = "ink-whisper";

        [JsonPropertyName("language")]
        public string? Language { get; set; }

        [JsonPropertyName("encoding")]
        public string? Encoding { get; set; }

        [JsonPropertyName("sampleRate")]
        public int? SampleRate { get; set; }

        [JsonPropertyName("includeWordTimestamps")]
        public bool IncludeWordTimestamps { get; set; } = true;
    }
}

