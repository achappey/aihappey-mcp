using System.ComponentModel;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.AI302;

public static class AI3023DModellingPlugin
{
    [Description("Convert a 3D model archive URL to another 3D format (obj, stl, glb).")]
    [McpServerTool(Title = "302.AI 3D format convert", Name = "302ai_3d_format_convert", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> AI302_3D_Format_Convert(
        [Description("Public URL to a 3D model ZIP package.")] string trimeshFileUrl,
        [Description("Target output format: obj, stl, or glb.")] string outputFormat,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var client = serviceProvider.GetRequiredService<AI302Client>();

                var body = new JsonObject
                {
                    ["trimesh_file_url"] = trimeshFileUrl,
                    ["output_format"] = outputFormat
                };

                JsonNode? response = await client.PostAsync("302/3d/format_convert", body, cancellationToken);
                return response;
            }));
}

