using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Upstage;

public static class UpstageInformationExtraction
{
    [Description("Run Upstage Universal Information Extraction. Input uses fileUrl (SharePoint/OneDrive supported). response_format is loaded from responseFormatFileUrl.")]
    [McpServerTool(Name = "upstage_information_extraction_universal", Title = "Upstage universal information extraction", IconSource = UpstageConstants.ICON_SOURCE, ReadOnly = true)]
    public static async Task<CallToolResult?> Upstage_Information_Extraction_Universal(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Document/image file URL (SharePoint/OneDrive/HTTPS).")]
        string fileUrl,
        [Description("File URL containing response_format JSON.")]
        string responseFormatFileUrl,
        [Description("Model alias/version. Default: information-extract.")]
        string model = "information-extract",
        [Description("Extraction mode: standard or enhanced.")]
        string mode = "standard",
        [Description("Include extraction locations in the response.")]
        bool location = false,
        [Description("Location granularity: element, word, or all.")]
        string locationGranularity = "element",
        [Description("Split multi-document files automatically.")]
        bool split = false,
        [Description("Include confidence labels in response.")]
        bool confidence = false,
        [Description("Chunking pages per chunk. 0 disables chunking.")]
        int chunkingPagesPerChunk = 0,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                if (string.IsNullOrWhiteSpace(fileUrl))
                    throw new ArgumentException("fileUrl is required.");
                if (string.IsNullOrWhiteSpace(responseFormatFileUrl))
                    throw new ArgumentException("responseFormatFileUrl is required.");

                var downloadService = serviceProvider.GetRequiredService<DownloadService>();
                var upstage = serviceProvider.GetRequiredService<UpstageClient>();

                var file = await DownloadSingleAsync(serviceProvider, requestContext, downloadService, fileUrl, cancellationToken);
                var responseFormat = await DownloadJsonAsync(serviceProvider, requestContext, downloadService, responseFormatFileUrl, cancellationToken);

                var body = new JsonObject
                {
                    ["model"] = model,
                    ["messages"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["role"] = "user",
                            ["content"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["type"] = "image_url",
                                    ["image_url"] = new JsonObject
                                    {
                                        ["url"] = file.ToDataUri()
                                    }
                                }
                            }
                        }
                    },
                    ["response_format"] = responseFormat,
                    ["mode"] = mode,
                    ["location"] = location,
                    ["location_granularity"] = locationGranularity,
                    ["split"] = split,
                    ["confidence"] = confidence
                };

                if (chunkingPagesPerChunk > 0)
                {
                    body["chunking"] = new JsonObject
                    {
                        ["pages_per_chunk"] = chunkingPagesPerChunk
                    };
                }

                return await upstage.PostJsonAsync("information-extraction", body, cancellationToken);
            }));

    [Description("Generate a response schema using Upstage Schema Generation. Input uses fileUrl (SharePoint/OneDrive supported).")]
    [McpServerTool(Name = "upstage_information_extraction_schema_generation", Title = "Upstage schema generation", IconSource = UpstageConstants.ICON_SOURCE, ReadOnly = true)]
    public static async Task<CallToolResult?> Upstage_Information_Extraction_SchemaGeneration(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Document/image file URL (SharePoint/OneDrive/HTTPS).")]
        string fileUrl,
        [Description("Model alias/version. Default: information-extract.")]
        string model = "information-extract",
        [Description("Optional system instruction to guide schema generation.")]
        string? systemPrompt = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                if (string.IsNullOrWhiteSpace(fileUrl))
                    throw new ArgumentException("fileUrl is required.");

                var downloadService = serviceProvider.GetRequiredService<DownloadService>();
                var upstage = serviceProvider.GetRequiredService<UpstageClient>();
                var file = await DownloadSingleAsync(serviceProvider, requestContext, downloadService, fileUrl, cancellationToken);

                var messages = new JsonArray();

                if (!string.IsNullOrWhiteSpace(systemPrompt))
                {
                    messages.Add(new JsonObject
                    {
                        ["role"] = "system",
                        ["content"] = systemPrompt
                    });
                }

                messages.Add(new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "image_url",
                            ["image_url"] = new JsonObject
                            {
                                ["url"] = file.ToDataUri()
                            }
                        }
                    }
                });

                var body = new JsonObject
                {
                    ["model"] = model,
                    ["messages"] = messages
                };

                return await upstage.PostJsonAsync("information-extraction/schema-generation", body, cancellationToken);
            }));

    [Description("Run Upstage Prebuilt Extraction. Input uses fileUrl (SharePoint/OneDrive supported).")]
    [McpServerTool(Name = "upstage_information_extraction_prebuilt", Title = "Upstage prebuilt extraction", IconSource = UpstageConstants.ICON_SOURCE, ReadOnly = true)]
    public static async Task<CallToolResult?> Upstage_Information_Extraction_Prebuilt(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Document file URL (SharePoint/OneDrive/HTTPS).")]
        string fileUrl,
        [Description("Prebuilt extraction model, e.g. receipt-extraction.")]
        string model,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                if (string.IsNullOrWhiteSpace(fileUrl))
                    throw new ArgumentException("fileUrl is required.");
                if (string.IsNullOrWhiteSpace(model))
                    throw new ArgumentException("model is required.");

                var downloadService = serviceProvider.GetRequiredService<DownloadService>();
                var upstage = serviceProvider.GetRequiredService<UpstageClient>();
                var file = await DownloadSingleAsync(serviceProvider, requestContext, downloadService, fileUrl, cancellationToken);

                using var form = new MultipartFormDataContent();

                var fileContent = new ByteArrayContent(file.Contents.ToArray());
                fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.MimeType);
                form.Add(fileContent, "document", file.Filename ?? "document.bin");
                form.Add(new StringContent(model), "model");

                using var req = new HttpRequestMessage(HttpMethod.Post, "information-extraction") { Content = form };
                return await upstage.SendAsync(req, cancellationToken);
            }));

    private static async Task<MCPhappey.Common.Models.FileItem> DownloadSingleAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        DownloadService downloadService,
        string url,
        CancellationToken cancellationToken)
    {
        var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, url, cancellationToken);
        return files.FirstOrDefault() ?? throw new Exception($"No file content could be downloaded from: {url}");
    }

    private static async Task<JsonNode> DownloadJsonAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        DownloadService downloadService,
        string fileUrl,
        CancellationToken cancellationToken)
    {
        var file = await DownloadSingleAsync(serviceProvider, requestContext, downloadService, fileUrl, cancellationToken);
        var json = file.Contents.ToString();
        return JsonNode.Parse(json) ?? throw new Exception($"JSON file at {fileUrl} is empty or invalid.");
    }
}

