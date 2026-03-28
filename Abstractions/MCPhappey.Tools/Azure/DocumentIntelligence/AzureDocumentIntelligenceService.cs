using System.ComponentModel;
using System.Text;
using System.Text.Json.Nodes;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Azure.DocumentIntelligence;

public static class AzureDocumentIntelligence
{
    [Description("Analyze a document or image with Azure Document Intelligence (OCR / prebuilt-read).")]
    [McpServerTool(Title = "Azure Document Intelligence OCR", ReadOnly = true)]
    public static async Task<CallToolResult?> AzureDocumentIntelligence_AnalyzeDocument(
        [Description("URL of the document to analyze.")]
        string documentUrl,
        [Description("Optional feature flags (e.g., ocr.highResolution, ocr.font, ocr.formula).")]
        string[]? features,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("When true, uploads the JSON analysis result and returns only a resource link instead of inline JSON.")]
        bool saveOutput = false,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
    {
        var result = await AnalyzeDocumentAsync(
            serviceProvider,
            requestContext,
            documentUrl,
            "prebuilt-read",
            features,
            "Document analysis",
            cancellationToken);

        if (saveOutput)
        {
            var uploadName = requestContext.ToOutputFileName("json");
            var uploaded = await requestContext.Server.Upload(
                serviceProvider,
                uploadName,
                BinaryData.FromString(result?.ToJsonString() ?? "{}"),
                cancellationToken);

            return uploaded?.ToResourceLinkCallToolResponse();
        }

        return new CallToolResult
        {
            Meta = await requestContext.GetToolMeta(),
            StructuredContent = result ?? new JsonObject()
        };
    });

    [Description("Analyze a document or image with Azure Document Intelligence (OCR / prebuilt-read), always save the JSON result, and optionally store it directly in a SharePoint or OneDrive folder.")]
    [McpServerTool(Title = "Azure Document Intelligence OCR Save", ReadOnly = true)]
    public static async Task<CallToolResult?> AzureDocumentIntelligence_AnalyzeDocumentSave(
        [Description("URL of the document to analyze.")]
        string documentUrl,
        [Description("Optional feature flags (e.g., ocr.highResolution, ocr.font, ocr.formula).")]
        string[]? features,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional SharePoint or OneDrive folder URL to store the JSON result in directly. When omitted, the default MCP output location is used.")]
        string? folderUrl = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
    {
        var result = await AnalyzeDocumentAsync(
            serviceProvider,
            requestContext,
            documentUrl,
            "prebuilt-read",
            features,
            "Document analysis",
            cancellationToken);

        var uploadName = requestContext.ToOutputFileName("json");
        var uploadContent = BinaryData.FromString(result?.ToJsonString() ?? "{}");

        if (!string.IsNullOrWhiteSpace(folderUrl))
        {
            using var graphClient = await serviceProvider.GetOboGraphClient(requestContext.Server);
            var uploadedToFolder = await graphClient.UploadToFolder(folderUrl, uploadName, uploadContent, cancellationToken);
            return uploadedToFolder?.ToResourceLinkCallToolResponse();
        }

        var uploaded = await requestContext.Server.Upload(
            serviceProvider,
            uploadName,
            uploadContent,
            cancellationToken);

        return uploaded?.ToResourceLinkCallToolResponse();
    });

    [Description("Extract text, tables, and structure from a document using Azure Document Intelligence (prebuilt-layout).")]
    [McpServerTool(Title = "Azure Document Intelligence Layout", ReadOnly = true)]
    public static async Task<CallToolResult?> AzureDocumentIntelligence_AnalyzeLayoutAsync(
        [Description("URL of the document to analyze.")]
        string documentUrl,
        [Description("Optional feature flags (e.g., ocr.highResolution, ocr.font, ocr.formula).")]
        string[]? features,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
{
    return await AnalyzeDocumentAsync(
        serviceProvider,
        requestContext,
        documentUrl,
        "prebuilt-layout",
        features,
        "Layout analysis",
        cancellationToken);
}));

    private static async Task<JsonNode?> AnalyzeDocumentAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string documentUrl,
        string modelId,
        string[]? features,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        var settings = serviceProvider.GetRequiredService<AzureAISettings>();
        var httpClient = serviceProvider.GetRequiredService<HttpClient>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();

        var featureQuery = (features is { Length: > 0 })
            ? $"?features={string.Join(",", features)}&api-version=2024-11-30"
            : "?api-version=2024-11-30";

        var uri = $"https://{settings.Endpoint}/documentintelligence/documentModels/{modelId}:analyze{featureQuery}";

        var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, documentUrl, cancellationToken);
        var file = files.FirstOrDefault() ?? throw new Exception("No file provided.");

        var payload = new JsonObject
        {
            ["base64Source"] = Convert.ToBase64String(file.Contents.ToArray())
        };

        using var post = new HttpRequestMessage(HttpMethod.Post, uri);
        post.Headers.Add("Ocp-Apim-Subscription-Key", settings.ApiKey);
        post.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, MimeTypes.Json);

        using var postResponse = await httpClient.SendAsync(post, cancellationToken);
        if (!postResponse.IsSuccessStatusCode)
        {
            var error = await postResponse.Content.ReadAsStringAsync(cancellationToken);
            throw new Exception($"{operationName} failed: {error}");
        }

        if (!postResponse.Headers.TryGetValues("Operation-Location", out var opLocValues))
            throw new Exception("Operation-Location header missing from response.");

        var operationUrl = opLocValues.First();

        JsonNode? result = null;
        while (true)
        {
            await Task.Delay(1000, cancellationToken);
            using var getReq = new HttpRequestMessage(HttpMethod.Get, operationUrl);
            getReq.Headers.Add("Ocp-Apim-Subscription-Key", settings.ApiKey);

            using var getRes = await httpClient.SendAsync(getReq, cancellationToken);
            var json = await getRes.Content.ReadAsStringAsync(cancellationToken);
            result = JsonNode.Parse(json);

            var status = result?["status"]?.ToString();
            if (status == "succeeded")
                break;
            if (status == "failed")
                throw new Exception($"{operationName} failed: {json}");
        }

        return result;
    }


}

public class AzureAISettings
{
    public string ApiKey { get; set; } = default!;
    public string Endpoint { get; set; } = default!;
}
