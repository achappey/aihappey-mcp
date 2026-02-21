using System.ComponentModel;
using System.Net.Http.Headers;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.StabilityAI.Models;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.StabilityAI;

public static class StabilityAI3DService
{
    private const string BASE_URL = "https://api.stability.ai/v2beta/3d";

    [Description("Generate a 3D GLB model from an image with Stability Fast 3D")]
    [McpServerTool(Title = "Generate 3D model with Stability Fast 3D",
        Name = "stabilityai_3d_generation_create_fast3d",
        Destructive = false)]
    public static async Task<CallToolResult?> StabilityAI_3DGeneration_Create_Fast3D(
        [Description("Source image url (supports SharePoint/OneDrive)")] string fileUrl,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Output filename without extension")] string? filename = null,
        [Description("Texture resolution: 512, 1024, 2048")] string textureResolution = "1024",
        [Description("Foreground ratio (0.1 – 1.0)")] double? foregroundRatio = 0.85,
        [Description("Optional remeshing algorithm: none, quad, triangle")] string remesh = "none",
        [Description("Approximate vertex count (-1 for auto)")] int vertexCount = -1,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileUrl);

        var downloader = serviceProvider.GetRequiredService<DownloadService>();
        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var settings = serviceProvider.GetRequiredService<StabilityAISettings>();

        var items = await downloader.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
        var file = items.FirstOrDefault() ?? throw new Exception("Could not download image file");

        using var client = clientFactory.CreateClient();
        using var form = new MultipartFormDataContent
        {
            "image".NamedFile(file.Contents.ToArray(), file.Filename ?? "input.png", file.MimeType),
            "texture_resolution".NamedField(textureResolution),
            "foreground_ratio".NamedField(foregroundRatio?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "0.85"),
            "remesh".NamedField(remesh),
            "vertex_count".NamedField(vertexCount.ToString())
        };

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("model/gltf-binary"));

        using var resp = await client.PostAsync($"{BASE_URL}/stable-fast-3d", form, cancellationToken);
        var bytesOut = await resp.Content.ReadAsByteArrayAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"{resp.StatusCode}: {System.Text.Encoding.UTF8.GetString(bytesOut)}");

        var outputName = (filename ?? requestContext.ToOutputFileName()) + ".glb";
        var uploaded = await requestContext.Server.Upload(
            serviceProvider,
            outputName,
            BinaryData.FromBytes(bytesOut),
            cancellationToken) ?? throw new Exception("Upload failed");

        return new EmbeddedResourceBlock()
        {
            Resource = new BlobResourceContents()
            {
                Blob = bytesOut,
                Uri = uploaded.Uri,
                MimeType = "model/gltf-binary"
            }
        }.ToCallToolResult();
    });


    [Description("Generate a 3D GLB model from an image with Stability Point Aware 3D")]
    [McpServerTool(Title = "Generate 3D model with Stability Point Aware 3D",
        Name = "stabilityai_3d_generation_create_pointaware3d",
        Destructive = false)]
    public static async Task<CallToolResult?> StabilityAI_3DGeneration_Create_PointAware3D(
        [Description("Source image url (supports SharePoint/OneDrive)")] string fileUrl,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Output filename without extension")] string? filename = null,
        [Description("Texture resolution: 512, 1024, 2048")] string textureResolution = "1024",
        [Description("Foreground ratio (1 – 2)")] double? foregroundRatio = 1.3,
        [Description("Remesh algorithm: none, quad, triangle")] string remesh = "none",
        [Description("Target type: none, vertex, face")] string targetType = "none",
        [Description("Target count (100 – 20000)")] int targetCount = 1000,
        [Description("Guidance scale (1 – 10)")] double guidanceScale = 3,
        [Description("Seed (0 for random)")] long seed = 0,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileUrl);

        var downloader = serviceProvider.GetRequiredService<DownloadService>();
        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var settings = serviceProvider.GetRequiredService<StabilityAISettings>();

        var items = await downloader.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
        var file = items.FirstOrDefault() ?? throw new Exception("Could not download image file");

        using var client = clientFactory.CreateClient();
        using var form = new MultipartFormDataContent
        {
            "image".NamedFile(file.Contents.ToArray(), file.Filename ?? "input.png", file.MimeType),
            "texture_resolution".NamedField(textureResolution),
            "foreground_ratio".NamedField(foregroundRatio?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "1.3"),
            "remesh".NamedField(remesh),
            "target_type".NamedField(targetType),
            "target_count".NamedField(targetCount.ToString()),
            "guidance_scale".NamedField(guidanceScale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)),
            "seed".NamedField(seed.ToString())
        };

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("model/gltf-binary"));

        using var resp = await client.PostAsync($"{BASE_URL}/stable-point-aware-3d", form, cancellationToken);
        var bytesOut = await resp.Content.ReadAsByteArrayAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"{resp.StatusCode}: {System.Text.Encoding.UTF8.GetString(bytesOut)}");

        var outputName = (filename ?? requestContext.ToOutputFileName()) + ".glb";
        var uploaded = await requestContext.Server.Upload(
            serviceProvider,
            outputName,
            BinaryData.FromBytes(bytesOut),
            cancellationToken) ?? throw new Exception("Upload failed");

        return bytesOut.ToBlobContent(uploaded.Uri, "model/gltf-binary").ToCallToolResult();
    });
}
