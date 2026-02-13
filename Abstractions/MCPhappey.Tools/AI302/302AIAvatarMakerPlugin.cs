using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.AI302;

public static class AI302AvatarMakerPlugin
{
    [Description("Generate stylized avatars from an input image URL. Supports SharePoint and OneDrive protected links via fileUrl.")]
    [McpServerTool(
        Title = "302.AI avatar maker",
        Name = "302ai_avatar_generate",
        ReadOnly = false,
        Idempotent = false,
        OpenWorld = true,
        Destructive = false)]
    public static async Task<CallToolResult?> AI302_Avatar_Generate(
        [Description("Input image URL. SharePoint and OneDrive secure links are supported.")] string fileUrl,
        [Description("Person type: female, male, or kid.")] string personType,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Preset style id (1-17). Ignored when customPrompt is not empty.")] int? style = null,
        [Description("Custom prompt. If set, style is ignored.")] string? customPrompt = null,
        [Description("Number of avatars to generate (1-4). Out-of-range values are treated as 1.")] int num = 1,
        [Description("Avatar width (256-1036). Out-of-range values are treated as 1024.")] int width = 1024,
        [Description("Avatar height (256-1036). Out-of-range values are treated as 1024.")] int height = 768,
        [Description("Model used to optimize avatar description prompts.")] string model = "gpt-4.1",
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        {
            var client = serviceProvider.GetRequiredService<AI302Client>();
            var downloadService = serviceProvider.GetRequiredService<DownloadService>();

            var (typed, _, _) = await requestContext.Server.TryElicit(new AI302AvatarGenerateInput
            {
                FileUrl = fileUrl,
                PersonType = personType,
                Style = style,
                CustomPrompt = customPrompt,
                Num = num,
                Width = width,
                Height = height,
                Model = model,
                Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
            }, cancellationToken);

            var inputFiles = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, typed.FileUrl, cancellationToken);
            var inputImage = inputFiles.FirstOrDefault() ?? throw new InvalidOperationException("Failed to download input image from fileUrl.");

            var normalizedPersonType = typed.PersonType?.Trim().ToLowerInvariant();
            if (normalizedPersonType is not ("female" or "male" or "kid"))
                throw new ArgumentException("personType must be one of: female, male, kid.");

            var finalNum = typed.Num is >= 1 and <= 4 ? typed.Num : 1;
            var finalWidth = typed.Width is >= 256 and <= 1036 ? typed.Width : 1024;
            var finalHeight = typed.Height is >= 256 and <= 1036 ? typed.Height : 1024;

            var body = new JsonObject
            {
                ["image"] = inputImage.ToDataUri(),
                ["person_type"] = normalizedPersonType,
                ["num"] = finalNum,
                ["width"] = finalWidth,
                ["height"] = finalHeight,
                ["model"] = string.IsNullOrWhiteSpace(typed.Model) ? "gpt-4.1" : typed.Model
            };

            if (!string.IsNullOrWhiteSpace(typed.CustomPrompt))
                body["custom_prompt"] = typed.CustomPrompt;
            else if (typed.Style.HasValue)
                body["style"] = typed.Style.Value;

            var response = await client.PostAsync("302/headshot/generate", body, cancellationToken)
                ?? throw new Exception("302.AI returned an empty response.");

            var urls = response["headshot_url_list"]?.AsArray()
                ?? throw new Exception("302.AI response missing headshot_url_list.");

            List<ResourceLinkBlock> uploaded = [];
            var baseName = typed.Filename;

            int index = 0;
            foreach (var urlNode in urls)
            {
                var outputUrl = urlNode?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(outputUrl))
                    continue;

                var generatedFiles = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, outputUrl, cancellationToken);
                var generatedFile = generatedFiles.FirstOrDefault();
                if (generatedFile is null)
                    continue;

                var ext = generatedFile.MimeType.ResolveExtensionFromMime();
                if (string.IsNullOrWhiteSpace(ext) || ext.Equals(".bin", StringComparison.OrdinalIgnoreCase))
                    ext = ".png";

                var graphItem = await requestContext.Server.Upload(
                    serviceProvider,
                    $"{baseName}-{index + 1}{ext}",
                    generatedFile.Contents,
                    cancellationToken);

                if (graphItem != null)
                    uploaded.Add(graphItem);

                index++;
            }

            return uploaded.ToResourceLinkCallToolResponse();
        });

    [Description("Please fill in the 302.AI avatar generation request details.")]
    public class AI302AvatarGenerateInput
    {
        [Required]
        [Description("Input image URL. SharePoint and OneDrive secure links are supported.")]
        public string FileUrl { get; set; } = default!;

        [Required]
        [Description("Person type: female, male, or kid.")]
        public string PersonType { get; set; } = "female";

        [Description("Preset style id (1-17). Ignored when customPrompt is not empty.")]
        public int? Style { get; set; }

        [Description("Custom prompt. If set, style is ignored.")]
        public string? CustomPrompt { get; set; }

        [Range(1, 4)]
        [Description("Number of avatars to generate (1-4).")]
        public int Num { get; set; } = 1;

        [Description("Avatar width (256-1036).")]
        public int Width { get; set; } = 1024;

        [Description("Avatar height (256-1036).")]
        public int Height { get; set; } = 768;

        [Description("Model used to optimize avatar description prompts.")]
        public string Model { get; set; } = "gpt-4.1";

        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }
}

