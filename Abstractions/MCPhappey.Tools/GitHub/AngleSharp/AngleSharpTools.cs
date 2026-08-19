using System.ComponentModel;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using MCPhappey.Core.Services;
using AngleSharp;

namespace MCPhappey.Tools.GitHub.AngleSharp;

public static class AngleSharpTools
{
    [Description("Download HTML from a URL and extract text using a CSS selector.")]
    [McpServerTool(Name = "anglesharp_select_from_url", ReadOnly = true)]
    public static async Task<CallToolResult?> AngleSharp_SelectFromUrl(
      RequestContext<CallToolRequestParams> requestContext,
      IServiceProvider serviceProvider,
      [Description("The target URL to download and parse")] string url,
      [Description("CSS selector for the elements to extract")] string selector,
      CancellationToken cancellationToken = default)
      => await ModelContextToolExtensions.WithExceptionCheck(async () =>
       await requestContext.WithStructuredContent(async () =>
  {
      // Download HTML via your existing service (stream-based)
      var downloadService = serviceProvider.GetRequiredService<DownloadService>();
      var downloads = await downloadService.DownloadContentAsync(
          serviceProvider,
          requestContext.Server,
          url,
          cancellationToken);

      var content = downloads.FirstOrDefault()?.Contents.ToString();
      if (string.IsNullOrWhiteSpace(content))
          throw new InvalidOperationException($"No HTML content downloaded from: {url}");

      // Parse HTML using AngleSharp
      var config = Configuration.Default;
      var context = BrowsingContext.New(config);
      var document = await context.OpenAsync(req => req.Content(content));

      // Query elements
      var elements = document.QuerySelectorAll(selector);
      var results = elements
          .Select(e => e.TextContent.Trim())
          .Where(t => !string.IsNullOrWhiteSpace(t))
          .ToList();

      return new
      {
          query = selector,
          results
      };
  }));

    [Description("Download HTML from a URL and extract elements with selected attributes using a CSS selector.")]
    [McpServerTool(
      Title = "Extract HTML elements",
      Name = "anglesharp_extract_elements_from_url",
      ReadOnly = true,
      OpenWorld = false)]
    public static async Task<CallToolResult?> AngleSharp_ExtractElementsFromUrl(
      RequestContext<CallToolRequestParams> requestContext,
      IServiceProvider serviceProvider,
      [Description("The target URL to download and parse")] string url,
      [Description("CSS selector for the elements to extract")] string selector,
      [Description("Optional attribute names to return, e.g. href, src, title")] string[]? attributes = null,
      CancellationToken cancellationToken = default)
      => await ModelContextToolExtensions.WithExceptionCheck(async () =>
          await requestContext.WithStructuredContent(async () =>
          {
              var downloadService = serviceProvider.GetRequiredService<DownloadService>();

              var downloads = await downloadService.DownloadContentAsync(
                  serviceProvider,
                  requestContext.Server,
                  url,
                  cancellationToken);

              var content = downloads.FirstOrDefault()?.Contents.ToString();

              if (string.IsNullOrWhiteSpace(content))
                  throw new InvalidOperationException(
                      $"No HTML content downloaded from: {url}");

              var context = BrowsingContext.New(Configuration.Default);
              var document = await context.OpenAsync(req => req.Content(content));

              var results = document.QuerySelectorAll(selector)
                  .Select(element => new
                  {
                      tag = element.TagName.ToLowerInvariant(),
                      text = element.TextContent.Trim(),
                      attributes = attributes?
                          .Where(name => element.HasAttribute(name))
                          .ToDictionary(
                              name => name,
                              name => element.GetAttribute(name))
                  })
                  .ToList();

              return new
              {
                  query = selector,
                  count = results.Count,
                  results
              };
          }));

    [Description("Extracts links from an HTML page, including link text and target URL.")]
    [McpServerTool(
Title = "Extract links",
Name = "anglesharp_extract_links",
ReadOnly = true,
OpenWorld = false)]
    public static async Task<CallToolResult?> AngleSharp_ExtractLinks(
RequestContext<CallToolRequestParams> requestContext,
IServiceProvider serviceProvider,
[Description("The target URL")] string url,
CancellationToken cancellationToken = default)
=> await ModelContextToolExtensions.WithExceptionCheck(async () =>
    await requestContext.WithStructuredContent(async () =>
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();

        var downloads = await downloadService.DownloadContentAsync(
            serviceProvider,
            requestContext.Server,
            url,
            cancellationToken);

        var content = downloads.FirstOrDefault()?.Contents.ToString();

        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException(
                $"No HTML content downloaded from: {url}");

        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(content));

        var results = document.QuerySelectorAll("a[href]")
            .Select(a => new
            {
                text = a.TextContent.Trim(),
                href = a.GetAttribute("href")
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.href))
            .ToList();

        return new
        {
            count = results.Count,
            results
        };
    }));
}


