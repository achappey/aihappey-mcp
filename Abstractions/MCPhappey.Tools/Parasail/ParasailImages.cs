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
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Parasail;

public static class ParasailImages
{
    private const string ApiBaseUrl = "https://api.parasail.io";
    private const string FilesPath = "/v1/files";
    private const string BatchesPath = "/v1/batches";
    private const string ImagesEndpoint = "/v1/images/generations";
    private const int DefaultPollIntervalSeconds = 2;
    private const int DefaultMaxWaitSeconds = 600;

    [Description("Generate image(s) with Parasail batch API, always confirm via elicitation, upload outputs to SharePoint/OneDrive, and return only resource link blocks.")]
    [McpServerTool(
        Title = "Parasail image generation",
        Name = "parasail_images_generate",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Parasail_Images_Generate(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Prompt text for image generation.")] string prompt,
        [Description("Model ID (for example: Shitao/OmniGen-v1 or Qwen/Qwen-Image-Edit).")]
        string model = "Shitao/OmniGen-v1",
        [Description("Output image size formatted as WxH, for example 1024x1024.")]
        string size = "1024x1024",
        [Description("Number of batch requests to submit (1-20).")][Range(1, 20)] int batchCount = 1,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new ParasailImageGenerateRequest
                {
                    Prompt = prompt,
                    Model = model,
                    Size = size,
                    BatchCount = batchCount,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName(),
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            ValidateGenerateRequest(typed);

            var batchJsonl = BuildGenerateJsonl(typed);
            var outputText = await SubmitBatchAndGetOutputJsonlAsync(serviceProvider, batchJsonl, DefaultPollIntervalSeconds, DefaultMaxWaitSeconds, cancellationToken);
            var links = await UploadFromOutputJsonlAsync(serviceProvider, requestContext, outputText, typed.Filename, cancellationToken);

            if (links.Count == 0)
                throw new Exception("Parasail generation completed but no output images were uploaded.");

            return links.ToResourceLinkCallToolResponse();
        });

    [Description("Edit image(s) with Parasail batch API from exactly one input fileUrl, always confirm via elicitation, upload outputs to SharePoint/OneDrive, and return only resource link blocks.")]
    [McpServerTool(
        Title = "Parasail image editing",
        Name = "parasail_images_edit",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Parasail_Images_Edit(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Single input image URL (SharePoint/OneDrive/public HTTP).")]
        string fileUrl,
        [Description("Prompt text for editing. Reference image token example: <img><|image_1|></img>.")]
        string prompt,
        [Description("Model ID (for example: Qwen/Qwen-Image-Edit).")]
        string model = "Qwen/Qwen-Image-Edit",
        [Description("Output image size formatted as WxH, for example 1024x1024.")]
        string size = "1024x1024",
        [Description("Number of batch requests to submit (1-20).")][Range(1, 20)] int batchCount = 1,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new ParasailImageEditRequest
                {
                    FileUrl = fileUrl,
                    Prompt = prompt,
                    Model = model,
                    Size = size,
                    BatchCount = batchCount,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName(),
                    Confirmation = "GENERATE"
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            if (!string.Equals(typed.Confirmation?.Trim(), "GENERATE", StringComparison.OrdinalIgnoreCase))
                return "Image edit canceled: confirmation text must be 'GENERATE'.".ToErrorCallToolResponse();

            ValidateEditRequest(typed);

            var downloadService = serviceProvider.GetRequiredService<DownloadService>();
            var sourceFiles = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, typed.FileUrl, cancellationToken);
            var source = sourceFiles.FirstOrDefault() ?? throw new InvalidOperationException("Could not download source image from fileUrl.");
            var sourceBase64 = Convert.ToBase64String(source.Contents.ToArray());

            var batchJsonl = BuildEditJsonl(typed, sourceBase64);
            var outputText = await SubmitBatchAndGetOutputJsonlAsync(serviceProvider, batchJsonl,
                DefaultPollIntervalSeconds, DefaultMaxWaitSeconds, cancellationToken);
            var links = await UploadFromOutputJsonlAsync(serviceProvider, requestContext, outputText, typed.Filename, cancellationToken);

            if (links.Count == 0)
                throw new Exception("Parasail editing completed but no output images were uploaded.");

            return links.ToResourceLinkCallToolResponse();
        });

    private static async Task<string> SubmitBatchAndGetOutputJsonlAsync(
        IServiceProvider serviceProvider,
        string submissionJsonl,
        int pollIntervalSeconds,
        int maxWaitSeconds,
        CancellationToken cancellationToken)
    {
        var settings = serviceProvider.GetRequiredService<ParasailSettings>();
        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        using var client = clientFactory.CreateClient();
        client.BaseAddress = new Uri(ApiBaseUrl);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var inputFileId = await UploadBatchInputFileAsync(client, submissionJsonl, cancellationToken);
        var batchId = await CreateBatchAsync(client, inputFileId, cancellationToken);
        var outputFileId = await WaitForBatchCompletionAsync(client, batchId, pollIntervalSeconds, maxWaitSeconds, cancellationToken);
        return await DownloadFileContentAsync(client, outputFileId, cancellationToken);
    }

    private static async Task<string> UploadBatchInputFileAsync(HttpClient client, string jsonl, CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("batch"), "purpose");

        var bytes = Encoding.UTF8.GetBytes(jsonl);
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/jsonl");
        form.Add(fileContent, "file", "batch_submission.jsonl");

        using var resp = await client.PostAsync(FilesPath, form, cancellationToken);
        var raw = await resp.Content.ReadAsStringAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"Parasail file upload failed ({resp.StatusCode}): {raw}");

        using var doc = JsonDocument.Parse(raw);
        var fileId = doc.RootElement.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;

        if (string.IsNullOrWhiteSpace(fileId))
            throw new Exception("Parasail file upload succeeded but no file id was returned.");

        return fileId;
    }

    private static async Task<string> CreateBatchAsync(HttpClient client, string inputFileId, CancellationToken cancellationToken)
    {
        var body = new
        {
            input_file_id = inputFileId,
            endpoint = ImagesEndpoint,
            completion_window = "24h"
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, BatchesPath)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };

        using var resp = await client.SendAsync(req, cancellationToken);
        var raw = await resp.Content.ReadAsStringAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"Parasail batch creation failed ({resp.StatusCode}): {raw}");

        using var doc = JsonDocument.Parse(raw);
        var batchId = doc.RootElement.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;

        if (string.IsNullOrWhiteSpace(batchId))
            throw new Exception("Parasail batch creation succeeded but no batch id was returned.");

        return batchId;
    }

    private static async Task<string> WaitForBatchCompletionAsync(
        HttpClient client,
        string batchId,
        int pollIntervalSeconds,
        int maxWaitSeconds,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(maxWaitSeconds));

        while (!timeoutCts.IsCancellationRequested)
        {
            using var resp = await client.GetAsync($"{BatchesPath}/{batchId}", timeoutCts.Token);
            var raw = await resp.Content.ReadAsStringAsync(timeoutCts.Token);

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"Parasail batch polling failed ({resp.StatusCode}): {raw}");

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            var status = root.TryGetProperty("status", out var statusEl)
                ? statusEl.GetString()?.Trim().ToLowerInvariant()
                : null;

            if (status == "completed")
            {
                var outputFileId = root.TryGetProperty("output_file_id", out var outEl) ? outEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(outputFileId))
                    throw new Exception($"Parasail batch {batchId} completed but output_file_id is missing.");

                return outputFileId;
            }

            if (status is "failed" or "cancelled" or "expired")
            {
                var errors = root.TryGetProperty("errors", out var errorsEl) ? errorsEl.ToString() : "unknown error";
                throw new Exception($"Parasail batch {batchId} finished with status '{status}': {errors}");
            }

            await Task.Delay(TimeSpan.FromSeconds(pollIntervalSeconds), timeoutCts.Token);
        }

        throw new TimeoutException($"Parasail batch {batchId} did not complete within {maxWaitSeconds} seconds.");
    }

    private static async Task<string> DownloadFileContentAsync(HttpClient client, string fileId, CancellationToken cancellationToken)
    {
        using var resp = await client.GetAsync($"{FilesPath}/{fileId}/content", cancellationToken);
        var raw = await resp.Content.ReadAsStringAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"Parasail output download failed ({resp.StatusCode}): {raw}");

        return raw;
    }

    private static async Task<List<ResourceLinkBlock>> UploadFromOutputJsonlAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string outputJsonl,
        string filename,
        CancellationToken cancellationToken)
    {
        var links = new List<ResourceLinkBlock>();
        var index = 0;
        var baseName = filename.ToOutputFileName();

        var lines = outputJsonl
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            using var doc = JsonDocument.Parse(line);
            if (!doc.RootElement.TryGetProperty("response", out var responseObj)
                || !responseObj.TryGetProperty("body", out var bodyObj)
                || !bodyObj.TryGetProperty("data", out var dataArr)
                || dataArr.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in dataArr.EnumerateArray())
            {
                if (!item.TryGetProperty("b64_json", out var b64El) || b64El.ValueKind != JsonValueKind.String)
                    continue;

                var b64 = b64El.GetString();
                if (string.IsNullOrWhiteSpace(b64))
                    continue;

                byte[] imageBytes;
                try
                {
                    imageBytes = Convert.FromBase64String(b64);
                }
                catch (FormatException)
                {
                    continue;
                }

                index++;
                var uploaded = await requestContext.Server.Upload(
                    serviceProvider,
                    $"{baseName}-{index}.png",
                    BinaryData.FromBytes(imageBytes),
                    cancellationToken);

                if (uploaded != null)
                    links.Add(uploaded);
            }
        }

        return links;
    }

    private static string BuildGenerateJsonl(ParasailImageGenerateRequest input)
    {
        var sb = new StringBuilder();

        for (var i = 1; i <= input.BatchCount; i++)
        {
            var body = new
            {
                model = input.Model,
                prompt = input.Prompt,
                size = input.Size,
                response_format = "b64_json"
            };

            var lineObj = new
            {
                custom_id = $"generate-{i}",
                method = "POST",
                url = ImagesEndpoint,
                body
            };

            sb.AppendLine(JsonSerializer.Serialize(lineObj));
        }

        return sb.ToString();
    }

    private static string BuildEditJsonl(ParasailImageEditRequest input, string imageBase64)
    {
        var sb = new StringBuilder();

        for (var i = 1; i <= input.BatchCount; i++)
        {
            var body = new
            {
                model = input.Model,
                prompt = input.Prompt,
                size = input.Size,
                image = new[] { imageBase64 },
                response_format = "b64_json"
            };

            var lineObj = new
            {
                custom_id = $"edit-{i}",
                method = "POST",
                url = ImagesEndpoint,
                body
            };

            sb.AppendLine(JsonSerializer.Serialize(lineObj));
        }

        return sb.ToString();
    }

    private static void ValidateGenerateRequest(ParasailImageGenerateRequest input)
    {
        if (string.IsNullOrWhiteSpace(input.Prompt))
            throw new ValidationException("prompt is required.");

        if (string.IsNullOrWhiteSpace(input.Model))
            throw new ValidationException("model is required.");

        ValidateCommon(input.Size, input.BatchCount, "b64_json", DefaultPollIntervalSeconds, DefaultMaxWaitSeconds);
    }

    private static void ValidateEditRequest(ParasailImageEditRequest input)
    {
        if (string.IsNullOrWhiteSpace(input.FileUrl))
            throw new ValidationException("fileUrl is required.");

        if (string.IsNullOrWhiteSpace(input.Prompt))
            throw new ValidationException("prompt is required.");

        if (string.IsNullOrWhiteSpace(input.Model))
            throw new ValidationException("model is required.");

        ValidateCommon(input.Size, input.BatchCount, "b64_json", DefaultPollIntervalSeconds, DefaultMaxWaitSeconds);
    }

    private static void ValidateCommon(string size, int batchCount, string responseFormat, int pollIntervalSeconds, int maxWaitSeconds)
    {
        if (string.IsNullOrWhiteSpace(size) || !size.Contains('x', StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("size must be formatted as WxH, for example 1024x1024.");

        if (batchCount < 1 || batchCount > 20)
            throw new ValidationException("batchCount must be between 1 and 20.");

        if (!string.Equals(responseFormat, "b64_json", StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("responseFormat currently only supports: b64_json.");

        if (pollIntervalSeconds < 1 || pollIntervalSeconds > 60)
            throw new ValidationException("pollIntervalSeconds must be between 1 and 60.");

        if (maxWaitSeconds < 30 || maxWaitSeconds > 3600)
            throw new ValidationException("maxWaitSeconds must be between 30 and 3600.");
    }

}

[Description("Please confirm the Parasail image generation request.")]
public sealed class ParasailImageGenerateRequest
{
    [JsonPropertyName("model")]
    [Required]
    [Description("Model ID (for example: Shitao/OmniGen-v1 or Qwen/Qwen-Image-Edit).")]
    public string Model { get; set; } = "Shitao/OmniGen-v1";

    [JsonPropertyName("prompt")]
    [Required]
    [Description("Prompt text for image generation.")]
    public string Prompt { get; set; } = default!;

    [JsonPropertyName("size")]
    [Required]
    [Description("Output size as WxH, for example 1024x1024.")]
    public string Size { get; set; } = "1024x1024";

    [JsonPropertyName("batchCount")]
    [Range(1, 20)]
    [Description("Number of batch requests (1-20).")]
    public int BatchCount { get; set; } = 1;

    [JsonPropertyName("filename")]
    [Required]
    [Description("Output filename without extension.")]
    public string Filename { get; set; } = default!;

}

[Description("Please confirm the Parasail image edit request.")]
public sealed class ParasailImageEditRequest
{
    [JsonPropertyName("fileUrl")]
    [Required]
    [Description("Single input image URL (SharePoint/OneDrive/public HTTP).")]
    public string FileUrl { get; set; } = default!;

    [JsonPropertyName("model")]
    [Required]
    [Description("Model ID (for example: Qwen/Qwen-Image-Edit).")]
    public string Model { get; set; } = "Qwen/Qwen-Image-Edit";

    [JsonPropertyName("prompt")]
    [Required]
    [Description("Prompt text for image editing.")]
    public string Prompt { get; set; } = default!;

    [JsonPropertyName("size")]
    [Required]
    [Description("Output size as WxH, for example 1024x1024.")]
    public string Size { get; set; } = "1024x1024";

    [JsonPropertyName("batchCount")]
    [Range(1, 20)]
    [Description("Number of batch requests (1-20).")]
    public int BatchCount { get; set; } = 1;

    [JsonPropertyName("filename")]
    [Required]
    [Description("Output filename without extension.")]
    public string Filename { get; set; } = default!;

    [JsonPropertyName("confirmation")]
    [Required]
    [Description("Type GENERATE to confirm execution.")]
    public string Confirmation { get; set; } = "GENERATE";
}

