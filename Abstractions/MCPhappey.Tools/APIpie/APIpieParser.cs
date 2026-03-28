using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.APIpie;

public static class APIpieParser
{
    [Description("Parse a document with APIpie Apache Tika parser and return extracted content and/or metadata.")]
    [McpServerTool(Title = "APIpie parser", Name = "apipie_parse_document", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> APIpie_Parse_Document(
        [Description("File URL of the input document. Secure SharePoint and OneDrive links are supported.")] string fileUrl,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Extract document metadata from APIpie parser response.")] bool metadata = true,
        [Description("Extract textual content from APIpie parser response.")] bool content = true,
        [Description("When true, saves the parser JSON result beside the source file using the same filename plus .LLMs.json when possible, otherwise falls back to the default MCP output location, and returns only a resource link.")] bool saveOutput = false,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
            {
                var result = await ExecuteParseAsync(serviceProvider, requestContext, fileUrl, metadata, content, cancellationToken)
                    ?? new JsonObject();

                if (saveOutput)
                    return await requestContext.SaveOutputAsync(serviceProvider, BinaryData.FromString(result.ToJsonString()), "json", cancellationToken: cancellationToken);

                return new CallToolResult
                {
                    Meta = await requestContext.GetToolMeta(),
                    StructuredContent = result
                };
            });

    private static async Task<JsonNode?> ExecuteParseAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string fileUrl,
        bool metadata,
        bool content,
        CancellationToken cancellationToken)
    {
        var client = serviceProvider.GetRequiredService<APIpieClient>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();

        var files = await downloadService.DownloadContentAsync(
            serviceProvider,
            requestContext.Server,
            fileUrl,
            cancellationToken);

        var file = files.FirstOrDefault()
            ?? throw new Exception("No file found for APIpie parser input.");

        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(file.Contents.ToArray());

        if (!string.IsNullOrWhiteSpace(file.MimeType))
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.MimeType);

        form.Add(fileContent, "file", file.Filename ?? "document.bin");
        form.Add(new StringContent(metadata ? "true" : "false"), "metadata");
        form.Add(new StringContent(content ? "true" : "false"), "content");

        return await client.PostMultipartAsync("v1/parser", form, cancellationToken)
            ?? throw new Exception("APIpie returned no response.");
    }
}

