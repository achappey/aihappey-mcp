using System.ComponentModel;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.ZAI;

public static class ZAITools
{
    [Description("Read and parse a web page with configurable formatting, cache, and summaries.")]
    [McpServerTool(Title = "Z.AI web reader", Name = "zai_tools_web_reader", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> ZAI_Tools_WebReader(
        [Description("The URL to retrieve.")] string url,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Request timeout in seconds. Default is 20.")] int? timeout = 20,
        [Description("Disable caching (true/false). Default is false.")] bool? noCache = false,
        [Description("Return format (e.g., markdown, text). Default is markdown.")] string? returnFormat = "markdown",
        [Description("Retain images (true/false). Default is true.")] bool? retainImages = true,
        [Description("Disable GitHub Flavored Markdown (true/false). Default is false.")] bool? noGfm = false,
        [Description("Keep image data URLs (true/false). Default is false.")] bool? keepImgDataUrl = false,
        [Description("Include image summary (true/false). Default is false.")] bool? withImagesSummary = false,
        [Description("Include links summary (true/false). Default is false.")] bool? withLinksSummary = false,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var client = serviceProvider.GetRequiredService<ZAIClient>();
                var body = new
                {
                    url,
                    timeout,
                    no_cache = noCache,
                    return_format = returnFormat,
                    retain_images = retainImages,
                    no_gfm = noGfm,
                    keep_img_data_url = keepImgDataUrl,
                    with_images_summary = withImagesSummary,
                    with_links_summary = withLinksSummary
                };

                JsonNode? response = await client.PostAsync("paas/v4/reader", body, null, cancellationToken);
                return response;
            }));

    [Description("Search the web using Z.AI Web Search with LLM-optimized results.")]
    [McpServerTool(Title = "Z.AI web search", Name = "zai_tools_web_search", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> ZAI_Tools_WebSearch(
        [Description("Search query content.")] string searchQuery,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Search engine code. Default is search-prime.")] string searchEngine = "search-prime",
        [Description("Number of results to return (1-50). Default is 10.")] int? count = 10,
        [Description("Whitelist domain filter (e.g., www.example.com).")] string? searchDomainFilter = null,
        [Description("Recency filter: oneDay, oneWeek, oneMonth, oneYear, noLimit. Default is noLimit.")] string? searchRecencyFilter = "noLimit",
        [Description("User-provided unique request identifier.")] string? requestId = null,
        [Description("End user ID for abuse monitoring.")] string? userId = null,
        [Description("Accept-Language header (default en-US,en). Optional.")] string? acceptLanguage = null,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var client = serviceProvider.GetRequiredService<ZAIClient>();
                var body = new
                {
                    search_engine = searchEngine,
                    search_query = searchQuery,
                    count,
                    search_domain_filter = searchDomainFilter,
                    search_recency_filter = searchRecencyFilter,
                    request_id = requestId,
                    user_id = userId
                };

                var headers = string.IsNullOrWhiteSpace(acceptLanguage)
                    ? null
                    : new Dictionary<string, string>
                    {
                        ["Accept-Language"] = acceptLanguage
                    };

                JsonNode? response = await client.PostAsync("paas/v4/web_search", body, headers, cancellationToken);
                return response;
            }));

    [Description("Parse document or image layout using GLM-OCR.")]
    [McpServerTool(Title = "Z.AI layout parsing", Name = "zai_tools_layout_parsing", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> ZAI_Tools_LayoutParsing(
        [Description("Model code. Must be glm-ocr.")] string model,
        [Description("Image or PDF URL/base64 to analyze.")] string file,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Return screenshot info (true/false). Default is false.")] bool? returnCropImages = false,
        [Description("Return detailed layout visualization (true/false). Default is false.")] bool? needLayoutVisualization = false,
        [Description("Start page number for PDF (>=1). Optional.")] int? startPageId = null,
        [Description("End page number for PDF (>=1). Optional.")] int? endPageId = null,
        [Description("Unique request ID.")] string? requestId = null,
        [Description("End user ID for abuse monitoring (6-128 chars). Optional.")] string? userId = null,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var client = serviceProvider.GetRequiredService<ZAIClient>();
                var body = new
                {
                    model,
                    file,
                    return_crop_images = returnCropImages,
                    need_layout_visualization = needLayoutVisualization,
                    start_page_id = startPageId,
                    end_page_id = endPageId,
                    request_id = requestId,
                    user_id = userId
                };

                JsonNode? response = await client.PostAsync("paas/v4/layout_parsing", body, null, cancellationToken);
                return response;
            }));
}
