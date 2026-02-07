using System.ComponentModel;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.GreenPT;

public static class GreenPTScraper
{
    [Description("Scrape a public webpage and return clean AI-ready content from GreenPT in markdown, html, rawHtml, and/or links formats.")]
    [McpServerTool(Title = "GreenPT scrape webpage", Name = "greenpt_scraper_scrape", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> GreenPT_Scraper_Scrape(
        [Description("The public URL to scrape. Must start with http:// or https://.")] string url,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional output formats. Allowed values: markdown, html, rawHtml, links. Default is [markdown].")] List<string>? formats = null,
        [Description("When true, excludes headers, navigation, and footers from output.")] bool? onlyMainContent = null,
        [Description("Optional HTML tags to include in output.")] List<string>? includeTags = null,
        [Description("Optional HTML tags to exclude from output.")] List<string>? excludeTags = null,
        [Description("Optional delay in milliseconds before fetching content.")] int? waitFor = null,
        [Description("When true, emulate a mobile device while scraping.")] bool? mobile = null,
        [Description("When true, skip TLS certificate verification.")] bool? skipTlsVerification = null,
        [Description("Optional request timeout in milliseconds. Maximum is 300000.")] int? timeout = null,
        [Description("When true, remove base64-encoded images from output.")] bool? removeBase64Images = null,
        [Description("When true, enable ad blocking and cookie popup removal.")] bool? blockAds = null,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var client = serviceProvider.GetRequiredService<GreenPTClient>();

                var body = new
                {
                    url,
                    formats,
                    onlyMainContent,
                    includeTags,
                    excludeTags,
                    waitFor,
                    mobile,
                    skipTlsVerification,
                    timeout,
                    removeBase64Images,
                    blockAds
                };

                return await client.PostAsync("v1/tools/crawl/scrape", body, cancellationToken)
                    ?? throw new Exception("GreenPT returned no response.");
            }));
}

