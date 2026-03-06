using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Speechactors;

public static class SpeechactorsService
{
    private const string GeneratePath = "v1/generate";

    [Description("Generate speech audio from raw text using Speechactors and upload the result as a resource link block.")]
    [McpServerTool(
        Title = "Speechactors Text-to-Speech",
        Name = "speechactors_text_to_speech",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Speechactors_TextToSpeech(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Text to synthesize into speech.")] string text,
        [Description("Language code locale, e.g. en-US.")] string locale = "en-US",
        [Description("Voice id (vid), e.g. en-US-JennyNeural.")] string vid = "en-US-JennyNeural",
        [Description("Optional style if supported by the selected voice.")] string? style = null,
        [Description("Optional speaking rate in range -100..200.")] int? speakingRate = null,
        [Description("Optional pitch in range -50..50.")] int? pitch = null,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new SpeechactorsTextToSpeechRequest
                {
                    Text = text,
                    Locale = NormalizeRequired(locale, "locale"),
                    Vid = NormalizeRequired(vid, "vid"),
                    Style = NormalizeOptional(style),
                    SpeakingRate = NormalizeSpeakingRate(speakingRate),
                    Pitch = NormalizePitch(pitch),
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            return await GenerateAndUploadAsync(
                serviceProvider,
                requestContext,
                typed.Text,
                typed.Locale,
                typed.Vid,
                typed.Style,
                typed.SpeakingRate,
                typed.Pitch,
                typed.Filename,
                cancellationToken);
        });

    [Description("Generate speech audio from fileUrl by scraping text first, then synthesizing with Speechactors.")]
    [McpServerTool(
        Title = "Speechactors FileUrl-to-Speech",
        Name = "speechactors_fileurl_to_speech",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Speechactors_FileUrlToSpeech(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("File URL (SharePoint, OneDrive, HTTP) to extract text from.")] string fileUrl,
        [Description("Language code locale, e.g. en-US.")] string locale = "en-US",
        [Description("Voice id (vid), e.g. en-US-JennyNeural.")] string vid = "en-US-JennyNeural",
        [Description("Optional style if supported by the selected voice.")] string? style = null,
        [Description("Optional speaking rate in range -100..200.")] int? speakingRate = null,
        [Description("Optional pitch in range -50..50.")] int? pitch = null,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fileUrl);

            var text = await ResolveTextFromFileUrlAsync(serviceProvider, requestContext, fileUrl, cancellationToken);

            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new SpeechactorsFileUrlToSpeechRequest
                {
                    FileUrl = fileUrl,
                    Locale = NormalizeRequired(locale, "locale"),
                    Vid = NormalizeRequired(vid, "vid"),
                    Style = NormalizeOptional(style),
                    SpeakingRate = NormalizeSpeakingRate(speakingRate),
                    Pitch = NormalizePitch(pitch),
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            return await GenerateAndUploadAsync(
                serviceProvider,
                requestContext,
                text,
                typed.Locale,
                typed.Vid,
                typed.Style,
                typed.SpeakingRate,
                typed.Pitch,
                typed.Filename,
                cancellationToken);
        });

    private static async Task<string> ResolveTextFromFileUrlAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string fileUrl,
        CancellationToken cancellationToken)
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var files = await downloadService.ScrapeContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
        var sourceText = string.Join("\n\n", files.GetTextFiles().Select(f => f.Contents.ToString()));

        if (string.IsNullOrWhiteSpace(sourceText))
            throw new InvalidOperationException("No readable text content found in fileUrl.");

        return sourceText;
    }

    private static async Task<CallToolResult?> GenerateAndUploadAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string text,
        string locale,
        string vid,
        string? style,
        int? speakingRate,
        int? pitch,
        string filename,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ValidationException("text is required.");

        var payload = new Dictionary<string, object?>
        {
            ["locale"] = NormalizeRequired(locale, "locale"),
            ["vid"] = NormalizeRequired(vid, "vid"),
            ["text"] = text.Trim()
        };

        if (!string.IsNullOrWhiteSpace(style)) payload["style"] = style;
        if (speakingRate.HasValue) payload["speakingRate"] = speakingRate.Value;
        if (pitch.HasValue) payload["pitch"] = pitch.Value;

        using var client = serviceProvider.CreateSpeechactorsClient("audio/mpeg");
        using var req = new HttpRequestMessage(HttpMethod.Post, GeneratePath)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, MimeTypes.Json)
        };

        using var resp = await client.SendAsync(req, cancellationToken);
        var bytes = await resp.Content.ReadAsByteArrayAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
        {
            var error = Encoding.UTF8.GetString(bytes);
            throw new InvalidOperationException($"Speechactors generate call failed ({(int)resp.StatusCode}): {error}");
        }

        if (bytes.Length == 0)
            throw new InvalidOperationException("Speechactors generate call returned empty audio data.");

        var uploadName = filename.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)
            ? filename
            : $"{filename}.mp3";

        var uploaded = await requestContext.Server.Upload(
            serviceProvider,
            uploadName,
            BinaryData.FromBytes(bytes),
            cancellationToken);

        return uploaded?.ToResourceLinkCallToolResponse();
    }

    private static string NormalizeRequired(string? value, string field)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ValidationException($"{field} is required.");

        return normalized;
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int? NormalizeSpeakingRate(int? value)
    {
        if (!value.HasValue)
            return null;

        return value.Value is < -100 or > 200 ? null : value.Value;
    }

    private static int? NormalizePitch(int? value)
    {
        if (!value.HasValue)
            return null;

        return value.Value is < -50 or > 50 ? null : value.Value;
    }

    [Description("Please fill in the Speechactors text-to-speech request.")]
    public sealed class SpeechactorsTextToSpeechRequest
    {
        [JsonPropertyName("text")]
        [Required]
        [Description("Text to synthesize into speech.")]
        public string Text { get; set; } = default!;

        [JsonPropertyName("locale")]
        [Required]
        [Description("Language code locale, e.g. en-US.")]
        public string Locale { get; set; } = "en-US";

        [JsonPropertyName("vid")]
        [Required]
        [Description("Voice id (vid), e.g. en-US-JennyNeural.")]
        public string Vid { get; set; } = "en-US-JennyNeural";

        [JsonPropertyName("style")]
        [Description("Optional style when supported by selected voice.")]
        public string? Style { get; set; }

        [JsonPropertyName("speakingRate")]
        [Range(-100, 200)]
        [Description("Optional speaking rate in range -100..200.")]
        public int? SpeakingRate { get; set; }

        [JsonPropertyName("pitch")]
        [Range(-50, 50)]
        [Description("Optional pitch in range -50..50.")]
        public int? Pitch { get; set; }

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }

    [Description("Please fill in the Speechactors fileUrl-to-speech request.")]
    public sealed class SpeechactorsFileUrlToSpeechRequest
    {
        [JsonPropertyName("fileUrl")]
        [Required]
        [Description("File URL (SharePoint, OneDrive, HTTP) to extract text from.")]
        public string FileUrl { get; set; } = default!;

        [JsonPropertyName("locale")]
        [Required]
        [Description("Language code locale, e.g. en-US.")]
        public string Locale { get; set; } = "en-US";

        [JsonPropertyName("vid")]
        [Required]
        [Description("Voice id (vid), e.g. en-US-JennyNeural.")]
        public string Vid { get; set; } = "en-US-JennyNeural";

        [JsonPropertyName("style")]
        [Description("Optional style when supported by selected voice.")]
        public string? Style { get; set; }

        [JsonPropertyName("speakingRate")]
        [Range(-100, 200)]
        [Description("Optional speaking rate in range -100..200.")]
        public int? SpeakingRate { get; set; }

        [JsonPropertyName("pitch")]
        [Range(-50, 50)]
        [Description("Optional pitch in range -50..50.")]
        public int? Pitch { get; set; }

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }
}

