using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Gradium;

public static class GradiumSpeech
{
    private const string SpeechUrl = "https://eu.api.gradium.ai/api/post/speech/tts";
    private const string VoicesUrl = "https://eu.api.gradium.ai/voices/";

    [Description("Generate speech audio from raw text using Gradium, upload the result, and return only a resource link block.")]
    [McpServerTool(
        Title = "Gradium Text-to-Speech",
        Name = "gradium_speech_text_to_speech",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Gradium_Speech_TextToSpeech(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Text to synthesize into speech.")] string text,
        [Description("Gradium voice ID to use for synthesis.")] string voice_id,
        [Description("Output format: wav, pcm, opus, ulaw_8000, alaw_8000, pcm_8000, pcm_16000, pcm_24000. Default: wav.")] string output_format = "wav",
        [Description("Optional additional JSON config string.")] string? json_config = null,
        [Description("Optional TTS model name. Default: default.")] string model_name = "default",
        [Description("Return raw audio bytes. Default: true.")] bool only_audio = true,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new GradiumTextToSpeechRequest
                {
                    Text = text,
                    VoiceId = voice_id,
                    OutputFormat = NormalizeOutputFormat(output_format),
                    JsonConfig = string.IsNullOrWhiteSpace(json_config) ? null : json_config.Trim(),
                    ModelName = string.IsNullOrWhiteSpace(model_name) ? "default" : model_name.Trim(),
                    OnlyAudio = only_audio,
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
                typed.JsonConfig,
                typed.ModelName,
                typed.OnlyAudio,
                typed.Filename,
                cancellationToken);
        });

    [Description("Generate speech audio from a fileUrl by scraping text first with Gradium, upload the result, and return only a resource link block.")]
    [McpServerTool(
        Title = "Gradium File-to-Speech",
        Name = "gradium_speech_file_to_speech",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Gradium_Speech_FileToSpeech(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("File URL (SharePoint, OneDrive, HTTP) to extract text from.")] string fileUrl,
        [Description("Gradium voice ID to use for synthesis.")] string voice_id,
        [Description("Output format: wav, pcm, opus, ulaw_8000, alaw_8000, pcm_8000, pcm_16000, pcm_24000. Default: wav.")] string output_format = "wav",
        [Description("Optional additional JSON config string.")] string? json_config = null,
        [Description("Optional TTS model name. Default: default.")] string model_name = "default",
        [Description("Return raw audio bytes. Default: true.")] bool only_audio = true,
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
                new GradiumFileToSpeechRequest
                {
                    FileUrl = fileUrl,
                    VoiceId = voice_id,
                    OutputFormat = NormalizeOutputFormat(output_format),
                    JsonConfig = string.IsNullOrWhiteSpace(json_config) ? null : json_config.Trim(),
                    ModelName = string.IsNullOrWhiteSpace(model_name) ? "default" : model_name.Trim(),
                    OnlyAudio = only_audio,
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
                typed.JsonConfig,
                typed.ModelName,
                typed.OnlyAudio,
                typed.Filename,
                cancellationToken);
        });

    [Description("Create a new custom Gradium voice with audio_file from fileUrl.")]
    [McpServerTool(
        Title = "Gradium Create Voice",
        Name = "gradium_voices_create",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Gradium_Voices_Create(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Audio file URL to upload for voice cloning.")] string fileUrl,
        [Description("Voice name.")] string name,
        [Description("Optional input format.")] string? input_format = null,
        [Description("Optional description.")] string? description = null,
        [Description("Optional language.")] string? language = null,
        [Description("Audio start offset in seconds. Default: 0.")] double start_s = 0,
        [Description("Timeout in seconds. Default: 10.")] double timeout_s = 10,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new GradiumCreateVoiceRequest
                {
                    FileUrl = fileUrl,
                    Name = name,
                    InputFormat = string.IsNullOrWhiteSpace(input_format) ? null : input_format.Trim(),
                    Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                    Language = string.IsNullOrWhiteSpace(language) ? null : language.Trim(),
                    StartS = start_s,
                    TimeoutS = timeout_s
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            var downloadService = serviceProvider.GetRequiredService<DownloadService>();
            var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, typed.FileUrl, cancellationToken);
            var sourceFile = files.FirstOrDefault() ?? throw new InvalidOperationException("No downloadable voice audio file found at fileUrl.");

            ValidateCreateVoice(typed, sourceFile.Filename);

            var settings = serviceProvider.GetRequiredService<GradiumSettings>();
            var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

            using var content = new MultipartFormDataContent();
            var audioContent = new ByteArrayContent(sourceFile.Contents.ToArray());
            audioContent.Headers.ContentType = MediaTypeHeaderValue.Parse(sourceFile.MimeType ?? "application/octet-stream");
            content.Add(audioContent, "audio_file", sourceFile.Filename ?? "voice-sample.bin");
            content.Add(new StringContent(typed.Name, Encoding.UTF8), "name");
            content.Add(new StringContent(typed.StartS.ToString(System.Globalization.CultureInfo.InvariantCulture), Encoding.UTF8), "start_s");
            content.Add(new StringContent(typed.TimeoutS.ToString(System.Globalization.CultureInfo.InvariantCulture), Encoding.UTF8), "timeout_s");

            if (!string.IsNullOrWhiteSpace(typed.InputFormat))
                content.Add(new StringContent(typed.InputFormat, Encoding.UTF8), "input_format");
            if (!string.IsNullOrWhiteSpace(typed.Description))
                content.Add(new StringContent(typed.Description, Encoding.UTF8), "description");
            if (!string.IsNullOrWhiteSpace(typed.Language))
                content.Add(new StringContent(typed.Language, Encoding.UTF8), "language");

            using var client = clientFactory.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Post, VoicesUrl);
            req.Headers.Add("x-api-key", settings.ApiKey);
            req.Content = content;

            using var resp = await client.SendAsync(req, cancellationToken);
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"Gradium create voice failed ({(int)resp.StatusCode}): {body}");

            return body.ToJsonContent(VoicesUrl).ToCallToolResult();
        });

    [Description("List Gradium voices for the authenticated organization.")]
    [McpServerTool(
        Title = "Gradium List Voices",
        Name = "gradium_voices_list",
        Destructive = false,
        OpenWorld = true,
        ReadOnly = true)]
    public static async Task<CallToolResult?> Gradium_Voices_List(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Skip offset. Default: 0.")] int skip = 0,
        [Description("Maximum number of voices to return. Default: 100.")] int limit = 100,
        [Description("Include catalog voices. Default: false.")] bool include_catalog = false,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var settings = serviceProvider.GetRequiredService<GradiumSettings>();
            var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var url = $"{VoicesUrl}?skip={Math.Max(0, skip)}&limit={Math.Max(0, limit)}&include_catalog={(include_catalog ? "true" : "false")}";

            using var client = clientFactory.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("x-api-key", settings.ApiKey);

            using var resp = await client.SendAsync(req, cancellationToken);
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"Gradium list voices failed ({(int)resp.StatusCode}): {body}");

            return body.ToJsonContent(url).ToCallToolResult();
        });

    [Description("Get a Gradium voice by UID.")]
    [McpServerTool(
        Title = "Gradium Get Voice",
        Name = "gradium_voices_get",
        Destructive = false,
        OpenWorld = true,
        ReadOnly = true)]
    public static async Task<CallToolResult?> Gradium_Voices_Get(
        [Description("Voice UID.")] string voice_uid,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(voice_uid);

            var settings = serviceProvider.GetRequiredService<GradiumSettings>();
            var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var url = $"{VoicesUrl}{voice_uid.Trim()}";

            using var client = clientFactory.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("x-api-key", settings.ApiKey);

            using var resp = await client.SendAsync(req, cancellationToken);
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"Gradium get voice failed ({(int)resp.StatusCode}): {body}");

            return body.ToJsonContent(url).ToCallToolResult();
        });

    [Description("Update a Gradium voice by UID.")]
    [McpServerTool(
        Title = "Gradium Update Voice",
        Name = "gradium_voices_update",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Gradium_Voices_Update(
        [Description("Voice UID.")] string voice_uid,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional updated name.")] string? name = null,
        [Description("Optional updated description.")] string? description = null,
        [Description("Optional updated language.")] string? language = null,
        [Description("Optional updated start offset in seconds.")] double? start_s = null,
        [Description("Optional numeric rank.")] double? rank = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(voice_uid);

            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new GradiumUpdateVoiceRequest
                {
                    VoiceUid = voice_uid,
                    Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
                    Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                    Language = string.IsNullOrWhiteSpace(language) ? null : language.Trim(),
                    StartS = start_s,
                    Rank = rank
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            var settings = serviceProvider.GetRequiredService<GradiumSettings>();
            var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var url = $"{VoicesUrl}{typed.VoiceUid.Trim()}";

            var payload = new Dictionary<string, object?>();
            if (typed.Name != null) payload["name"] = typed.Name;
            if (typed.Description != null) payload["description"] = typed.Description;
            if (typed.Language != null) payload["language"] = typed.Language;
            if (typed.StartS.HasValue) payload["start_s"] = typed.StartS.Value;
            if (typed.Rank.HasValue) payload["rank"] = typed.Rank.Value;

            if (payload.Count == 0)
                throw new ValidationException("At least one field must be provided for update.");

            using var client = clientFactory.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Put, url);
            req.Headers.Add("x-api-key", settings.ApiKey);
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var resp = await client.SendAsync(req, cancellationToken);
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"Gradium update voice failed ({(int)resp.StatusCode}): {body}");

            return body.ToJsonContent(url).ToCallToolResult();
        });

    [Description("Delete a Gradium voice by UID using the default delete confirmation flow.")]
    [McpServerTool(
        Title = "Gradium Delete Voice",
        Name = "gradium_voices_delete",
        Destructive = true,
        OpenWorld = true,
        ReadOnly = false)]
    public static async Task<CallToolResult?> Gradium_Voices_Delete(
        [Description("Voice UID to delete.")] string voice_uid,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(voice_uid);

            var settings = serviceProvider.GetRequiredService<GradiumSettings>();
            var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var normalizedVoiceUid = voice_uid.Trim();

            return await requestContext.ConfirmAndDeleteAsync<ConfirmDeleteGradiumVoice>(
                normalizedVoiceUid,
                async ct =>
                {
                    using var client = clientFactory.CreateClient();
                    using var req = new HttpRequestMessage(HttpMethod.Delete, $"{VoicesUrl}{normalizedVoiceUid}");
                    req.Headers.Add("x-api-key", settings.ApiKey);

                    using var resp = await client.SendAsync(req, ct);
                    var body = await resp.Content.ReadAsStringAsync(ct);
                    if (!resp.IsSuccessStatusCode)
                        throw new InvalidOperationException($"Gradium delete voice failed ({(int)resp.StatusCode}): {body}");
                },
                $"Voice '{normalizedVoiceUid}' deleted successfully.",
                cancellationToken);
        });

    private static async Task<CallToolResult?> GenerateAndUploadSpeechAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string text,
        string voiceId,
        string outputFormat,
        string? jsonConfig,
        string modelName,
        bool onlyAudio,
        string filename,
        CancellationToken cancellationToken)
    {
        ValidateSpeechRequest(text, voiceId);

        var settings = serviceProvider.GetRequiredService<GradiumSettings>();
        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var normalizedFormat = NormalizeOutputFormat(outputFormat);

        var payload = new Dictionary<string, object?>
        {
            ["text"] = text,
            ["voice_id"] = voiceId.Trim(),
            ["output_format"] = normalizedFormat,
            ["only_audio"] = onlyAudio
        };

        if (!string.IsNullOrWhiteSpace(jsonConfig))
            payload["json_config"] = jsonConfig.Trim();

        if (!string.IsNullOrWhiteSpace(modelName))
            payload["model_name"] = modelName.Trim();

        using var client = clientFactory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, SpeechUrl);
        req.Headers.Add("x-api-key", settings.ApiKey);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(GetAcceptMimeType(normalizedFormat)));
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var resp = await client.SendAsync(req, cancellationToken);
        var bytes = await resp.Content.ReadAsByteArrayAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
        {
            var body = Encoding.UTF8.GetString(bytes);
            throw new InvalidOperationException($"Gradium speech call failed ({(int)resp.StatusCode}): {body}");
        }

        var extension = ResolveExtension(resp.Content.Headers.ContentType?.MediaType, normalizedFormat);
        var uploadName = filename.EndsWith($".{extension}", StringComparison.OrdinalIgnoreCase)
            ? filename
            : $"{filename}.{extension}";

        var uploaded = await requestContext.Server.Upload(
            serviceProvider,
            uploadName,
            BinaryData.FromBytes(bytes),
            cancellationToken);

        return uploaded?.ToResourceLinkCallToolResponse();
    }

    private static void ValidateSpeechRequest(string text, string voiceId)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ValidationException("text is required.");

        if (string.IsNullOrWhiteSpace(voiceId))
            throw new ValidationException("voice_id is required.");
    }

    private static void ValidateCreateVoice(GradiumCreateVoiceRequest request, string? sourceFileName)
    {
        if (string.IsNullOrWhiteSpace(request.FileUrl))
            throw new ValidationException("fileUrl is required.");

        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ValidationException("name is required.");

        if (string.IsNullOrWhiteSpace(sourceFileName))
            throw new ValidationException("audio_file is required.");
    }

    private static string NormalizeOutputFormat(string? outputFormat)
    {
        var value = (outputFormat ?? "wav").Trim().ToLowerInvariant();
        return value is "wav" or "pcm" or "opus" or "ulaw_8000" or "alaw_8000" or "pcm_8000" or "pcm_16000" or "pcm_24000"
            ? value
            : "wav";
    }

    private static string GetAcceptMimeType(string outputFormat)
        => outputFormat switch
        {
            "opus" => "audio/ogg",
            "pcm" or "pcm_8000" or "pcm_16000" or "pcm_24000" => "audio/pcm",
            "ulaw_8000" or "alaw_8000" => "audio/basic",
            _ => "audio/wav"
        };

    private static string ResolveExtension(string? mimeType, string fallbackFormat)
    {
        var mediaType = mimeType?.Trim().ToLowerInvariant();
        return mediaType switch
        {
            "audio/wav" => "wav",
            "audio/x-wav" => "wav",
            "audio/ogg" => "ogg",
            "audio/pcm" => "pcm",
            "audio/basic" => fallbackFormat is "alaw_8000" ? "alaw" : "ulaw",
            _ => fallbackFormat switch
            {
                "opus" => "ogg",
                "pcm" or "pcm_8000" or "pcm_16000" or "pcm_24000" => "pcm",
                "ulaw_8000" => "ulaw",
                "alaw_8000" => "alaw",
                _ => "wav"
            }
        };
    }

    [Description("Please fill in the Gradium text-to-speech request.")]
    public sealed class GradiumTextToSpeechRequest
    {
        [JsonPropertyName("text")]
        [Required]
        [Description("Text to synthesize.")]
        public string Text { get; set; } = default!;

        [JsonPropertyName("voice_id")]
        [Required]
        [Description("Gradium voice ID.")]
        public string VoiceId { get; set; } = default!;

        [JsonPropertyName("output_format")]
        [Required]
        [Description("Output format: wav, pcm, opus, ulaw_8000, alaw_8000, pcm_8000, pcm_16000, pcm_24000.")]
        public string OutputFormat { get; set; } = "wav";

        [JsonPropertyName("json_config")]
        [Description("Optional additional JSON config string.")]
        public string? JsonConfig { get; set; }

        [JsonPropertyName("model_name")]
        [Description("Optional TTS model name.")]
        public string ModelName { get; set; } = "default";

        [JsonPropertyName("only_audio")]
        [Description("Whether to return only raw audio bytes.")]
        public bool OnlyAudio { get; set; } = true;

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }

    [Description("Please fill in the Gradium file-to-speech request.")]
    public sealed class GradiumFileToSpeechRequest
    {
        [JsonPropertyName("fileUrl")]
        [Required]
        [Description("Source file URL to scrape/extract text from.")]
        public string FileUrl { get; set; } = default!;

        [JsonPropertyName("voice_id")]
        [Required]
        [Description("Gradium voice ID.")]
        public string VoiceId { get; set; } = default!;

        [JsonPropertyName("output_format")]
        [Required]
        [Description("Output format: wav, pcm, opus, ulaw_8000, alaw_8000, pcm_8000, pcm_16000, pcm_24000.")]
        public string OutputFormat { get; set; } = "wav";

        [JsonPropertyName("json_config")]
        [Description("Optional additional JSON config string.")]
        public string? JsonConfig { get; set; }

        [JsonPropertyName("model_name")]
        [Description("Optional TTS model name.")]
        public string ModelName { get; set; } = "default";

        [JsonPropertyName("only_audio")]
        [Description("Whether to return only raw audio bytes.")]
        public bool OnlyAudio { get; set; } = true;

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }

    [Description("Please fill in the Gradium create voice request.")]
    public sealed class GradiumCreateVoiceRequest
    {
        [JsonPropertyName("fileUrl")]
        [Required]
        [Description("Audio file URL to upload for voice cloning.")]
        public string FileUrl { get; set; } = default!;

        [JsonPropertyName("name")]
        [Required]
        [Description("Voice name.")]
        public string Name { get; set; } = default!;

        [JsonPropertyName("input_format")]
        [Description("Optional input format.")]
        public string? InputFormat { get; set; }

        [JsonPropertyName("description")]
        [Description("Optional description.")]
        public string? Description { get; set; }

        [JsonPropertyName("language")]
        [Description("Optional language.")]
        public string? Language { get; set; }

        [JsonPropertyName("start_s")]
        [Description("Audio start offset in seconds.")]
        public double StartS { get; set; } = 0;

        [JsonPropertyName("timeout_s")]
        [Description("Timeout in seconds.")]
        public double TimeoutS { get; set; } = 10;
    }

    [Description("Please fill in the Gradium update voice request.")]
    public sealed class GradiumUpdateVoiceRequest
    {
        [JsonPropertyName("voice_uid")]
        [Required]
        [Description("Voice UID.")]
        public string VoiceUid { get; set; } = default!;

        [JsonPropertyName("name")]
        [Description("Updated name.")]
        public string? Name { get; set; }

        [JsonPropertyName("description")]
        [Description("Updated description.")]
        public string? Description { get; set; }

        [JsonPropertyName("language")]
        [Description("Updated language.")]
        public string? Language { get; set; }

        [JsonPropertyName("start_s")]
        [Description("Updated start offset.")]
        public double? StartS { get; set; }

        [JsonPropertyName("rank")]
        [Description("Updated rank.")]
        public double? Rank { get; set; }
    }

    [Description("Please confirm deletion of the voice UID: {0}")]
    public sealed class ConfirmDeleteGradiumVoice : IHasName
    {
        [Required]
        [JsonPropertyName("name")]
        [Description("Type the exact voice UID to confirm deletion.")]
        public string? Name { get; set; }
    }
}
