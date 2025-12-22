using System.ComponentModel;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.Azure.DocumentIntelligence;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Azure.Vision;

public static class AzureVision
{
    [Description("Analyze an image using Azure AI Vision (describe, tags, objects, OCR).")]
    [McpServerTool(Title = "Azure Vision Analyze", ReadOnly = true)]
    public static async Task<CallToolResult?> AzureVision_AnalyzeImageAsync(
      [Description("URL of the image to analyze.")]
        string imageUrl,
      [Description("Optional visual features: Description, Tags, Objects, Faces, Adult, Brands, Categories.")]
        string[]? visualFeatures,
      IServiceProvider serviceProvider,
      RequestContext<CallToolRequestParams> requestContext,
      CancellationToken cancellationToken = default)
      => await requestContext.WithExceptionCheck(async () =>
      await requestContext.WithStructuredContent(async () =>
  {
      var settings = serviceProvider.GetRequiredService<AzureAISettings>();
      var httpClient = serviceProvider.GetRequiredService<HttpClient>();
      var downloadService = serviceProvider.GetRequiredService<DownloadService>();

      // 1) Build query
      var featureQuery = (visualFeatures is { Length: > 0 })
          ? $"?visualFeatures={string.Join(",", visualFeatures)}"
          : "?visualFeatures=Description,Tags,Objects";

      var uri = $"https://{settings.Endpoint}/vision/v4.0/analyze{featureQuery}";

      // 2) Download image
      var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, imageUrl, cancellationToken);
      var file = files.FirstOrDefault() ?? throw new Exception("No image file provided.");

      // 3) Send request as binary
      using var post = new HttpRequestMessage(HttpMethod.Post, uri);
      post.Headers.Add("Ocp-Apim-Subscription-Key", settings.ApiKey);
      post.Content = new ByteArrayContent(file.Contents.ToArray());
      post.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

      using var response = await httpClient.SendAsync(post, cancellationToken);
      var json = await response.Content.ReadAsStringAsync(cancellationToken);

      if (!response.IsSuccessStatusCode)
          throw new Exception($"Vision analyze failed: {json}");

      return JsonNode.Parse(json);
  }));

    [Description("Extract printed text (OCR) from an image using Azure AI Vision Read API.")]
    [McpServerTool(Title = "Azure Vision OCR", ReadOnly = true)]
    public static async Task<CallToolResult?> AzureVision_ReadImageAsync(
        [Description("URL of the image to extract text from.")]
        string imageUrl,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
    {
        var settings = serviceProvider.GetRequiredService<AzureAISettings>();
        var httpClient = serviceProvider.GetRequiredService<HttpClient>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();

        var uri = $"https://{settings.Endpoint}/vision/v4.0/read/analyze";

        var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, imageUrl, cancellationToken);
        var file = files.FirstOrDefault() ?? throw new Exception("No image file provided.");

        using var post = new HttpRequestMessage(HttpMethod.Post, uri);
        post.Headers.Add("Ocp-Apim-Subscription-Key", settings.ApiKey);
        post.Content = new ByteArrayContent(file.Contents.ToArray());
        post.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

        using var postResponse = await httpClient.SendAsync(post, cancellationToken);
        if (!postResponse.IsSuccessStatusCode)
        {
            var error = await postResponse.Content.ReadAsStringAsync(cancellationToken);
            throw new Exception($"Vision OCR start failed: {error}");
        }

        // Operation-Location polling
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
                throw new Exception($"Vision OCR failed: {json}");
        }

        return result;
    }));
}
