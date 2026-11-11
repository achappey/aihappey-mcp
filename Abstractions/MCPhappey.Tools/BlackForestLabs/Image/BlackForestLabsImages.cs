using System.ComponentModel;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.BlackForestLabs.Image;

public static class BlackForestLabsImages
{
    private static readonly string[] AspectRatios = ["auto", "21:9", "2:1", "16:9", "3:2", "7:5", "4:3", "5:4", "1:1", "4:5", "3:4", "5:7", "2:3", "9:16", "1:2", "9:21"];

    [Description("Generate or edit an image with native Black Forest Labs FLUX 3 Image or Image Klein. Supply reference URLs on every call; no conversation state is retained.")]
    [McpServerTool(Title = "Black Forest Labs Images: generate or edit image", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> BlackForestLabsImages_CreateImage(
        [Description("Prompt describing the image or edits. Reference images are numbered in supplied order.")] string prompt,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Native model: flux-3-image or flux-3-image-klein.")] string model = "flux-3-image",
        [Description("Up to 10 reference image URLs. Public HTTPS URLs or SharePoint/OneDrive links with delegated access.")] string[]? imageUrls = null,
        [Description("Output aspect ratio, or auto.")] string aspectRatio = "auto",
        [Description("768sq, 1k, 1.5k, 2k, 4k; match_input is also supported by FLUX 3 Image for edits. Omit for model defaults.")] string? resolution = null,
        [Description("Exact pixel width; requires height. Overrides aspect ratio/resolution.")] int? width = null,
        [Description("Exact pixel height; requires width.")] int? height = null,
        [Description("Grounding through external research. FLUX 3 Image only; omit for Klein.")] bool? grounding = null,
        [Description("Moderation tolerance 0–7; provider organization determines approved levels.")] int safetyTolerance = 2,
        [Description("Reproducibility seed 0–4294967295. Klein only.")] long? seed = null,
        [Description("jpeg or png. Klein only.")] string? outputFormat = null,
        [Description("Denoising steps 1–16. Klein only.")] int? steps = null,
        [Description("Output filename without extension; defaults to an automatically generated name.")] string? filename = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            // Validate before downloading any supplied media or issuing a billable generation.
            var payload = BuildPayload(prompt, model, imageUrls?.Length ?? 0, aspectRatio, resolution,
                width, height, grounding, safetyTolerance, seed, outputFormat, steps);
            var references = new JsonArray();
            foreach (var url in imageUrls ?? [])
            {
                var image = await serviceProvider.GetMediaInputAsync(requestContext.Server, url, "image", cancellationToken);
                references.Add(Convert.ToBase64String(image.Contents.ToArray()));
            }
            if (references.Count > 0) payload["images"] = references;
            var client = serviceProvider.GetRequiredService<BlackForestLabsClient>();
            var result = await client.GenerateAsync(requestContext.Server, model, payload, cancellationToken);
            var sample = result["sample"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(sample)) throw new InvalidOperationException("BFL returned no generated image sample.");
            var output = await client.DownloadOutputAsync(sample,
                Path.GetFileNameWithoutExtension(filename ?? requestContext.ToOutputFileName("jpg")) + ".jpg", "image/jpeg", cancellationToken);
            return await requestContext.Server.ToMediaCallToolResult(serviceProvider, [output], cancellationToken);
        });

    internal static JsonObject BuildPayload(string prompt, string model, int referenceCount, string aspectRatio,
        string? resolution, int? width, int? height, bool? grounding, int safetyTolerance, long? seed, string? outputFormat, int? steps)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        var klein = model == "flux-3-image-klein";
        if (!klein && model != "flux-3-image") throw new ArgumentException("Use flux-3-image or flux-3-image-klein.");
        if (referenceCount is < 0 or > 10) throw new ArgumentException("BFL supports at most 10 reference images.");
        if (!AspectRatios.Contains(aspectRatio)) throw new ArgumentException("Unsupported BFL image aspect ratio.");
        if (resolution is not null && resolution is not ("768sq" or "1k" or "1.5k" or "2k" or "4k"))
            if (klein || resolution != "match_input" || referenceCount == 0)
                throw new ArgumentException("Unsupported resolution for this model/input combination.");
        if (width.HasValue != height.HasValue) throw new ArgumentException("Set width and height together.");
        var minimum = klein ? 64 : 256;
        if (width is { } w && height is { } h && (w < minimum || h < minimum || w > 6256 || h > 6256
            || (long)w * h > (!klein && referenceCount > 0 ? 4194304 : 16822272)))
            throw new ArgumentException("BFL image dimensions exceed the model's pixel or reference-image limits.");
        if (safetyTolerance is < 0 or > 7) throw new ArgumentException("Safety tolerance must be from 0 to 7.");
        if (!klein && (seed.HasValue || outputFormat is not null || steps.HasValue))
            throw new ArgumentException("Seed, output format, and steps are Klein-only options.");
        if (klein && grounding.HasValue) throw new ArgumentException("Grounding is not a Klein option.");
        if (seed is < 0 or > uint.MaxValue) throw new ArgumentException("Seed must be from 0 to 4294967295.");
        if (steps is < 1 or > 16) throw new ArgumentException("Klein steps must be from 1 to 16.");
        if (outputFormat is not null && outputFormat is not ("jpeg" or "png")) throw new ArgumentException("Use jpeg or png output format.");
        var payload = new JsonObject { ["prompt"] = prompt, ["aspect_ratio"] = aspectRatio, ["safety_tolerance"] = safetyTolerance };
        if (!klein) payload["version"] = "latest";
        if (resolution is not null) payload["resolution"] = resolution;
        if (width.HasValue) { payload["width"] = width.Value; payload["height"] = height!.Value; }
        if (grounding.HasValue) payload["grounding"] = grounding.Value;
        if (seed.HasValue) payload["seed"] = seed.Value;
        if (outputFormat is not null) payload["output_format"] = outputFormat;
        if (steps.HasValue) payload["steps"] = steps.Value;
        return payload;
    }
}
