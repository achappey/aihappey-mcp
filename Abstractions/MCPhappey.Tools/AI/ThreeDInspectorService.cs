using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using SharpGLTF.Schema2;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using MCPhappey.Core.Services;
using System.Text.Json;
using MCPhappey.Core.Extensions;

namespace MCPhappey.Tools.AI;

public static class ThreeDInspectorService
{
    [Description("Inspect a GLB file (url or data URL) and return metadata.")]
    [McpServerTool(
        Title = "3D Inspector (GLB)",
        Name = "threed_inspect_glb",
        ReadOnly = true,
        Idempotent = true)]
    public static async Task<CallToolResult?> ThreeD_InspectGlb(
        [Description("Url of the glb file. Protected SharePoint and OneDive links are supported.")] string fileUrl,
        IServiceProvider services,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken ct = default) =>
        await requestContext.WithExceptionCheck(async () =>
    {
        if (string.IsNullOrWhiteSpace(fileUrl))
            throw new ArgumentException("Provide 'fileUrl'");

        var downloadService = services.GetRequiredService<DownloadService>();

        var files = await downloadService.DownloadContentAsync(services, requestContext.Server, fileUrl, ct);
        var file = files.FirstOrDefault() ?? throw new InvalidOperationException("No GLB bytes available.");

        using var ms = new MemoryStream(file.Contents.ToArray());
        ModelRoot model = ModelRoot.ReadGLB(ms);

        return new CallToolResult
        {
            StructuredContent = JsonSerializer.SerializeToNode(model)
        };
    });
}
