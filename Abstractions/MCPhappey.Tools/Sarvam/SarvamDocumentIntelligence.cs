using System.ComponentModel;
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

namespace MCPhappey.Tools.Sarvam;

public static class SarvamDocumentIntelligence
{
    private const string JobsBaseUrl = "https://api.sarvam.ai/doc-digitization/job/v1";

    private static readonly HashSet<string> SupportedLanguages =
    [
        "hi-IN", "en-IN", "bn-IN", "gu-IN", "kn-IN", "ml-IN", "mr-IN", "or-IN", "pa-IN", "ta-IN",
        "te-IN", "ur-IN", "as-IN", "bodo-IN", "doi-IN", "ks-IN", "kok-IN", "mai-IN", "mni-IN",
        "ne-IN", "sa-IN", "sat-IN", "sd-IN"
    ];

    private static readonly HashSet<string> SupportedOutputFormats = ["html", "md", "json"];

    [Description("Run Sarvam Document Intelligence end-to-end from fileUrl (SharePoint/OneDrive/HTTPS): create job, upload file, start, poll until completion, download result, upload output to SharePoint/OneDrive, and return only a resource link block.")]
    [McpServerTool(
        Title = "Sarvam Document Intelligence",
        Name = "sarvam_document_intelligence_process_file",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Sarvam_DocumentIntelligence_ProcessFile(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("File URL to process (SharePoint, OneDrive, or HTTPS).")]
        string fileUrl,
        [Description("Primary language in BCP-47 format. Supported: hi-IN,en-IN,bn-IN,gu-IN,kn-IN,ml-IN,mr-IN,or-IN,pa-IN,ta-IN,te-IN,ur-IN,as-IN,bodo-IN,doi-IN,ks-IN,kok-IN,mai-IN,mni-IN,ne-IN,sa-IN,sat-IN,sd-IN. Default: hi-IN.")]
        string language = "hi-IN",
        [Description("Output format for extracted content. Allowed: html, md, json. Default: md.")]
        string output_format = "md",
        [Description("Optional output filename without extension. If omitted, an MCP output name is generated.")]
        string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new SarvamDocumentIntelligenceRequest
                {
                    FileUrl = fileUrl,
                    Language = NormalizeLanguageCode(language),
                    OutputFormat = NormalizeOutputFormat(output_format),
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            ArgumentException.ThrowIfNullOrWhiteSpace(typed.FileUrl);

            var settings = serviceProvider.GetRequiredService<SarvamSettings>();
            var downloadService = serviceProvider.GetRequiredService<DownloadService>();
            var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

            using var client = clientFactory.CreateClient();

            var inputFiles = await downloadService.DownloadContentAsync(
                serviceProvider,
                requestContext.Server,
                typed.FileUrl,
                cancellationToken);

            var inputFile = inputFiles.FirstOrDefault()
                ?? throw new InvalidOperationException("Failed to download input file from fileUrl.");

            var createPayload = new
            {
                job_parameters = new
                {
                    language = NormalizeLanguageCode(typed.Language),
                    output_format = NormalizeOutputFormat(typed.OutputFormat)
                }
            };

            var jobId = await CreateJobAsync(client, settings.ApiKey, createPayload, cancellationToken);

            await UploadInputFileAsync(
                client,
                settings.ApiKey,
                jobId,
                inputFile.Contents.ToArray(),
                string.IsNullOrWhiteSpace(inputFile.Filename) ? "document.bin" : inputFile.Filename!,
                string.IsNullOrWhiteSpace(inputFile.MimeType) ? "application/octet-stream" : inputFile.MimeType!,
                cancellationToken);

            await StartJobAsync(client, settings.ApiKey, jobId, cancellationToken);

            var finalState = await WaitForCompletionAsync(
                client,
                settings.ApiKey,
                jobId,
                3,
                900,
                requestContext,
                cancellationToken);

            if (finalState.State is "Failed")
                throw new InvalidOperationException($"Sarvam Document Intelligence job failed: {finalState.ErrorMessage ?? "Unknown error."}");

            var download = await GetDownloadUrlsAsync(client, settings.ApiKey, jobId, cancellationToken);
            var selected = SelectDownloadUrl(download.DownloadUrls)
                ?? throw new InvalidOperationException("Sarvam Document Intelligence returned no downloadable output URLs.");

            using var fileResp = await client.GetAsync(selected.Url, cancellationToken);
            var fileBytes = await fileResp.Content.ReadAsByteArrayAsync(cancellationToken);

            if (!fileResp.IsSuccessStatusCode)
            {
                var text = fileBytes.Length > 0 ? Encoding.UTF8.GetString(fileBytes) : string.Empty;
                throw new InvalidOperationException($"Failed to download Sarvam output ({(int)fileResp.StatusCode}): {text}");
            }

            if (fileBytes.Length == 0)
                throw new InvalidOperationException("Downloaded Sarvam output file is empty.");

            var extension = InferOutputExtension(selected.FileName, typed.OutputFormat, fileResp.Content.Headers.ContentType?.MediaType);
            var uploadName = typed.Filename.EndsWith($".{extension}", StringComparison.OrdinalIgnoreCase)
                ? typed.Filename
                : $"{typed.Filename}.{extension}";

            var uploaded = await requestContext.Server.Upload(
                serviceProvider,
                uploadName,
                BinaryData.FromBytes(fileBytes),
                cancellationToken);

            return uploaded?.ToResourceLinkCallToolResponse();
        });

    private static async Task<string> CreateJobAsync(HttpClient client, string apiKey, object payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, JobsBaseUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, MimeTypes.Json)
        };
        request.Headers.Add("api-subscription-key", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Sarvam create job failed ({(int)response.StatusCode}): {body}");

        using var doc = JsonDocument.Parse(body);
        var jobId = doc.RootElement.TryGetProperty("job_id", out var idNode)
            ? idNode.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(jobId))
            throw new InvalidOperationException("Sarvam create job response did not include job_id.");

        return jobId;
    }

    private static async Task UploadInputFileAsync(
        HttpClient client,
        string apiKey,
        string jobId,
        byte[] fileBytes,
        string filename,
        string mimeType,
        CancellationToken cancellationToken)
    {
        var uploadEndpoints = new[]
        {
            $"{JobsBaseUrl}/{Uri.EscapeDataString(jobId)}/upload-file",
            $"{JobsBaseUrl}/{Uri.EscapeDataString(jobId)}/upload"
        };

        var fieldNames = new[] { "file", "document", "raw_file" };
        Exception? lastError = null;

        foreach (var endpoint in uploadEndpoints)
        {
            foreach (var field in fieldNames)
            {
                using var form = new MultipartFormDataContent();
                var fileContent = new ByteArrayContent(fileBytes);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
                form.Add(fileContent, field, filename);

                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = form
                };
                request.Headers.Add("api-subscription-key", apiKey);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));

                using var response = await client.SendAsync(request, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.IsSuccessStatusCode)
                    return;

                lastError = new InvalidOperationException(
                    $"Sarvam upload attempt failed at endpoint '{endpoint}' with field '{field}' ({(int)response.StatusCode}): {body}");
            }
        }

        throw lastError ?? new InvalidOperationException("Sarvam upload failed with unknown error.");
    }

    private static async Task StartJobAsync(HttpClient client, string apiKey, string jobId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{JobsBaseUrl}/{Uri.EscapeDataString(jobId)}/start");
        request.Headers.Add("api-subscription-key", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Sarvam start job failed ({(int)response.StatusCode}): {body}");
    }

    private static async Task<SarvamJobState> WaitForCompletionAsync(
        HttpClient client,
        string apiKey,
        string jobId,
        int pollingIntervalSeconds,
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
                throw new TimeoutException($"Sarvam Document Intelligence job timed out after {maxWaitSeconds}s.");

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{JobsBaseUrl}/{Uri.EscapeDataString(jobId)}/status");
            request.Headers.Add("api-subscription-key", apiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));

            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Sarvam status polling failed ({(int)response.StatusCode}): {body}");

            using var doc = JsonDocument.Parse(body);
            var state = doc.RootElement.TryGetProperty("job_state", out var stateNode)
                ? (stateNode.GetString() ?? string.Empty)
                : string.Empty;

            var errorMessage = doc.RootElement.TryGetProperty("error_message", out var errorNode)
                ? errorNode.GetString()
                : null;

            poll++;
            await requestContext.Server.SendMessageNotificationAsync(
                $"Sarvam Document Intelligence status: {state} (poll #{poll})",
                LoggingLevel.Info,
                cancellationToken);

            if (state is "Completed" or "PartiallyCompleted" or "Failed")
                return new SarvamJobState(state, errorMessage);

            await Task.Delay(TimeSpan.FromSeconds(pollingIntervalSeconds), cancellationToken);
        }
    }

    private static async Task<SarvamDownloadResponse> GetDownloadUrlsAsync(
        HttpClient client,
        string apiKey,
        string jobId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{JobsBaseUrl}/{Uri.EscapeDataString(jobId)}/download-files");
        request.Headers.Add("api-subscription-key", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Sarvam get download URLs failed ({(int)response.StatusCode}): {body}");

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("download_urls", out var downloadUrls)
            || downloadUrls.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Sarvam download-files response did not include download_urls.");

        var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var prop in downloadUrls.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Object) continue;
            if (!prop.Value.TryGetProperty("file_url", out var urlNode)) continue;

            var url = urlNode.GetString();
            if (!string.IsNullOrWhiteSpace(url))
                results[prop.Name] = url!;
        }

        return new SarvamDownloadResponse(results);
    }

    private static DownloadSelection? SelectDownloadUrl(Dictionary<string, string> downloadUrls)
    {
        if (downloadUrls.Count == 0)
            return null;

        var preferred = downloadUrls
            .OrderByDescending(x => x.Key.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .First();

        return new DownloadSelection(preferred.Key, preferred.Value);
    }

    private static string NormalizeLanguageCode(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "hi-IN" : value.Trim();
        return SupportedLanguages.Contains(normalized) ? normalized : "hi-IN";
    }

    private static string NormalizeOutputFormat(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? "md"
            : value.Trim().ToLowerInvariant();
        return SupportedOutputFormats.Contains(normalized) ? normalized : "md";
    }

    private static string InferOutputExtension(string? sourceFileName, string outputFormat, string? contentType)
    {
        if (!string.IsNullOrWhiteSpace(sourceFileName))
        {
            var ext = Path.GetExtension(sourceFileName).TrimStart('.');
            if (!string.IsNullOrWhiteSpace(ext))
                return ext;
        }

        if (!string.IsNullOrWhiteSpace(contentType))
        {
            var normalized = contentType.ToLowerInvariant();
            if (normalized.Contains("zip")) return "zip";
            if (normalized.Contains("json")) return "json";
            if (normalized.Contains("html")) return "html";
            if (normalized.Contains("markdown") || normalized.Contains("text/plain")) return "md";
        }

        return NormalizeOutputFormat(outputFormat) switch
        {
            "json" => "zip",
            "html" => "zip",
            _ => "zip"
        };
    }

    [Description("Please fill in the Sarvam Document Intelligence request.")]
    public sealed class SarvamDocumentIntelligenceRequest
    {
        [JsonPropertyName("fileUrl")]
        [Description("File URL to process (SharePoint, OneDrive, or HTTPS).")]
        public string FileUrl { get; set; } = string.Empty;

        [JsonPropertyName("language")]
        [Description("Document language in BCP-47 format.")]
        public string Language { get; set; } = "hi-IN";

        [JsonPropertyName("output_format")]
        [Description("Output format: html, md, or json.")]
        public string OutputFormat { get; set; } = "md";

        [JsonPropertyName("filename")]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = "sarvam_document_intelligence";
    }

    private sealed record SarvamJobState(string State, string? ErrorMessage);
    private sealed record SarvamDownloadResponse(Dictionary<string, string> DownloadUrls);
    private sealed record DownloadSelection(string FileName, string Url);
}
