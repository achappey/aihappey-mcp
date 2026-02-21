using System.ComponentModel;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.APIpie;

public static class APIpieAnon
{
    [Description("Anonymize sensitive entities in text extracted from a file URL (including SharePoint/OneDrive).")]
    [McpServerTool(Title = "APIpie anon from file", Name = "apipie_anon_file", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> APIpie_Anon_File(
        [Description("File URL of the input document. Secure SharePoint and OneDrive links are supported.")] string fileUrl,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var client = serviceProvider.GetRequiredService<APIpieClient>();
                var downloadService = serviceProvider.GetRequiredService<DownloadService>();

                var files = await downloadService.ScrapeContentAsync(
                    serviceProvider,
                    requestContext.Server,
                    fileUrl,
                    cancellationToken);

                var file = files.FirstOrDefault()
                    ?? throw new Exception("No file found for APIpie anon input.");

                var text = file.Contents.ToString();
                if (string.IsNullOrWhiteSpace(text))
                    throw new Exception("No text content could be extracted from fileUrl for APIpie anon.");

                return await client.PostAsync("v1/anon", new { text }, cancellationToken)
                    ?? throw new Exception("APIpie returned no response.");
            }));

    [Description("Anonymize sensitive entities in plain text using APIpie Anon.")]
    [McpServerTool(Title = "APIpie anon from text", Name = "apipie_anon_text", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> APIpie_Anon_Text(
        [Description("Text containing potentially sensitive information to anonymize.")] string text,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                if (string.IsNullOrWhiteSpace(text))
                    throw new Exception("Text is required for APIpie anon.");

                var client = serviceProvider.GetRequiredService<APIpieClient>();

                return await client.PostAsync("v1/anon", new { text }, cancellationToken)
                    ?? throw new Exception("APIpie returned no response.");
            }));
}

