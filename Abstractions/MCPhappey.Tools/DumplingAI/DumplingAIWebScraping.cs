using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.DumplingAI;

public static class DumplingAIWebScraping
{
    [Description("Scrape a URL with DumplingAI and return structured content, markdown, HTML, or other extracted page data.")]
    [McpServerTool(Title = "DumplingAI scrape", Name = "dumplingai_scrape", ReadOnly = true, OpenWorld = true, UseStructuredContent = true)]
    public static async Task<CallToolResult?> DumplingAI_Scrape(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The target URL to scrape.")] string url,
        [Description("Optional output format such as markdown, html, text, or json.")] string? format = null,
        [Description("Optional CSS selector or extraction hint when supported by DumplingAI.")] string? selector = null,
        CancellationToken cancellationToken = default)
        => await ExecuteAsync(
            serviceProvider,
            requestContext,
            "/scrape",
            new JsonObject
            {
                ["url"] = url,
                ["format"] = format,
                ["selector"] = selector
            }.WithoutNulls(),
            cancellationToken,
            $"DumplingAI scrape completed for {url}.");

    [Description("Crawl a website or sitemap with DumplingAI and return captured pages and crawl metadata as structured content.")]
    [McpServerTool(Title = "DumplingAI crawl website", Name = "dumplingai_crawl", ReadOnly = true, OpenWorld = true, UseStructuredContent = true)]
    public static async Task<CallToolResult?> DumplingAI_Crawl(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The root URL or sitemap URL to crawl.")] string url,
        [Description("Optional maximum number of pages to crawl.")][Range(1, int.MaxValue)] int? limit = null,
        [Description("Optional maximum crawl depth.")][Range(0, int.MaxValue)] int? depth = null,
        [Description("Optional output format such as markdown, html, text, or json.")] string? format = null,
        CancellationToken cancellationToken = default)
        => await ExecuteAsync(
            serviceProvider,
            requestContext,
            "/crawl",
            new JsonObject
            {
                ["url"] = url,
                ["limit"] = limit,
                ["depth"] = depth,
                ["format"] = format
            }.WithoutNulls(),
            cancellationToken,
            $"DumplingAI crawl completed for {url}.");

    [Description("Capture a screenshot or PDF rendition of a URL with DumplingAI and return the resulting structured response.")]
    [McpServerTool(Title = "DumplingAI screenshot", Name = "dumplingai_screenshot", ReadOnly = true, OpenWorld = true, UseStructuredContent = true)]
    public static async Task<CallToolResult?> DumplingAI_Screenshot(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The target URL to capture.")] string url,
        [Description("Optional output type such as png, jpeg, or pdf.")] string? outputType = null,
        [Description("Capture the full page when true.")] bool? fullPage = null,
        [Description("Optional viewport width in pixels.")][Range(1, int.MaxValue)] int? width = null,
        [Description("Optional viewport height in pixels.")][Range(1, int.MaxValue)] int? height = null,
        CancellationToken cancellationToken = default)
        => await ExecuteAsync(
            serviceProvider,
            requestContext,
            "/screenshot",
            new JsonObject
            {
                ["url"] = url,
                ["outputType"] = outputType,
                ["fullPage"] = fullPage,
                ["width"] = width,
                ["height"] = height
            }.WithoutNulls(),
            cancellationToken,
            $"DumplingAI screenshot completed for {url}.");

    [Description("Extract structured content from raw text, HTML, a URL, or a document or image file sent as base64 or fileUrl.")]
    [McpServerTool(Title = "DumplingAI extract", Name = "dumplingai_extract", ReadOnly = true, OpenWorld = true, UseStructuredContent = true)]
    public static async Task<CallToolResult?> DumplingAI_Extract(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional URL to fetch and extract from.")] string? url = null,
        [Description("Optional raw text input.")] string? text = null,
        [Description("Optional raw HTML input.")] string? html = null,
        [Description("Optional extractor or schema name.")] string? extractor = null,
        [Description("Optional fileUrl for SharePoint or OneDrive documents when file-based extraction is needed.")] string? fileUrl = null,
        [Description("Optional base64-encoded input file content when file-based extraction is needed.")] string? inputFileBase64 = null,
        [Description("Optional filename for base64 file input.")] string? inputFileName = null,
        [Description("Optional MIME type for base64 file input.")] string? inputFileMimeType = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                DumplingAIHelpers.EnsureExclusiveFileInput(fileUrl, inputFileBase64, "extract");

                if (string.IsNullOrWhiteSpace(url) && string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(html) && string.IsNullOrWhiteSpace(fileUrl) && string.IsNullOrWhiteSpace(inputFileBase64))
                    throw new ValidationException("Provide at least one of url, text, html, fileUrl, or inputFileBase64.");

                var file = await DumplingAIHelpers.BuildOptionalFileObjectAsync(
                    serviceProvider,
                    requestContext,
                    fileUrl,
                    inputFileBase64,
                    inputFileName,
                    inputFileMimeType,
                    cancellationToken);

                var payload = new JsonObject
                {
                    ["url"] = url,
                    ["text"] = text,
                    ["html"] = html,
                    ["extractor"] = extractor,
                    ["file"] = file
                }.WithoutNulls();

                var client = serviceProvider.GetRequiredService<DumplingAIClient>();
                var response = await client.PostAsync("/extract", payload, cancellationToken);
                var structured = DumplingAIHelpers.CreateStructuredResponse("/extract", payload, response);

                return new CallToolResult
                {
                    Meta = await requestContext.GetToolMeta(),
                    StructuredContent = structured,
                    Content = ["DumplingAI extract completed.".ToTextContentBlock()]
                };
            }));

    private static async Task<CallToolResult?> ExecuteAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string endpoint,
        JsonObject payload,
        CancellationToken cancellationToken,
        string summary)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var client = serviceProvider.GetRequiredService<DumplingAIClient>();
                var response = await client.PostAsync(endpoint, payload, cancellationToken);
                var structured = DumplingAIHelpers.CreateStructuredResponse(endpoint, payload, response);

                return new CallToolResult
                {
                    Meta = await requestContext.GetToolMeta(),
                    StructuredContent = structured,
                    Content = [summary.ToTextContentBlock()]
                };
            }));
}
