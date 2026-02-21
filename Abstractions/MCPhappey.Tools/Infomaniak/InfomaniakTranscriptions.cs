using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MCPhappey.Common.Extensions;

namespace MCPhappey.Tools.Infomaniak;

public static class InfomaniakTranscriptions
{
    private const string ApiBaseUrl = "https://api.infomaniak.com";

    [Description("Create an Infomaniak transcription from an audio/video file URL. Returns transcription as structured content only.")]
    [McpServerTool(
        Title = "Infomaniak Audio Transcription",
        Name = "infomaniak_transcriptions_create",
        Destructive = false,
        ReadOnly = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> InfomaniakTranscriptions_Create(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("URL of the media file to transcribe (supports SharePoint/OneDrive/HTTP).")]
        string fileUrl,
        [Description("Infomaniak AI product id. If omitted, tries x-infomaniak-product-id from headers.")]
        int? productId = null,
        [Description("Model name to use. Default: whisper.")]
        string model = "whisper",
        [Description("Optional language code (e.g. en, nl, fr).")]
        string? language = null,
        [Description("Response format: json, text, srt, verbose_json, or vtt.")]
        string responseFormat = "json",
        [Description("Optional prompt to guide transcription style.")]
        string? prompt = null,
        [Description("Polling interval in seconds.")]
        int pollingIntervalSeconds = 2,
        [Description("Maximum wait time in seconds before timeout.")]
        int maxWaitSeconds = 900,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(fileUrl);
                ArgumentException.ThrowIfNullOrWhiteSpace(model);

                var settings = serviceProvider.GetRequiredService<InfomaniakSettings>();
                var downloadService = serviceProvider.GetRequiredService<DownloadService>();
                var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

                var resolvedProductId = productId ?? settings.DefaultProductId
                    ?? throw new ValidationException("Missing productId. Provide it explicitly or configure x-infomaniak-product-id header.");

                pollingIntervalSeconds = Math.Max(1, pollingIntervalSeconds);
                maxWaitSeconds = Math.Max(30, maxWaitSeconds);

                var downloads = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
                var media = downloads.FirstOrDefault() ?? throw new InvalidOperationException("Failed to download media content from fileUrl.");

                var (typed, _, _) = await requestContext.Server.TryElicit(
                    new InfomaniakTranscriptionRequest
                    {
                        ProductId = resolvedProductId,
                        Model = model,
                        Language = language,
                        ResponseFormat = responseFormat,
                        Prompt = prompt,
                        PollingIntervalSeconds = pollingIntervalSeconds,
                        MaxWaitSeconds = maxWaitSeconds
                    },
                    cancellationToken);

                using var client = clientFactory.CreateClient();
                client.BaseAddress = new Uri(ApiBaseUrl);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var fileBase64 = Convert.ToBase64String(media.Contents.ToArray());
                var createPayload = new JsonObject
                {
                    ["file"] = fileBase64,
                    ["model"] = typed.Model,
                };

                if (!string.IsNullOrWhiteSpace(typed.Language))
                    createPayload["language"] = typed.Language;

                if (!string.IsNullOrWhiteSpace(typed.ResponseFormat))
                    createPayload["response_format"] = typed.ResponseFormat;

                if (!string.IsNullOrWhiteSpace(typed.Prompt))
                    createPayload["prompt"] = typed.Prompt;

                var createPath = $"/1/ai/{typed.ProductId}/openai/audio/transcriptions";
                using var createReq = new HttpRequestMessage(HttpMethod.Post, createPath)
                {
                    Content = new StringContent(createPayload.ToJsonString(), Encoding.UTF8, "application/json")
                };

                using var createResp = await client.SendAsync(createReq, cancellationToken);
                var createJson = await createResp.Content.ReadAsStringAsync(cancellationToken);

                if (!createResp.IsSuccessStatusCode)
                    throw new Exception($"{createResp.StatusCode}: {createJson}");

                var batchId = ExtractBatchId(createJson)
                    ?? throw new Exception("Infomaniak response did not contain batch_id.");

                var statusPath = $"/1/ai/{typed.ProductId}/results/{Uri.EscapeDataString(batchId)}";
                var downloadPath = $"/1/ai/{typed.ProductId}/results/{Uri.EscapeDataString(batchId)}/download";

                string? statusJson = null;
                string status = "unknown";
                var sw = Stopwatch.StartNew();

                while (true)
                {
                    if (sw.Elapsed > TimeSpan.FromSeconds(typed.MaxWaitSeconds))
                        throw new TimeoutException($"Infomaniak transcription timed out after {typed.MaxWaitSeconds}s.");

                    cancellationToken.ThrowIfCancellationRequested();

                    using var statusReq = new HttpRequestMessage(HttpMethod.Get, statusPath);
                    using var statusResp = await client.SendAsync(statusReq, cancellationToken);
                    statusJson = await statusResp.Content.ReadAsStringAsync(cancellationToken);

                    if (!statusResp.IsSuccessStatusCode)
                        throw new Exception($"{statusResp.StatusCode}: {statusJson}");

                    status = ExtractStatus(statusJson) ?? "unknown";
                    if (IsCompleted(status))
                        break;

                    if (IsFailed(status))
                        throw new Exception($"Infomaniak transcription failed. Status={status}. Payload={statusJson}");

                    await Task.Delay(TimeSpan.FromSeconds(typed.PollingIntervalSeconds), cancellationToken);
                }

                using var downloadReq = new HttpRequestMessage(HttpMethod.Get, downloadPath);
                using var downloadResp = await client.SendAsync(downloadReq, cancellationToken);
                var downloadRaw = await downloadResp.Content.ReadAsStringAsync(cancellationToken);

                if (!downloadResp.IsSuccessStatusCode)
                    throw new Exception($"{downloadResp.StatusCode}: {downloadRaw}");

                JsonNode? downloadStructured;
                string? transcriptText;
                var contentType = downloadResp.Content.Headers.ContentType?.MediaType;

                if (string.Equals(contentType, "application/json", StringComparison.OrdinalIgnoreCase))
                {
                    downloadStructured = JsonNode.Parse(downloadRaw) ?? new JsonObject();
                    transcriptText = ExtractTranscriptText(downloadRaw);
                }
                else
                {
                    transcriptText = string.IsNullOrWhiteSpace(downloadRaw) ? null : downloadRaw;
                    downloadStructured = new JsonObject
                    {
                        ["contentType"] = contentType,
                        ["text"] = transcriptText
                    };
                }

                return new JsonObject
                {
                    ["provider"] = "infomaniak",
                    ["productId"] = typed.ProductId,
                    ["batchId"] = batchId,
                    ["status"] = status,
                    ["transcriptText"] = transcriptText,
                    ["statusResult"] = string.IsNullOrWhiteSpace(statusJson) ? new JsonObject() : JsonNode.Parse(statusJson),
                    ["downloadResult"] = downloadStructured
                };
            }));

    [Description("Please fill in the Infomaniak transcription request details.")]
    public sealed class InfomaniakTranscriptionRequest
    {
        [JsonPropertyName("product_id")]
        [Required]
        [Description("Infomaniak AI product id.")]
        public int ProductId { get; set; }

        [JsonPropertyName("model")]
        [Required]
        [Description("Model name. Example: whisper.")]
        public string Model { get; set; } = "whisper";

        [JsonPropertyName("language")]
        [Description("Optional language code (ISO).")]
        public string? Language { get; set; }

        [JsonPropertyName("response_format")]
        [Description("json, text, srt, verbose_json, or vtt.")]
        public string ResponseFormat { get; set; } = "json";

        [JsonPropertyName("prompt")]
        [Description("Optional prompt.")]
        public string? Prompt { get; set; }

        [JsonPropertyName("polling_interval_seconds")]
        [Range(1, 60)]
        [Description("Polling interval in seconds.")]
        public int PollingIntervalSeconds { get; set; } = 2;

        [JsonPropertyName("max_wait_seconds")]
        [Range(30, 3600)]
        [Description("Maximum total wait time in seconds.")]
        public int MaxWaitSeconds { get; set; } = 900;
    }

    private static string? ExtractBatchId(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("batch_id", out var batchId) && batchId.ValueKind == JsonValueKind.String)
            return batchId.GetString();

        if (root.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("batch_id", out var nestedBatchId)
            && nestedBatchId.ValueKind == JsonValueKind.String)
            return nestedBatchId.GetString();

        return null;
    }

    private static string? ExtractStatus(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("status", out var status))
            return status.ValueKind == JsonValueKind.String ? status.GetString() : status.ToString();

        if (root.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("status", out var nestedStatus))
            return nestedStatus.ValueKind == JsonValueKind.String ? nestedStatus.GetString() : nestedStatus.ToString();

        return null;
    }

    private static string? ExtractTranscriptText(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            return text.GetString();

        if (root.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("text", out var nestedText)
            && nestedText.ValueKind == JsonValueKind.String)
            return nestedText.GetString();

        return null;
    }

    private static bool IsCompleted(string status)
        => status.Equals("completed", StringComparison.OrdinalIgnoreCase)
           || status.Equals("done", StringComparison.OrdinalIgnoreCase)
           || status.Equals("success", StringComparison.OrdinalIgnoreCase)
           || status.Equals("succeeded", StringComparison.OrdinalIgnoreCase)
           || status.Equals("finished", StringComparison.OrdinalIgnoreCase);

    private static bool IsFailed(string status)
        => status.Equals("failed", StringComparison.OrdinalIgnoreCase)
           || status.Equals("error", StringComparison.OrdinalIgnoreCase)
           || status.Equals("canceled", StringComparison.OrdinalIgnoreCase)
           || status.Equals("cancelled", StringComparison.OrdinalIgnoreCase);
}

