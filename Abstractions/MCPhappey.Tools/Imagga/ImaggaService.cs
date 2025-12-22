using System.ComponentModel;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Imagga;

public static class ImaggaService
{

    [Description("Automatically generate descriptive tags for an image using Imagga’s Auto-Tagging API.")]
    [McpServerTool(
       Title = "Imagga auto-tagging",
       Idempotent = false,
       OpenWorld = true,
       Destructive = false,
       ReadOnly = true)]
    public static async Task<CallToolResult?> Imagga_AutoTagImage(
       [Description("URL of the image file to tag.")] string fileUrl,
       IServiceProvider serviceProvider,
       RequestContext<CallToolRequestParams> requestContext,
       [Description("Optional tagger id for custom tagging models.")] string? taggerId = null,
       [Description("Language of returned tags (default: en).")] string? language = null,
       [Description("Include detailed tag info (0 or 1, default: 0).")] int? verbose = null,
       [Description("Limit the number of returned tags (-1 = all).")] int? limit = null,
       [Description("Confidence threshold for tag filtering (default: 0.0).")] double? threshold = null,
       [Description("Decrease confidence for parent tags (1=yes,0=no).")] int? decreaseParents = null,
       CancellationToken cancellationToken = default)
       => await requestContext.WithExceptionCheck(async () =>
       await requestContext.WithStructuredContent(async () =>
   {
       var imagga = serviceProvider.GetRequiredService<ImaggaClient>();
       var downloadService = serviceProvider.GetRequiredService<DownloadService>();

       // Download and encode image
       var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
       var image = files.FirstOrDefault();

       if (image == null || image.Contents == null)
           throw new InvalidOperationException("Failed to download or read image from the provided URL.");

       string base64Image = Convert.ToBase64String(image.Contents.ToArray());

       // Prepare request body
       var payload = new Dictionary<string, object?>
       {
           ["image_base64"] = base64Image,
           ["language"] = language ?? "en",
           ["verbose"] = verbose,
           ["limit"] = limit,
           ["threshold"] = threshold,
           ["decrease_parents"] = decreaseParents
       };

       // Send request
       using var resp = await imagga.AutoTagAsync(payload, taggerId, cancellationToken);
       resp.EnsureSuccessStatusCode();

       // Parse response
       var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
       return await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken);
   }));

    [Description("Extract dominant colors, palettes, and foreground/background separation from an image using Imagga API.")]
    [McpServerTool(
        Title = "Imagga color amalysis",
        Idempotent = false,
        OpenWorld = true,
        Destructive = false,
        ReadOnly = true)]
    public static async Task<CallToolResult?> Imagga_AnalyzeColors(
        [Description("URL of the image file to analyze.")] string fileUrl,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Extract overall image colors (1 = yes, 0 = no, default: 1).")] int? extractOverallColors = null,
        [Description("Extract object and non-object colors separately (1 = yes, 0 = no, default: 1).")] int? extractObjectColors = null,
        [Description("Number of overall image colors to extract (default: 5).")] int? overallCount = null,
        [Description("Number of separated colors (foreground/background) to extract (default: 3).")] int? separatedCount = null,
        [Description("Use deterministic algorithm (0 or 1).")] int? deterministic = null,
        [Description("Optional index name to save image for future search. Requires deterministic=1.")] string? saveIndex = null,
        [Description("Optional ID to associate the image in search index.")] string? saveId = null,
        [Description("Features type to extract (overall or object).")] string? featuresType = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();

        // Download image and convert to Base64
        var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
        var image = files.FirstOrDefault();

        if (image == null || image.Contents == null)
            throw new InvalidOperationException("Failed to download or read image from the provided URL.");

        string base64Image = Convert.ToBase64String(image.Contents.ToArray());

        // Build payload
        var payload = new Dictionary<string, object?>
        {
            ["image_base64"] = base64Image,
            ["extract_overall_colors"] = extractOverallColors,
            ["extract_object_colors"] = extractObjectColors,
            ["overall_count"] = overallCount,
            ["separated_count"] = separatedCount,
            ["deterministic"] = deterministic,
            ["save_index"] = saveIndex,
            ["save_id"] = saveId,
            ["features_type"] = featuresType
        };

        var imagga = serviceProvider.GetRequiredService<ImaggaClient>();
        using var resp = await imagga.AnalyzeColorsAsync(payload, cancellationToken);
        resp.EnsureSuccessStatusCode();

        // Parse result
        var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
        var json = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken);

        return json;
    }));

    [Description("Categorize an image using Imagga API with a given categorizer.")]
    [McpServerTool(
        Title = "Imagga categorizer",
        Idempotent = false,
        OpenWorld = true,
        Destructive = false,
        ReadOnly = true)]
    public static async Task<CallToolResult?> Imagga_Categorize(
        [Description("Id of the categorizer to use (e.g. 'personal_photos').")]
        string categorizerId,
        [Description("Url of the image file to categorize.")]
        string fileUrl,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional language code (default: en).")]
        string? language = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
    {
        // Resolve dependencies
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();

        // Download image and convert to Base64
        var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
        var image = files.FirstOrDefault();

        if (image == null || image.Contents == null)
            throw new InvalidOperationException("Failed to download or read image from the provided URL.");

        string base64Image = Convert.ToBase64String(image.Contents.ToArray());

        // Prepare request payload
        var payload = new Dictionary<string, object?>
        {
            ["image_base64"] = base64Image,
            ["language"] = language ?? "en"
        };

        // Construct the request
        var imagga = serviceProvider.GetRequiredService<ImaggaClient>();
        using var resp = await imagga.CategorizeAsync(payload, categorizerId, cancellationToken);
        resp.EnsureSuccessStatusCode();

        // Parse JSON response
        var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);

        return await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken);
    }));

    [Description("Analyze an image using Imagga Smart Cropping and return suggested crop coordinates.")]
    [McpServerTool(
       Title = "Imagga smart cropping",
       Idempotent = false,
       OpenWorld = true,
       Destructive = false,
       ReadOnly = true)]
    public static async Task<CallToolResult?> Imagga_CropImage(
       [Description("Url of the image file to analyze.")] string fileUrl,
       IServiceProvider serviceProvider,
       RequestContext<CallToolRequestParams> requestContext,
       [Description("Target resolution pair in format WIDTHxHEIGHT (e.g., '500x300'). Optional.")] string? resolution = null,
       [Description("If 1, coordinates exactly match resolution without scaling. Default: 0")] int? noScaling = null,
       [Description("Minimum visual area percentage to preserve (-1 lets the API decide).")] double? rectPercentage = null,
       [Description("Return raster image result (1 = yes, default 0 = coordinates only).")] int? imageResult = null,
       CancellationToken cancellationToken = default) =>
       await requestContext.WithExceptionCheck(async () =>
       await requestContext.WithStructuredContent(async () =>
   {
       // Resolve dependencies
       var downloadService = serviceProvider.GetRequiredService<DownloadService>();

       // Download the image
       var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
       var image = files.FirstOrDefault();

       if (image == null || image.Contents == null)
           throw new InvalidOperationException("Failed to download or read image from the provided URL.");

       // Convert image to Base64
       string base64Image = Convert.ToBase64String(image.Contents.ToArray());

       // Build payload
       var payload = new Dictionary<string, object?>
       {
           ["image_base64"] = base64Image,
           ["resolution"] = resolution,
           ["no_scaling"] = noScaling,
           ["rect_percentage"] = rectPercentage,
           ["image_result"] = imageResult
       };

       // Prepare request
       var imagga = serviceProvider.GetRequiredService<ImaggaClient>();
       using var resp = await imagga.SmartCropAsync(payload, cancellationToken);
       resp.EnsureSuccessStatusCode();

       // Parse response
       var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
       var json = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken);

       return json;
   }));
}
