using System.ComponentModel;
using System.Text.Json.Nodes;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Upstage;

public static class UpstageDocumentClassification
{
    [Description("Classify a document using Upstage Document Classification. Uses fileUrl (SharePoint/OneDrive supported) and response format from responseFormatFileUrl.")]
    [McpServerTool(Name = "upstage_document_classification", Title = "Upstage document classification", IconSource = UpstageConstants.ICON_SOURCE, ReadOnly = true)]
    public static async Task<CallToolResult?> Upstage_Document_Classification(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Document file URL (SharePoint/OneDrive/HTTPS).")]
        string fileUrl,
        [Description("File URL containing response_format JSON.")]
        string responseFormatFileUrl,
        [Description("Model alias/version. Default: document-classify.")]
        string model = "document-classify",
        [Description("Split the input when document type boundaries are detected.")]
        bool split = false,
        [Description("Optional file URL containing split_criteria JSON array.")]
        string? splitCriteriaFileUrl = null,
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

                var inputFile = await DownloadSingleAsync(serviceProvider, requestContext, downloadService, fileUrl, cancellationToken);
                var responseFormat = await DownloadJsonAsync(serviceProvider, requestContext, downloadService, responseFormatFileUrl, cancellationToken);

                JsonNode? splitCriteria = null;
                if (!string.IsNullOrWhiteSpace(splitCriteriaFileUrl))
                    splitCriteria = await DownloadJsonAsync(serviceProvider, requestContext, downloadService, splitCriteriaFileUrl!, cancellationToken);

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
                                        ["url"] = inputFile.ToDataUri()
                                    }
                                }
                            }
                        }
                    },
                    ["response_format"] = responseFormat,
                    ["split"] = split
                };

                if (splitCriteria is not null)
                    body["split_criteria"] = splitCriteria;

                return await upstage.PostJsonAsync("document-classification", body, cancellationToken);
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

