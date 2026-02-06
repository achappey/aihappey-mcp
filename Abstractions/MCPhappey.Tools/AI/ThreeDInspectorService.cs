using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using SharpGLTF.Schema2;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Services;

namespace MCPhappey.Tools.AI;

public static class ThreeDInspectorService
{
    // ---- CONFIG ----
    private const int MaxDownloadBytes = 64 * 1024 * 1024; // 64 MB safety

    [Description("Inspect a GLB file (url or data URL) and return metadata.")]
    [McpServerTool(
        Title = "3D Inspector (GLB)",
        Name = "threed_inspect_glb",
        ReadOnly = true,
        Idempotent = true)]
    public static async Task<CallToolResult> ThreeD_InspectGlb(
        [Description("Public URL to a .glb file (optional if dataUrl provided).")]
        string? url,
        [Description("Url of the glb file. Protected SharePoint and OneDive links are supported.")] string fileUrl,
        IServiceProvider services,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url) && string.IsNullOrWhiteSpace(fileUrl))
            throw new ArgumentException("Provide either 'url' or 'dataUrl' with base64 GLB.");
        var downloadService = services.GetRequiredService<DownloadService>();

        var files = await downloadService.DownloadContentAsync(services, requestContext.Server, fileUrl, ct);
        var file = files.FirstOrDefault() ?? throw new InvalidOperationException("No GLB bytes available.");
        // 1) Acquire bytes
        // 2) Load model
        ModelRoot model;
        try
        {
            using var ms = new MemoryStream(file.Contents.ToArray());
            model = ModelRoot.ReadGLB(ms);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to read GLB. Ensure the file is valid.", ex);
        }

        // 3) Extract metadata
        var info = ExtractInfo(model, file.Contents.Length);

        // 4) Build outputs
        var summaryMd = BuildSummaryMarkdown(info, url);

        return new CallToolResult
        {
            Content =
                    [
                summaryMd.ToTextContentBlock(),
                info.ToJsonContentBlock("https://github.com/vpenades/SharpGLTF")
            ]
        };


    }

    // -----------------------------
    // Helpers
    // -----------------------------
    private static byte[] ParseDataUrlToBytes(string dataUrl)
    {
        // Support: data:model/gltf-binary;base64,AAAA...
        var m = Regex.Match(dataUrl, @"^data:(?<mime>[^;]+);base64,(?<b64>.+)$", RegexOptions.IgnoreCase);
        if (!m.Success) throw new ArgumentException("Invalid data URL. Expect 'data:*;base64,...'");

        var b64 = m.Groups["b64"].Value;
        try { return Convert.FromBase64String(b64); }
        catch (FormatException ex) { throw new ArgumentException("dataUrl base64 is invalid.", ex); }
    }

    private static ThreeDGlbInfo ExtractInfo(ModelRoot model, long fileSizeBytes)
    {
        var meshes = model.LogicalMeshes
            .Select(m => new
            {
                Mesh = m,
                Name = m.Name ?? "",
                Primitives = m.Primitives.Select(p => new
                {
                    Material = p.Material?.Name ?? "",
                    DrawPrimitiveType = p.DrawPrimitiveType.ToString()
                }).ToList()
            })
            .ToList();

        var materials = model.LogicalMaterials.Select(m => m.Name ?? "").ToList();
        var images = model.LogicalImages.Select(i => i.Name ?? "").ToList();
        var textures = model.LogicalTextures.Select(t => t.Name ?? "").ToList();
        var nodes = model.LogicalNodes.Select(n => n.Name ?? "").ToList();
        var scenes = model.LogicalScenes.Select(s => s.Name ?? "").ToList();
        var animations = model.LogicalAnimations.Select(a => a.Name ?? "").ToList();
        var skins = model.LogicalSkins.Select(s => s.Name ?? "").ToList();

        // If you add SharpGLTF.Toolkit: you can compute bounds like:
        // var bounds = ModelBounds.CreateFrom(model); // requires Toolkit
        // var bbox = new { Min = bounds.Minimum, Max = bounds.Maximum };

        return new ThreeDGlbInfo
        {
            FileSizeBytes = fileSizeBytes,
            AssetVersion = model.Asset?.Version.ToString(),
            AssetGenerator = model.Asset?.Generator,
            SceneCount = model.LogicalScenes.Count,
            NodeCount = model.LogicalNodes.Count,
            MeshCount = model.LogicalMeshes.Count,
            PrimitiveCount = model.LogicalMeshes.Sum(m => m.Primitives.Count),
            MaterialCount = model.LogicalMaterials.Count,
            TextureCount = model.LogicalTextures.Count,
            ImageCount = model.LogicalImages.Count,
            AnimationCount = model.LogicalAnimations.Count,
            SkinCount = model.LogicalSkins.Count,

            Scenes = scenes,
            Nodes = nodes,
            Materials = materials,
            Textures = textures,
            Images = images,
            Animations = animations,
            Skins = skins,
            Meshes = [.. meshes.Select(m => new ThreeDGlbMesh
            {
                Name = m.Name,
                PrimitiveCount = m.Primitives.Count,
                Primitives = [.. m.Primitives.Select(p => new ThreeDGlbPrimitive
                {
                    Material = p.Material,
                    Mode = p.DrawPrimitiveType
                })]
            })]
        };
    }

    private static string BuildSummaryMarkdown(ThreeDGlbInfo info, string? url)
    {
        var src = !string.IsNullOrWhiteSpace(url) ? url : "(unknown)";
        var sb = new StringBuilder();
        sb.AppendLine("## 3D Inspector — GLB Summary");
        sb.AppendLine();
        sb.AppendLine($"**Source**: {src}");
        sb.AppendLine($"**File size**: {info.FileSizeBytes:N0} bytes");
        if (!string.IsNullOrWhiteSpace(info.AssetVersion)) sb.AppendLine($"**glTF Version**: {info.AssetVersion}");
        if (!string.IsNullOrWhiteSpace(info.AssetGenerator)) sb.AppendLine($"**Generator**: {info.AssetGenerator}");
        sb.AppendLine();
        sb.AppendLine($"- Scenes: **{info.SceneCount}**");
        sb.AppendLine($"- Nodes: **{info.NodeCount}**");
        sb.AppendLine($"- Meshes: **{info.MeshCount}** (Primitives: **{info.PrimitiveCount}**) ");
        sb.AppendLine($"- Materials: **{info.MaterialCount}**, Textures: **{info.TextureCount}**, Images: **{info.ImageCount}**");
        sb.AppendLine($"- Animations: **{info.AnimationCount}**, Skins: **{info.SkinCount}**");
        if (info.Meshes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**Meshes (first 5):**");
            foreach (var m in info.Meshes.Take(5))
            {
                sb.AppendLine($"- {EscapeMd(m.Name ?? string.Empty)} (primitives: {m.PrimitiveCount})");
            }
        }
        return sb.ToString();
    }

    private static string EscapeMd(string s) =>
        string.IsNullOrEmpty(s) ? "(unnamed)" : s.Replace("*", "\\*").Replace("_", "\\_");
}

// -----------------------------
// DTOs
// -----------------------------
public sealed class ThreeDGlbInfo
{
    public long FileSizeBytes { get; set; }
    public string? AssetVersion { get; set; }
    public string? AssetGenerator { get; set; }

    public int SceneCount { get; set; }
    public int NodeCount { get; set; }
    public int MeshCount { get; set; }
    public int PrimitiveCount { get; set; }
    public int MaterialCount { get; set; }
    public int TextureCount { get; set; }
    public int ImageCount { get; set; }
    public int AnimationCount { get; set; }
    public int SkinCount { get; set; }

    public List<string> Scenes { get; set; } = [];
    public List<string> Nodes { get; set; } = [];
    public List<string> Materials { get; set; } = [];
    public List<string> Textures { get; set; } = [];
    public List<string> Images { get; set; } = [];
    public List<string> Animations { get; set; } = [];
    public List<string> Skins { get; set; } = [];
    public List<ThreeDGlbMesh> Meshes { get; set; } = [];
}

public sealed class ThreeDGlbMesh
{
    public string? Name { get; set; }
    public int PrimitiveCount { get; set; }
    public List<ThreeDGlbPrimitive> Primitives { get; set; } = [];
}

public sealed class ThreeDGlbPrimitive
{
    public string? Material { get; set; }
    public string? Mode { get; set; }
}
