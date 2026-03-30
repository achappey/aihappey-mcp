using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.StabilityAI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Privatemode;

public static class PrivatemodeTranscriptions
{
    private const string TranscriptionsPath = "v1/audio/transcriptions";

    [Description("Transcribe audio from fileUrl using Privatemode speech-to-text and return structured transcription output.")]
    [McpServerTool(
        Title = "Privatemode Speech-to-Text",
        Name = "privatemode_transcriptions_create",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Privatemode_Transcriptions_Create(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Audio file URL (.flac/.mp3/.mp4/.mpeg/.mpga/.m4a/.ogg/.wav/.webm) to transcribe. Supports SharePoint/OneDrive/HTTP.")]
        string fileUrl,
        [Description("Model id. Default: whisper-large-v3.")]
        string model = "whisper-large-v3",
        [Description("Optional language hint in ISO-639-1 (e.g. en, nl).")]
        string? language = null,
        [Description("Optional prompt to guide transcription style.")]
        string? prompt = null,
        [Description("Response format: json or verbose_json. Default: json.")]
        string responseFormat = "json",
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(fileUrl);

                var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                    new PrivatemodeTranscriptionRequest
                    {
                        FileUrl = fileUrl,
                        Model = NormalizeModel(model),
                        Language = NormalizeOptional(language),
                        Prompt = NormalizeOptional(prompt),
                        ResponseFormat = NormalizeResponseFormat(responseFormat)
                    },
                    cancellationToken);

                if (notAccepted != null)
                    return notAccepted;

                if (typed == null)
                    return "No input data provided".ToErrorCallToolResponse();

                ValidateRequest(typed);

                var downloadService = serviceProvider.GetRequiredService<DownloadService>();
                var downloads = await downloadService.DownloadContentAsync(
                    serviceProvider,
                    requestContext.Server,
                    typed.FileUrl,
                    cancellationToken);

                var media = downloads.FirstOrDefault()
                    ?? throw new InvalidOperationException("Failed to download audio content from fileUrl.");

                using var form = new MultipartFormDataContent();

                var streamContent = new StreamContent(media.Contents.ToStream());
                streamContent.Headers.ContentType = new MediaTypeHeaderValue(
                    string.IsNullOrWhiteSpace(media.MimeType) ? "application/octet-stream" : media.MimeType);

                form.Add(streamContent, "file", string.IsNullOrWhiteSpace(media.Filename) ? "input.bin" : media.Filename);
                form.Add("model".NamedField(typed.Model));
                form.Add("response_format".NamedField(typed.ResponseFormat));

                if (!string.IsNullOrWhiteSpace(typed.Language))
                    form.Add("language".NamedField(typed.Language));

                if (!string.IsNullOrWhiteSpace(typed.Prompt))
                    form.Add("prompt".NamedField(typed.Prompt));

                using var client = serviceProvider.CreatePrivatemodeClient();
                using var response = await client.PostAsync(TranscriptionsPath, form, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException($"Privatemode transcription failed ({(int)response.StatusCode}): {body}");

                var structured = BuildStructuredResponse(typed, body);
                var text = structured["text"]?.GetValue<string>() ?? string.Empty;

                return new CallToolResult
                {
                    StructuredContent = (structured).ToJsonElement(),
                    Content = [text.ToTextContentBlock()]
                };
            }));

    private static void ValidateRequest(PrivatemodeTranscriptionRequest typed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typed.FileUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(typed.Model);

        if (!string.Equals(typed.ResponseFormat, "json", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(typed.ResponseFormat, "verbose_json", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("responseFormat must be json or verbose_json.");
        }

        if (string.Equals(typed.ResponseFormat, "verbose_json", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(typed.Language))
        {
            throw new ValidationException("language is required when responseFormat is verbose_json.");
        }
    }

    private static string NormalizeModel(string? model)
        => string.IsNullOrWhiteSpace(model) ? "whisper-large-v3" : model.Trim();

    private static string NormalizeResponseFormat(string? responseFormat)
    {
        var normalized = (responseFormat ?? "json").Trim().ToLowerInvariant();
        return normalized is "json" or "verbose_json" ? normalized : "json";
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static JsonObject BuildStructuredResponse(PrivatemodeTranscriptionRequest typed, string rawBody)
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
            ["provider"] = "privatemode",
            ["type"] = "transcription",
            ["model"] = typed.Model,
            ["fileUrl"] = typed.FileUrl,
            ["language"] = parsed["language"]?.GetValue<string>() ?? typed.Language,
            ["response_format"] = typed.ResponseFormat,
            ["text"] = text,
            ["usage"] = parsed["usage"],
            ["raw"] = parsed
        };
    }

    [Description("Please fill in the Privatemode transcription request.")]
    public sealed class PrivatemodeTranscriptionRequest
    {
        [JsonPropertyName("fileUrl")]
        [Required]
        public string FileUrl { get; set; } = default!;

        [JsonPropertyName("model")]
        [Required]
        public string Model { get; set; } = "whisper-large-v3";

        [JsonPropertyName("language")]
        public string? Language { get; set; }

        [JsonPropertyName("prompt")]
        public string? Prompt { get; set; }

        [JsonPropertyName("responseFormat")]
        [Required]
        public string ResponseFormat { get; set; } = "json";
    }
}

