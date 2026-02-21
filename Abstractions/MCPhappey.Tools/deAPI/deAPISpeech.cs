using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
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

namespace MCPhappey.Tools.deAPI;

public static class deAPISpeech
{
    private const string BaseUrl = "https://api.deapi.ai";
    private const string Txt2AudioPath = "/api/v1/client/txt2audio";
    private const string Txt2AudioPricePath = "/api/v1/client/txt2audio/price-calculation";
    private const int DefaultPollIntervalSeconds = 2;
    private const int DefaultMaxWaitSeconds = 300;

    [Description("Generate speech audio from raw text using deAPI, then upload and return a resource link block.")]
    [McpServerTool(
        Title = "deAPI Text-to-Speech",
        Name = "deapi_speech_text_to_speech",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> deAPI_Speech_TextToSpeech(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Text to synthesize into speech.")] string text,
        [Description("deAPI TTS model slug. Default: Kokoro.")] string model = "Kokoro",
        [Description("Voice identifier. Default: af_sky.")] string voice = "af_sky",
        [Description("Language code. Default: en-us.")] string lang = "en-us",
        [Description("Speech speed. Default: 1.0.")] double speed = 1.0,
        [Description("Audio output format. Default: flac.")] string format = "flac",
        [Description("Audio sample rate. Default: 24000.")] int sample_rate = 24000,
        [Description("Polling interval in seconds.")][Range(1, 60)] int pollIntervalSeconds = DefaultPollIntervalSeconds,
        [Description("Maximum total wait time in seconds.")][Range(30, 3600)] int maxWaitSeconds = DefaultMaxWaitSeconds,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(text);

            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new deAPISpeechRequest
                {
                    Text = text,
                    Model = model,
                    Voice = voice,
                    Lang = lang,
                    Speed = speed,
                    Format = NormalizeFormat(format),
                    SampleRate = sample_rate,
                    PollIntervalSeconds = pollIntervalSeconds,
                    MaxWaitSeconds = maxWaitSeconds,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName("flac")
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            return await ExecuteTtsAndUploadAsync(serviceProvider, requestContext, typed, cancellationToken);
        });

    [Description("Generate speech audio from fileUrl by scraping text first, then running deAPI text-to-speech. Upload and return a resource link block.")]
    [McpServerTool(
        Title = "deAPI File-to-Speech",
        Name = "deapi_speech_file_to_speech",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> deAPI_Speech_FileToSpeech(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Source file URL (SharePoint, OneDrive, HTTP) to scrape text from.")] string fileUrl,
        [Description("deAPI TTS model slug. Default: Kokoro.")] string model = "Kokoro",
        [Description("Voice identifier. Default: af_sky.")] string voice = "af_sky",
        [Description("Language code. Default: en-us.")] string lang = "en-us",
        [Description("Speech speed. Default: 1.0.")] double speed = 1.0,
        [Description("Audio output format. Default: flac.")] string format = "flac",
        [Description("Audio sample rate. Default: 24000.")] int sample_rate = 24000,
        [Description("Polling interval in seconds.")][Range(1, 60)] int pollIntervalSeconds = DefaultPollIntervalSeconds,
        [Description("Maximum total wait time in seconds.")][Range(30, 3600)] int maxWaitSeconds = DefaultMaxWaitSeconds,
        [Description("Output filename without extension.")] string? filename = null,
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
                new deAPIFileToSpeechRequest
                {
                    FileUrl = fileUrl,
                    Model = model,
                    Voice = voice,
                    Lang = lang,
                    Speed = speed,
                    Format = NormalizeFormat(format),
                    SampleRate = sample_rate,
                    PollIntervalSeconds = pollIntervalSeconds,
                    MaxWaitSeconds = maxWaitSeconds,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName("flac")
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            var speechRequest = new deAPISpeechRequest
            {
                Text = sourceText,
                Model = typed.Model,
                Voice = typed.Voice,
                Lang = typed.Lang,
                Speed = typed.Speed,
                Format = typed.Format,
                SampleRate = typed.SampleRate,
                PollIntervalSeconds = typed.PollIntervalSeconds,
                MaxWaitSeconds = typed.MaxWaitSeconds,
                Filename = typed.Filename
            };

            return await ExecuteTtsAndUploadAsync(serviceProvider, requestContext, speechRequest, cancellationToken);
        });

    [Description("Calculate deAPI text-to-speech price using either text length from text or explicit count_text.")]
    [McpServerTool(
        Title = "deAPI Text-to-Speech Price Calculation",
        Name = "deapi_speech_calculate_price",
        ReadOnly = true,
        OpenWorld = true)]
    public static async Task<CallToolResult?> deAPI_Speech_CalculatePrice(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional text to estimate price from.")] string? text = null,
        [Description("Optional explicit character count. Use when text is omitted.")] int? count_text = null,
        [Description("deAPI TTS model slug. Default: Kokoro.")] string model = "Kokoro",
        [Description("Voice identifier. Default: af_sky.")] string voice = "af_sky",
        [Description("Language code. Default: en-us.")] string lang = "en-us",
        [Description("Speech speed. Default: 1.0.")] double speed = 1.0,
        [Description("Audio output format. Default: flac.")] string format = "flac",
        [Description("Audio sample rate. Default: 24000.")] int sample_rate = 24000,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            if (string.IsNullOrWhiteSpace(text) && (!count_text.HasValue || count_text.Value <= 0))
                throw new ValidationException("Provide either text or a positive count_text value.");

            var settings = serviceProvider.GetRequiredService<deAPISettings>();
            var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

            var payload = new
            {
                text = string.IsNullOrWhiteSpace(text) ? null : text,
                count_text,
                model,
                voice,
                lang,
                speed,
                format = NormalizeFormat(format),
                sample_rate
            };

            using var client = clientFactory.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}{Txt2AudioPricePath}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, MimeTypes.Json);

            using var resp = await client.SendAsync(req, cancellationToken);
            var json = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"{resp.StatusCode}: {json}");

            return json.ToJsonCallToolResponse($"{BaseUrl}{Txt2AudioPricePath}");
        });

    private static async Task<CallToolResult?> ExecuteTtsAndUploadAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        deAPISpeechRequest typed,
        CancellationToken cancellationToken)
    {
        ValidateSpeechRequest(typed);

        var settings = serviceProvider.GetRequiredService<deAPISettings>();
        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();

        var payload = new
        {
            text = typed.Text,
            model = typed.Model,
            voice = typed.Voice,
            lang = typed.Lang,
            speed = typed.Speed,
            format = NormalizeFormat(typed.Format),
            sample_rate = typed.SampleRate
        };

        using var client = clientFactory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}{Txt2AudioPath}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, MimeTypes.Json);

        using var resp = await client.SendAsync(req, cancellationToken);
        var submitJson = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"{resp.StatusCode}: {submitJson}");

        using var submitDoc = JsonDocument.Parse(submitJson);
        var requestId = submitDoc.RootElement
            .GetProperty("data")
            .GetProperty("request_id")
            .GetString();

        if (string.IsNullOrWhiteSpace(requestId))
            throw new Exception("deAPI did not return request_id.");

        var resultUrl = await PollForResultUrlAsync(client, settings.ApiKey, requestId, typed.PollIntervalSeconds, typed.MaxWaitSeconds, cancellationToken);
        if (string.IsNullOrWhiteSpace(resultUrl))
            throw new Exception($"deAPI job {requestId} completed without result_url.");

        var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, resultUrl, cancellationToken);
        var file = files.FirstOrDefault() ?? throw new Exception("Failed to download deAPI generated audio file.");

        var ext = GetAudioExtension(file.Filename, file.MimeType, typed.Format);
        var uploaded = await requestContext.Server.Upload(
            serviceProvider,
            $"{typed.Filename}.{ext}",
            BinaryData.FromBytes(file.Contents.ToArray()),
            cancellationToken);

        if (uploaded == null)
            throw new Exception("Audio upload failed.");

        return uploaded.ToResourceLinkCallToolResponse();
    }

    private static async Task<string?> PollForResultUrlAsync(
        HttpClient client,
        string apiKey,
        string requestId,
        int pollIntervalSeconds,
        int maxWaitSeconds,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(maxWaitSeconds));

        while (!timeoutCts.IsCancellationRequested)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/v1/client/request-status/{requestId}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));

            using var resp = await client.SendAsync(req, timeoutCts.Token);
            var statusJson = await resp.Content.ReadAsStringAsync(timeoutCts.Token);

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"Polling failed ({resp.StatusCode}): {statusJson}");

            using var doc = JsonDocument.Parse(statusJson);
            var data = doc.RootElement.GetProperty("data");
            var status = data.TryGetProperty("status", out var statusEl) ? statusEl.GetString()?.Trim().ToLowerInvariant() : null;

            if (status == "done")
                return data.TryGetProperty("result_url", out var resultUrlEl) ? resultUrlEl.GetString() : null;

            if (status == "error")
            {
                var error = data.TryGetProperty("error", out var errorEl) ? errorEl.ToString() : "unknown deAPI error";
                throw new Exception($"deAPI request {requestId} failed: {error}");
            }

            await Task.Delay(TimeSpan.FromSeconds(pollIntervalSeconds), timeoutCts.Token);
        }

        throw new TimeoutException($"deAPI request {requestId} did not complete within {maxWaitSeconds} seconds.");
    }

    private static void ValidateSpeechRequest(deAPISpeechRequest input)
    {
        if (string.IsNullOrWhiteSpace(input.Text))
            throw new ValidationException("text is required.");

        if (string.IsNullOrWhiteSpace(input.Model))
            throw new ValidationException("model is required.");

        if (string.IsNullOrWhiteSpace(input.Voice))
            throw new ValidationException("voice is required.");

        if (string.IsNullOrWhiteSpace(input.Lang))
            throw new ValidationException("lang is required.");

        if (input.SampleRate <= 0)
            throw new ValidationException("sample_rate must be greater than 0.");

        if (input.PollIntervalSeconds < 1 || input.PollIntervalSeconds > 60)
            throw new ValidationException("pollIntervalSeconds must be between 1 and 60.");

        if (input.MaxWaitSeconds < 30 || input.MaxWaitSeconds > 3600)
            throw new ValidationException("maxWaitSeconds must be between 30 and 3600.");
    }

    private static string NormalizeFormat(string? format)
    {
        var value = (format ?? "flac").Trim().ToLowerInvariant();
        return value is "flac" or "mp3" or "wav" or "ogg" or "aac" or "pcm" ? value : "flac";
    }

    private static string GetAudioExtension(string? filename, string? mimeType, string fallbackFormat)
    {
        var ext = Path.GetExtension(filename ?? string.Empty)?.TrimStart('.');
        if (!string.IsNullOrWhiteSpace(ext))
            return ext;

        return mimeType?.ToLowerInvariant() switch
        {
            "audio/flac" => "flac",
            "audio/mpeg" => "mp3",
            "audio/wav" => "wav",
            "audio/ogg" => "ogg",
            "audio/aac" => "aac",
            _ => NormalizeFormat(fallbackFormat)
        };
    }

    [Description("Please fill in the deAPI text-to-speech request details.")]
    public sealed class deAPISpeechRequest
    {
        [JsonPropertyName("text")]
        [Required]
        [Description("Text to convert to speech.")]
        public string Text { get; set; } = default!;

        [JsonPropertyName("model")]
        [Required]
        [Description("deAPI TTS model slug.")]
        public string Model { get; set; } = "Kokoro";

        [JsonPropertyName("voice")]
        [Required]
        [Description("Voice identifier.")]
        public string Voice { get; set; } = "af_sky";

        [JsonPropertyName("lang")]
        [Required]
        [Description("Language code.")]
        public string Lang { get; set; } = "en-us";

        [JsonPropertyName("speed")]
        [Required]
        [Description("Speech speed.")]
        public double Speed { get; set; } = 1.0;

        [JsonPropertyName("format")]
        [Required]
        [Description("Output format. Default: flac.")]
        public string Format { get; set; } = "flac";

        [JsonPropertyName("sample_rate")]
        [Required]
        [Description("Sample rate in Hz.")]
        public int SampleRate { get; set; } = 24000;

        [JsonPropertyName("pollIntervalSeconds")]
        [Required]
        [Description("Polling interval in seconds.")]
        public int PollIntervalSeconds { get; set; } = DefaultPollIntervalSeconds;

        [JsonPropertyName("maxWaitSeconds")]
        [Required]
        [Description("Maximum total wait in seconds.")]
        public int MaxWaitSeconds { get; set; } = DefaultMaxWaitSeconds;

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }

    [Description("Please fill in the deAPI file-to-speech request details.")]
    public sealed class deAPIFileToSpeechRequest
    {
        [JsonPropertyName("fileUrl")]
        [Required]
        [Description("Source file URL to scrape/extract text from.")]
        public string FileUrl { get; set; } = default!;

        [JsonPropertyName("model")]
        [Required]
        [Description("deAPI TTS model slug.")]
        public string Model { get; set; } = "Kokoro";

        [JsonPropertyName("voice")]
        [Required]
        [Description("Voice identifier.")]
        public string Voice { get; set; } = "af_sky";

        [JsonPropertyName("lang")]
        [Required]
        [Description("Language code.")]
        public string Lang { get; set; } = "en-us";

        [JsonPropertyName("speed")]
        [Required]
        [Description("Speech speed.")]
        public double Speed { get; set; } = 1.0;

        [JsonPropertyName("format")]
        [Required]
        [Description("Output format. Default: flac.")]
        public string Format { get; set; } = "flac";

        [JsonPropertyName("sample_rate")]
        [Required]
        [Description("Sample rate in Hz.")]
        public int SampleRate { get; set; } = 24000;

        [JsonPropertyName("pollIntervalSeconds")]
        [Required]
        [Description("Polling interval in seconds.")]
        public int PollIntervalSeconds { get; set; } = DefaultPollIntervalSeconds;

        [JsonPropertyName("maxWaitSeconds")]
        [Required]
        [Description("Maximum total wait in seconds.")]
        public int MaxWaitSeconds { get; set; } = DefaultMaxWaitSeconds;

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }
}

