using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text.Json;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.deAPI;

public static class deAPIAnalysis
{
    private const string BaseUrl = "https://api.deapi.ai";
    private const string Img2TxtPath = "/api/v1/client/img2txt";

    [Description("Extract text from an image using deAPI OCR (image-to-text) and return structured JSON content.")]
    [McpServerTool(
        Title = "deAPI Image-to-Text Analysis",
        Name = "deapi_analysis_image_to_text",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> deAPI_Analysis_ImageToText(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Image file URL (SharePoint/OneDrive/HTTP) to process with OCR.")] string fileUrl,
        [Description("deAPI OCR model slug.")] string model,
        [Description("Optional language code for OCR processing, e.g. en.")] string? language = null,
        [Description("OCR output format: text or json.")] string format = "text",
        [Description("If true, return OCR result directly in response when supported by the model/endpoint.")] bool return_result_in_response = false,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ValidateRequest(fileUrl, model, format);

                var settings = serviceProvider.GetRequiredService<deAPISettings>();
                var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
                var downloadService = serviceProvider.GetRequiredService<DownloadService>();

                var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
                var source = files.FirstOrDefault() ?? throw new ValidationException("fileUrl could not be downloaded.");

                using var client = clientFactory.CreateClient();
                using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}{Img2TxtPath}");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));

                using var form = new MultipartFormDataContent();
                var contentType = string.IsNullOrWhiteSpace(source.MimeType) ? "application/octet-stream" : source.MimeType;
                var imageContent = new ByteArrayContent(source.Contents.ToArray());
                imageContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                var sourceName = string.IsNullOrWhiteSpace(source.Filename) ? "input-image.png" : source.Filename;

                form.Add(imageContent, "image", sourceName);
                form.Add(new StringContent(model), "model");
                form.Add(new StringContent(format.Trim().ToLowerInvariant()), "format");
                form.Add(new StringContent(return_result_in_response ? "true" : "false"), "return_result_in_response");

                if (!string.IsNullOrWhiteSpace(language))
                    form.Add(new StringContent(language.Trim()), "language");

                req.Content = form;

                using var resp = await client.SendAsync(req, cancellationToken);
                var json = await resp.Content.ReadAsStringAsync(cancellationToken);

                if (!resp.IsSuccessStatusCode)
                    throw new Exception($"{resp.StatusCode}: {json}");

                return JsonSerializer.Deserialize<object>(json) ?? new { raw = json };
            }));

    private static void ValidateRequest(string fileUrl, string model, string format)
    {
        if (string.IsNullOrWhiteSpace(fileUrl))
            throw new ValidationException("fileUrl is required.");

        if (string.IsNullOrWhiteSpace(model))
            throw new ValidationException("model is required.");

        var normalizedFormat = (format ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedFormat is not ("text" or "json"))
            throw new ValidationException("format must be either 'text' or 'json'.");

    }
}

