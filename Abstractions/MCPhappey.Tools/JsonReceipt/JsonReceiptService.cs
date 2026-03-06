using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.JsonReceipt;

public static class JsonReceiptService
{
    [Description("Process a receipt synchronously with JsonReceipt using fileUrl input (supports SharePoint/OneDrive/HTTPS) and return structured JSON extraction output.")]
    [McpServerTool(
        Title = "JsonReceipt process receipt",
        Name = "jsonreceipt_process_receipt",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> JsonReceipt_Process(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Receipt file URL (SharePoint/OneDrive/HTTP) to process. Supported formats: JPEG, PNG, WEBP, PDF.")] string fileUrl,
        [Description("Response format: json or xml. Default: json.")] string format = "json",
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ValidateRequest(fileUrl, format);

                var client = serviceProvider.GetRequiredService<JsonReceiptClient>();
                var downloadService = serviceProvider.GetRequiredService<DownloadService>();

                var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
                var source = files.FirstOrDefault() ?? throw new ValidationException("fileUrl could not be downloaded.");

                var payload = new JsonObject
                {
                    ["image"] = Convert.ToBase64String(source.Contents.ToArray()),
                    ["format"] = format.Trim().ToLowerInvariant()
                };

                using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/receipts/process")
                {
                    Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
                };

                return await client.SendAsync(request, cancellationToken);
            }));

    private static void ValidateRequest(string fileUrl, string format)
    {
        if (string.IsNullOrWhiteSpace(fileUrl))
            throw new ValidationException("fileUrl is required.");

        var normalizedFormat = (format ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedFormat is not ("json" or "xml"))
            throw new ValidationException("format must be either 'json' or 'xml'.");
    }
}

