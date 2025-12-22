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
      => await requestContext.WithExceptionCheck(async () =>
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
}


