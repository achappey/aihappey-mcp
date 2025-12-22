using System.ComponentModel;
using HtmlAgilityPack;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using HtmlAgilityPack.CssSelectors.NetCore;
using System.Text.Json;
using System.Text;
using Microsoft.KernelMemory.Pipeline;
using MCPhappey.Core.Extensions;

namespace MCPhappey.Tools.HTTP;

public static class HTTPService
{
    [Description("Fetches a public accessible url.")]
    [McpServerTool(Title = "Fetch public URL",
        Idempotent = true,
        OpenWorld = true,
        ReadOnly = true)]
    public static async Task<CallToolResult?> Http_FetchUrl(
        [Description("The url to fetch.")]
        string url,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var content = await downloadService.ScrapeContentAsync(serviceProvider, requestContext.Server, url,
            cancellationToken) ?? throw new Exception();

        var contentBlocks = content.ToContentBlocks();

        return contentBlocks.ToCallToolResult();
    });

    [Description("Fetches raw HTML from a public accessible url.")]
    [McpServerTool(Title = "Fetch raw HTML from public URL",
        Idempotent = true,
        OpenWorld = true,
        ReadOnly = true)]
    public static async Task<IEnumerable<ContentBlock>> Http_FetchHtml(
        [Description("The url to fetch.")]
        string url,
        IServiceProvider serviceProvider,
        McpServer mcpServer,
        [Description("Css selector.")]
        string? cssSelector = null,
        [Description("Extract only a specific HTML attribute from matched nodes (e.g., href, src, value).")]
        string? attribute = null,
        [Description("Return only the inner text without HTML tags.")]
        bool textOnly = false,
        [Description("Limit the number of matches returned.")]
        int? maxMatches = null,
        CancellationToken cancellationToken = default)
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();

        var content = await downloadService.DownloadContentAsync(serviceProvider, mcpServer, url,
            cancellationToken) ?? throw new Exception();

        // Geen selector: hele pagina teruggeven als 1 HTML-resource
        if (string.IsNullOrWhiteSpace(cssSelector))
        {
            return content.ToContentBlocks();
        }

        List<string> results = [];

        foreach (var item in content.Where(a => a.MimeType == "text/html"))
        {
            // Met CSS-selector: matches parsen met HtmlAgilityPack + Fizzler
            var doc = new HtmlDocument
            {
                OptionFixNestedTags = true,
                OptionAutoCloseOnEnd = true
            };

            doc.LoadHtml(item.Contents.ToString());

            var matches = doc.DocumentNode.QuerySelectorAll(cssSelector?.Trim());
            if (matches == null || !matches.Any())
                return [];

            if (maxMatches.HasValue)
                matches = [.. matches.Take(maxMatches.Value)];

            foreach (var match in matches)
            {
                string output;
                if (!string.IsNullOrWhiteSpace(attribute))
                {
                    output = match.GetAttributeValue(attribute, string.Empty);
                }
                else if (textOnly)
                {
                    output = match.InnerText;
                }
                else
                {
                    output = match.OuterHtml;
                }

                if (!string.IsNullOrEmpty(output))
                    results.Add(output);
            }
        }

        return [string.Join("\n", results).ToTextContentBlock()];
    }

    [Description("Sends an HTTP POST request to a public URL with optional headers and body.")]
    [McpServerTool(
      Title = "POST to public URL",
      Idempotent = false,
      OpenWorld = true,
      ReadOnly = false)]
    public static async Task<IEnumerable<ContentBlock>> Http_PostUrl(
      [Description("The URL to send the POST request to.")]
        string url,
      [Description("Body to send in the POST request (JSON or raw string).")]
        string? body = null,
      [Description("Optional headers as JSON (e.g., {\"Authorization\":\"Bearer xyz\"}).")]
        string? headers = null,
      [Description("MIME type of the body content (default: application/json).")]
        string? contentType = "application/json",
      [Description("If true, extracts readable text or JSON instead of raw HTML.")]
        bool parseResponse = true,
      CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient();

        // Apply headers if provided
        if (!string.IsNullOrWhiteSpace(headers))
        {
            try
            {
                var headerDict = JsonSerializer.Deserialize<Dictionary<string, string>>(headers);
                if (headerDict != null)
                {
                    foreach (var (key, value) in headerDict)
                        client.DefaultRequestHeaders.TryAddWithoutValidation(key, value);
                }
            }
            catch
            {
                // ignore invalid header JSON
            }
        }

        // Prepare content
        HttpContent? httpContent = null;
        if (!string.IsNullOrEmpty(body))
        {
            httpContent = new StringContent(body, Encoding.UTF8, contentType);
        }

        using var response = await client.PostAsync(url, httpContent, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        // Convert response based on content-type
        if (!parseResponse)
            return [responseBody.ToTextContentBlock()];

        var mime = response.Content.Headers.ContentType?.MediaType ?? "text/plain";

        if (mime.Contains(MimeTypes.Json, StringComparison.OrdinalIgnoreCase))
        {
            var formatted = responseBody.TryFormatJson();
            return [formatted.ToTextContentBlock()];
        }

        if (mime.Contains("text/html", StringComparison.OrdinalIgnoreCase))
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(responseBody);
            var text = doc.DocumentNode.InnerText.Trim();
            return [text.ToTextContentBlock()];
        }

        return [responseBody.ToTextContentBlock()];
    }

    private static string TryFormatJson(this string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return json;
        }
    }
}

