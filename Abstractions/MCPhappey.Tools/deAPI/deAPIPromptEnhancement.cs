using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.deAPI;

public static class deAPIPromptEnhancement
{
    private const string BaseUrl = "https://api.deapi.ai";
    private const string ImagePromptPath = "/api/v1/client/prompt/image";
    private const string VideoPromptPath = "/api/v1/client/prompt/video";
    private const string SpeechPromptPath = "/api/v1/client/prompt/speech";
    private const string ImageToImagePromptPath = "/api/v1/client/prompt/image2image";

    [Description("Enhance text-to-image prompts for better AI generation results.")]
    [McpServerTool(
        Title = "deAPI Image Prompt Booster",
        Name = "deapi_prompt_enhance_image",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true)]
    public static async Task<PromptEnhancementResult> deAPI_Prompt_EnhanceImage(
        IServiceProvider serviceProvider,
        [Description("Prompt to enhance (minimum 3 characters).")]
        string prompt,
        [Description("Optional negative prompt to enhance (minimum 3 characters when provided).")]
        string? negative_prompt = null,
        CancellationToken cancellationToken = default)
    {
        ValidatePrompt(prompt, "prompt");
        ValidateOptionalPrompt(negative_prompt, "negative_prompt");

        var payload = new
        {
            prompt,
            negative_prompt = string.IsNullOrWhiteSpace(negative_prompt) ? null : negative_prompt
        };

        using var doc = await PostJsonAsync(serviceProvider, ImagePromptPath, payload, cancellationToken);
        return ParsePromptAndNegativePrompt(doc.RootElement);
    }

    [Description("Enhance text/image-to-video prompts for better AI generation results.")]
    [McpServerTool(
        Title = "deAPI Video Prompt Booster",
        Name = "deapi_prompt_enhance_video",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true)]
    public static async Task<PromptEnhancementResult> deAPI_Prompt_EnhanceVideo(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Prompt to enhance (minimum 3 characters).")]
        string prompt,
        [Description("Optional negative prompt to enhance (minimum 3 characters when provided).")]
        string? negative_prompt = null,
        [Description("Optional image file URL (SharePoint/OneDrive/HTTP) to guide prompt enhancement.")]
        string? fileUrl = null,
        CancellationToken cancellationToken = default)
    {
        ValidatePrompt(prompt, "prompt");
        ValidateOptionalPrompt(negative_prompt, "negative_prompt");

        var settings = serviceProvider.GetRequiredService<deAPISettings>();
        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();

        using var client = clientFactory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}{VideoPromptPath}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(prompt), "prompt");
        if (!string.IsNullOrWhiteSpace(negative_prompt))
            form.Add(new StringContent(negative_prompt), "negative_prompt");

        if (!string.IsNullOrWhiteSpace(fileUrl))
        {
            var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
            var source = files.FirstOrDefault() ?? throw new ValidationException("fileUrl could not be downloaded.");

            var contentType = string.IsNullOrWhiteSpace(source.MimeType) ? "application/octet-stream" : source.MimeType;
            var imageContent = new ByteArrayContent(source.Contents.ToArray());
            imageContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            var sourceName = string.IsNullOrWhiteSpace(source.Filename) ? "reference_image.png" : source.Filename;
            form.Add(imageContent, "image", sourceName);
        }

        req.Content = form;

        using var resp = await client.SendAsync(req, cancellationToken);
        var json = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"{resp.StatusCode}: {json}");

        using var doc = JsonDocument.Parse(json);
        return ParsePromptAndNegativePrompt(doc.RootElement);
    }

    [Description("Enhance text-to-speech prompts for better AI generation results.")]
    [McpServerTool(
        Title = "deAPI Speech Prompt Booster",
        Name = "deapi_prompt_enhance_speech",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true)]
    public static async Task<SpeechPromptEnhancementResult> deAPI_Prompt_EnhanceSpeech(
        IServiceProvider serviceProvider,
        [Description("Prompt to enhance (minimum 3 characters).")]
        string prompt,
        [Description("Optional language code used to optimize pronunciation and phrasing.")]
        string? lang_code = null,
        CancellationToken cancellationToken = default)
    {
        ValidatePrompt(prompt, "prompt");

        var payload = new
        {
            prompt,
            lang_code = string.IsNullOrWhiteSpace(lang_code) ? null : lang_code
        };

        using var doc = await PostJsonAsync(serviceProvider, SpeechPromptPath, payload, cancellationToken);
        var enhancedPrompt = doc.RootElement.TryGetProperty("prompt", out var promptElement)
            ? promptElement.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(enhancedPrompt))
            throw new Exception("deAPI response did not contain an enhanced prompt.");

        return new SpeechPromptEnhancementResult
        {
            Prompt = enhancedPrompt
        };
    }

    [Description("Enhance image-to-image prompts using visual context from a required reference image.")]
    [McpServerTool(
        Title = "deAPI Image-to-Image Prompt Booster",
        Name = "deapi_prompt_enhance_image_to_image",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true)]
    public static async Task<PromptEnhancementResult> deAPI_Prompt_EnhanceImageToImage(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Prompt to enhance (minimum 3 characters).")]
        string prompt,
        [Description("Image file URL (SharePoint/OneDrive/HTTP) used as required reference for enhancement.")]
        string fileUrl,
        [Description("Optional negative prompt to enhance (minimum 3 characters when provided).")]
        string? negative_prompt = null,
        CancellationToken cancellationToken = default)
    {
        ValidatePrompt(prompt, "prompt");
        ValidatePrompt(fileUrl, "fileUrl");
        ValidateOptionalPrompt(negative_prompt, "negative_prompt");

        var settings = serviceProvider.GetRequiredService<deAPISettings>();
        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();

        var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
        var source = files.FirstOrDefault() ?? throw new ValidationException("fileUrl could not be downloaded.");

        using var client = clientFactory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}{ImageToImagePromptPath}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(prompt), "prompt");
        if (!string.IsNullOrWhiteSpace(negative_prompt))
            form.Add(new StringContent(negative_prompt), "negative_prompt");

        var contentType = string.IsNullOrWhiteSpace(source.MimeType) ? "application/octet-stream" : source.MimeType;
        var imageContent = new ByteArrayContent(source.Contents.ToArray());
        imageContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        var sourceName = string.IsNullOrWhiteSpace(source.Filename) ? "reference_image.png" : source.Filename;
        form.Add(imageContent, "image", sourceName);

        req.Content = form;

        using var resp = await client.SendAsync(req, cancellationToken);
        var json = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"{resp.StatusCode}: {json}");

        using var doc = JsonDocument.Parse(json);
        return ParsePromptAndNegativePrompt(doc.RootElement);
    }

    private static async Task<JsonDocument> PostJsonAsync(
        IServiceProvider serviceProvider,
        string path,
        object payload,
        CancellationToken cancellationToken)
    {
        var settings = serviceProvider.GetRequiredService<deAPISettings>();
        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        using var client = clientFactory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}{path}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, MimeTypes.Json);

        using var resp = await client.SendAsync(req, cancellationToken);
        var json = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"{resp.StatusCode}: {json}");

        return JsonDocument.Parse(json);
    }

    private static PromptEnhancementResult ParsePromptAndNegativePrompt(JsonElement root)
    {
        var enhancedPrompt = root.TryGetProperty("prompt", out var promptElement)
            ? promptElement.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(enhancedPrompt))
            throw new Exception("deAPI response did not contain an enhanced prompt.");

        var enhancedNegativePrompt = root.TryGetProperty("negative_prompt", out var negativeElement)
            ? negativeElement.GetString()
            : null;

        return new PromptEnhancementResult
        {
            Prompt = enhancedPrompt,
            NegativePrompt = string.IsNullOrWhiteSpace(enhancedNegativePrompt) ? null : enhancedNegativePrompt
        };
    }

    private static void ValidatePrompt(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ValidationException($"{fieldName} is required.");

        if (value.Trim().Length < 3)
            throw new ValidationException($"{fieldName} must be at least 3 characters.");
    }

    private static void ValidateOptionalPrompt(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (value.Trim().Length < 3)
            throw new ValidationException($"{fieldName} must be at least 3 characters when provided.");
    }

    public sealed class PromptEnhancementResult
    {
        [Description("Enhanced prompt text.")]
        public required string Prompt { get; set; }

        [Description("Enhanced negative prompt text when provided or returned by the API.")]
        public string? NegativePrompt { get; set; }
    }

    public sealed class SpeechPromptEnhancementResult
    {
        [Description("Enhanced speech prompt text.")]
        public required string Prompt { get; set; }
    }
}
