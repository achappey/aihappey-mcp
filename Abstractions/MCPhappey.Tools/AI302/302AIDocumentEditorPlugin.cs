using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.AI302;

public static class AI302DocumentEditorPlugin
{
    [Description("Generate a long-text outline for an article using 302.AI Document Editor.")]
    [McpServerTool(
        Title = "302.AI document editor outline",
        Name = "302ai_document_editor_generate_outline",
        ReadOnly = true,
        OpenWorld = true)]
    public static async Task<CallToolResult?> AI302_DocumentEditor_GenerateOutline(
        [Description("Article title.")] string title,
        [Description("Model name, e.g. gpt-4.1 or gpt-4o-mini.")] string model,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Language code, e.g. zh, en, nl.")] string language = "zh",
        [Description("Whether to include illustration sections in the outline.")] bool includeIllustration = true,
        [Description("Optional top_p sampling value.")] double? topP = null,
        [Description("Optional temperature sampling value.")] double? temperature = null,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(new AI302DocumentEditorOutlineInput
            {
                Title = title,
                Model = model,
                Language = language,
                IncludeIllustration = includeIllustration,
                TopP = topP,
                Temperature = temperature
            }, cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "Elicitation was not accepted.".ToErrorCallToolResponse();

            return await requestContext.WithStructuredContent(async () =>
            {
                var client = serviceProvider.GetRequiredService<AI302Client>();

                var body = new JsonObject
                {
                    ["title"] = typed.Title,
                    ["model"] = typed.Model,
                    ["language"] = typed.Language,
                    ["include_illustration"] = typed.IncludeIllustration,
                    ["top_p"] = typed.TopP,
                    ["temperature"] = typed.Temperature
                };

                JsonNode? response = await client.PostAsync("302/writing/api/v1/outline/generate", body, cancellationToken);
                return response;
            });
        });

    [Description("Generate full article content from a title and optional outline sections using 302.AI Document Editor.")]
    [McpServerTool(
        Title = "302.AI document editor article",
        Name = "302ai_document_editor_generate_article",
        ReadOnly = false,
        OpenWorld = true,
        Destructive = false)]
    public static async Task<CallToolResult?> AI302_DocumentEditor_GenerateArticle(
        [Description("Article title.")] string title,
        [Description("Model name, e.g. gpt-4o-mini.")] string model,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Language code, e.g. zh, en, nl.")] string language = "zh",
        [Description("Optional outline sections as JSON array string. Example: [{\"type\":\"text\",\"content\":\"...\"}]")] string? sectionsJson = null,
        [Description("Illustration mode: only_net, only_ai, or first_net.")] string imgDemo = "only_ai",
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(new AI302DocumentEditorArticleInput
            {
                Title = title,
                Model = model,
                Language = language,
                SectionsJson = sectionsJson,
                ImgDemo = imgDemo
            }, cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "Elicitation was not accepted.".ToErrorCallToolResponse();

            return await requestContext.WithStructuredContent(async () =>
            {
                var client = serviceProvider.GetRequiredService<AI302Client>();

                var normalizedImgDemo = string.IsNullOrWhiteSpace(typed.ImgDemo)
                    ? "only_ai"
                    : typed.ImgDemo.Trim().ToLowerInvariant();

                if (normalizedImgDemo is not ("only_net" or "only_ai" or "first_net"))
                    throw new ArgumentException("imgDemo must be one of: only_net, only_ai, first_net.");

                var body = new JsonObject
                {
                    ["title"] = typed.Title,
                    ["language"] = typed.Language,
                    ["model"] = typed.Model,
                    ["img_demo"] = normalizedImgDemo
                };

                if (!string.IsNullOrWhiteSpace(typed.SectionsJson))
                {
                    var parsed = JsonNode.Parse(typed.SectionsJson)
                        ?? throw new ArgumentException("sectionsJson is not valid JSON.");

                    if (parsed is not JsonArray)
                        throw new ArgumentException("sectionsJson must be a JSON array.");

                    body["sections"] = parsed;
                }

                JsonNode? response = await client.PostAsync("302/writing/api/v1/longtext/generate", body, cancellationToken);
                return response;
            });
        });

    [Description("Please fill in the 302.AI document editor outline request details.")]
    public class AI302DocumentEditorOutlineInput
    {
        [Required]
        [Description("Article title.")]
        public string Title { get; set; } = default!;

        [Required]
        [Description("Model name, e.g. gpt-4.1.")]
        public string Model { get; set; } = default!;

        [Description("Language code, e.g. zh, en, nl.")]
        public string Language { get; set; } = "zh";

        [Description("Whether to include illustration sections.")]
        public bool IncludeIllustration { get; set; } = true;

        [Description("Optional top_p sampling value.")]
        public double? TopP { get; set; }

        [Description("Optional temperature sampling value.")]
        public double? Temperature { get; set; }
    }

    [Description("Please fill in the 302.AI document editor article generation request details.")]
    public class AI302DocumentEditorArticleInput
    {
        [Required]
        [Description("Article title.")]
        public string Title { get; set; } = default!;

        [Required]
        [Description("Model name, e.g. gpt-4o-mini.")]
        public string Model { get; set; } = default!;

        [Description("Language code, e.g. zh, en, nl.")]
        public string Language { get; set; } = "zh";

        [Description("Optional outline sections as JSON array string.")]
        public string? SectionsJson { get; set; }

        [Description("Illustration mode: only_net, only_ai, or first_net.")]
        public string ImgDemo { get; set; } = "only_ai";
    }
}

