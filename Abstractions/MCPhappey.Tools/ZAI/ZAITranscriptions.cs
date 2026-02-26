using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.ZAI;

public static class ZAITranscriptions
{
    private const string TranscriptionsUrl = "https://api.z.ai/api/paas/v4/audio/transcriptions";

    [Description("Transcribe audio from fileUrl using Z.AI GLM-ASR, returning structured transcription content.")]
    [McpServerTool(
        Title = "Z.AI Audio Transcription",
        Name = "zai_transcriptions_create",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> ZAI_Transcriptions_Create(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Audio file URL (.wav/.mp3 and other downloadable audio formats) to transcribe. Supports SharePoint/OneDrive/HTTPS.")] string fileUrl,
        [Description("Model ID. Default: glm-asr-2512.")] string model = "glm-asr-2512",
        [Description("Optional prompt context to improve recognition in long text scenarios.")] string? prompt = null,
        [Description("Optional hotwords, comma-separated. Max 100 entries.")] string? hotwords = null,
        [Description("Optional unique client request ID.")] string? requestId = null,
        [Description("Optional unique end-user ID (6-128 chars).")]
        string? userId = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(fileUrl);

                var downloadService = serviceProvider.GetRequiredService<DownloadService>();
                var settings = serviceProvider.GetRequiredService<ZAISettings>();
                var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

                var downloads = await downloadService.DownloadContentAsync(
                    serviceProvider,
                    requestContext.Server,
                    fileUrl,
                    cancellationToken);

                var audio = downloads.FirstOrDefault()
                    ?? throw new InvalidOperationException("Failed to download audio content from fileUrl.");

                var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                    new ZAITranscriptionRequest
                    {
                        FileUrl = fileUrl,
                        Model = model,
                        Prompt = prompt,
                        Hotwords = hotwords,
                    },
                    cancellationToken);

                if (notAccepted != null)
                    throw new InvalidOperationException("Transcription request was not accepted.");

                if (typed == null)
                    throw new InvalidOperationException("No transcription input data provided.");

                if (!string.Equals(typed.Model, "glm-asr-2512", StringComparison.OrdinalIgnoreCase))
                    throw new ValidationException("model must be glm-asr-2512.");

                var hotwordList = ParseHotwords(typed.Hotwords);
                if (hotwordList.Count > 100)
                    throw new ValidationException("hotwords cannot exceed 100 entries.");

                using var form = new MultipartFormDataContent();

                var fileContent = new StreamContent(audio.Contents.ToStream());
                fileContent.Headers.ContentType = new MediaTypeHeaderValue(audio.MimeType ?? "audio/mpeg");
                form.Add(fileContent, "file", audio.Filename ?? "input.mp3");

                form.Add(new StringContent("glm-asr-2512"), "model");
                form.Add(new StringContent("false"), "stream");

                if (!string.IsNullOrWhiteSpace(typed.Prompt))
                    form.Add(new StringContent(typed.Prompt), "prompt");

                if (hotwordList.Count > 0)
                    form.Add(new StringContent(JsonSerializer.Serialize(hotwordList)), "hotwords");           

                using var client = clientFactory.CreateClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));

                using var response = await client.PostAsync(TranscriptionsUrl, form, cancellationToken);
                var json = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                    throw new Exception($"{response.StatusCode}: {json}");

                var parsed = JsonNode.Parse(json)?.AsObject()
                    ?? throw new Exception("Z.AI returned an empty or invalid JSON response.");

                return new JsonObject
                {
                    ["provider"] = "z.ai",
                    ["type"] = "transcription",
                    ["fileUrl"] = typed.FileUrl,
                    ["id"] = parsed["id"]?.GetValue<string>(),
                    ["created"] = parsed["created"]?.GetValue<long>(),
                    ["request_id"] = parsed["request_id"]?.GetValue<string>(),
                    ["model"] = parsed["model"]?.GetValue<string>() ?? "glm-asr-2512",
                    ["text"] = parsed["text"]?.GetValue<string>() ?? string.Empty,
                    ["raw"] = parsed
                };
            }));

    private static List<string> ParseHotwords(string? hotwords)
        => string.IsNullOrWhiteSpace(hotwords)
            ? []
            : [.. hotwords
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)];
}

[Description("Please confirm the Z.AI transcription request.")]
public sealed class ZAITranscriptionRequest
{
    [JsonPropertyName("fileUrl")]
    [Required]
    [Description("Audio file URL to transcribe.")]
    public string FileUrl { get; set; } = default!;

    [JsonPropertyName("model")]
    [Required]
    [Description("Model id. Must be glm-asr-2512.")]
    public string Model { get; set; } = "glm-asr-2512";

    [JsonPropertyName("prompt")]
    [Description("Optional prompt context to improve recognition.")]
    public string? Prompt { get; set; }

    [JsonPropertyName("hotwords")]
    [Description("Optional comma-separated hotwords. Max 100 entries.")]
    public string? Hotwords { get; set; }

}

