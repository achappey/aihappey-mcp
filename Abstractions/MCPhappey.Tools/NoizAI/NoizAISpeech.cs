using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.NoizAI;

public static class NoizAISpeech
{
    private const string SpeechUrl = "https://noiz.ai/v1/text-to-speech";

    [Description("Generate speech audio from raw text using Noiz AI and upload the result as a resource link block.")]
    [McpServerTool(
        Title = "Noiz AI Text-to-Speech",
        Name = "noiz_ai_speech_text_to_speech",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Noiz_AI_Speech_TextToSpeech(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Text to synthesize into speech (max 5000 chars).")]
        string text,
        [Description("Voice ID to use for synthesis.")]
        string voice_id,
        [Description("Output format: wav or mp3. Default: wav.")]
        string output_format = "wav",
        [Description("Speech speed multiplier. Default: 1.0")]
        double speed = 1.0,
        [Description("Target language code, for example en, zh, or zh+en.")]
        string? target_lang = null,
        [Description("Whether to trim leading and trailing silence. Default: false.")]
        bool trim_silence = false,
        [Description("Output filename without extension.")]
        string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new NoizAITextToSpeechRequest
                {
                    Text = text,
                    VoiceId = voice_id,
                    OutputFormat = NormalizeOutputFormat(output_format),
                    Speed = ClampSpeed(speed),
                    TargetLang = string.IsNullOrWhiteSpace(target_lang) ? null : target_lang.Trim(),
                    TrimSilence = trim_silence,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            return await GenerateAndUploadSpeechAsync(
                serviceProvider,
                requestContext,
                typed.Text,
                typed.VoiceId,
                typed.OutputFormat,
                typed.Speed,
                typed.TargetLang,
                typed.TrimSilence,
                typed.Filename,
                cancellationToken);
        });

    [Description("Generate speech audio from a fileUrl by scraping text first, then synthesizing with Noiz AI.")]
    [McpServerTool(
        Title = "Noiz AI File-to-Speech",
        Name = "noiz_ai_speech_file_to_speech",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Noiz_AI_Speech_FileToSpeech(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("File URL (SharePoint, OneDrive, HTTP) to extract text from.")]
        string fileUrl,
        [Description("Voice ID to use for synthesis.")]
        string voice_id,
        [Description("Output format: wav or mp3. Default: wav.")]
        string output_format = "wav",
        [Description("Speech speed multiplier. Default: 1.0")]
        double speed = 1.0,
        [Description("Target language code, for example en, zh, or zh+en.")]
        string? target_lang = null,
        [Description("Whether to trim leading and trailing silence. Default: false.")]
        bool trim_silence = false,
        [Description("Output filename without extension.")]
        string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fileUrl);

            var downloadService = serviceProvider.GetRequiredService<DownloadService>();
            var files = await downloadService.ScrapeContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
            var sourceText = string.Join("\n\n", files.GetTextFiles().Select(f => f.Contents.ToString()));

            if (string.IsNullOrWhiteSpace(sourceText))
                throw new InvalidOperationException("No readable text content found in fileUrl.");

            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new NoizAIFileToSpeechRequest
                {
                    FileUrl = fileUrl,
                    VoiceId = voice_id,
                    OutputFormat = NormalizeOutputFormat(output_format),
                    Speed = ClampSpeed(speed),
                    TargetLang = string.IsNullOrWhiteSpace(target_lang) ? null : target_lang.Trim(),
                    TrimSilence = trim_silence,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            return await GenerateAndUploadSpeechAsync(
                serviceProvider,
                requestContext,
                sourceText,
                typed.VoiceId,
                typed.OutputFormat,
                typed.Speed,
                typed.TargetLang,
                typed.TrimSilence,
                typed.Filename,
                cancellationToken);
        });

    private static async Task<CallToolResult?> GenerateAndUploadSpeechAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string text,
        string voiceId,
        string outputFormat,
        double speed,
        string? targetLang,
        bool trimSilence,
        string filename,
        CancellationToken cancellationToken)
    {
        ValidateInput(text, voiceId);

        var settings = serviceProvider.GetRequiredService<NoizAISettings>();
        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var resolvedFormat = NormalizeOutputFormat(outputFormat);

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(text, Encoding.UTF8), "text");
        content.Add(new StringContent(voiceId, Encoding.UTF8), "voice_id");
        content.Add(new StringContent(resolvedFormat, Encoding.UTF8), "output_format");
        content.Add(new StringContent(ClampSpeed(speed).ToString(CultureInfo.InvariantCulture), Encoding.UTF8), "speed");
        content.Add(new StringContent(trimSilence ? "true" : "false", Encoding.UTF8), "trim_silence");

        if (!string.IsNullOrWhiteSpace(targetLang))
            content.Add(new StringContent(targetLang, Encoding.UTF8), "target_lang");

        using var client = clientFactory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, SpeechUrl);
        req.Headers.TryAddWithoutValidation("Authorization", settings.ApiKey);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(GetAcceptMimeType(resolvedFormat)));
        req.Content = content;

        using var resp = await client.SendAsync(req, cancellationToken);
        var bytes = await resp.Content.ReadAsByteArrayAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
        {
            var body = Encoding.UTF8.GetString(bytes);
            throw new InvalidOperationException($"Noiz AI speech call failed ({(int)resp.StatusCode}): {body}");
        }

        var ext = ResolveExtension(resp.Content.Headers.ContentType?.MediaType, resolvedFormat);
        var uploadName = filename.EndsWith($".{ext}", StringComparison.OrdinalIgnoreCase)
            ? filename
            : $"{filename}.{ext}";

        var uploaded = await requestContext.Server.Upload(
            serviceProvider,
            uploadName,
            BinaryData.FromBytes(bytes),
            cancellationToken);

        return uploaded?.ToResourceLinkCallToolResponse();
    }

    private static void ValidateInput(string text, string voiceId)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ValidationException("text is required.");

        if (text.Length > 5000)
            throw new ValidationException("text exceeds maximum length of 5000 characters.");

        if (string.IsNullOrWhiteSpace(voiceId))
            throw new ValidationException("voice_id is required.");
    }

    private static string NormalizeOutputFormat(string? outputFormat)
    {
        var value = (outputFormat ?? "wav").Trim().ToLowerInvariant();
        return value is "wav" or "mp3" ? value : "wav";
    }

    private static double ClampSpeed(double speed)
        => Math.Clamp(speed, 0.25, 4.0);

    private static string GetAcceptMimeType(string outputFormat)
        => outputFormat switch
        {
            "mp3" => "audio/mpeg",
            _ => "audio/wav"
        };

    private static string ResolveExtension(string? mimeType, string fallbackFormat)
    {
        var mediaType = mimeType?.Trim().ToLowerInvariant();
        return mediaType switch
        {
            "audio/mpeg" => "mp3",
            "audio/mp3" => "mp3",
            "audio/wav" => "wav",
            "audio/x-wav" => "wav",
            "application/octet-stream" => NormalizeOutputFormat(fallbackFormat),
            _ => NormalizeOutputFormat(fallbackFormat)
        };
    }

    [Description("Please fill in the Noiz AI text-to-speech request.")]
    public sealed class NoizAITextToSpeechRequest
    {
        [JsonPropertyName("text")]
        [Required]
        [Description("Plain text to synthesize (max 5000 chars).")]
        public string Text { get; set; } = default!;

        [JsonPropertyName("voice_id")]
        [Required]
        [Description("Voice ID to use for synthesis.")]
        public string VoiceId { get; set; } = default!;

        [JsonPropertyName("output_format")]
        [Required]
        [Description("Output format: wav or mp3.")]
        public string OutputFormat { get; set; } = "wav";

        [JsonPropertyName("speed")]
        [Description("Speech speed between 0.25 and 4.0.")]
        public double Speed { get; set; } = 1.0;

        [JsonPropertyName("target_lang")]
        [Description("Optional target language code.")]
        public string? TargetLang { get; set; }

        [JsonPropertyName("trim_silence")]
        [Description("Whether to trim leading and trailing silence.")]
        public bool TrimSilence { get; set; }

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }

    [Description("Please fill in the Noiz AI file-to-speech request.")]
    public sealed class NoizAIFileToSpeechRequest
    {
        [JsonPropertyName("fileUrl")]
        [Required]
        [Description("Source file URL to scrape/extract text from.")]
        public string FileUrl { get; set; } = default!;

        [JsonPropertyName("voice_id")]
        [Required]
        [Description("Voice ID to use for synthesis.")]
        public string VoiceId { get; set; } = default!;

        [JsonPropertyName("output_format")]
        [Required]
        [Description("Output format: wav or mp3.")]
        public string OutputFormat { get; set; } = "wav";

        [JsonPropertyName("speed")]
        [Description("Speech speed between 0.25 and 4.0.")]
        public double Speed { get; set; } = 1.0;

        [JsonPropertyName("target_lang")]
        [Description("Optional target language code.")]
        public string? TargetLang { get; set; }

        [JsonPropertyName("trim_silence")]
        [Description("Whether to trim leading and trailing silence.")]
        public bool TrimSilence { get; set; }

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }
}
