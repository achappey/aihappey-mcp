using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using Microsoft.Graph.Beta.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.Sites;

public static class GraphSharePointPages
{
    [Description("Create a draft modern SharePoint site page with a title and optional introductory text.")]
    [McpServerTool(Title = "Create SharePoint draft page", Name = "graph_sharepoint_pages_create",
        UseStructuredContent = true, OutputSchemaType = typeof(BaseSitePage), Destructive = false,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphSharePointPages_CreateDraft(
        [Description("Microsoft Graph site ID.")] string siteId,
        [Description("Page title.")] string title,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional page file name ending in .aspx.")] string? name = null,
        [Description("Optional introductory HTML for the first text web part.")] string? introductoryHtml = null,
        [Description("Whether comments are enabled.")] bool showComments = true,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new CreatePageInput
                {
                    Title = title,
                    Name = name,
                    IntroductoryHtml = introductoryHtml,
                    ShowComments = showComments
                }, cancellationToken);
            if (notAccepted is not null)
                return default(BaseSitePage);

            ValidateCreate(typed);
            var page = new SitePage
            {
                Name = NormalizePageName(typed!.Name, typed.Title),
                Title = typed.Title.Trim(),
                PageLayout = PageLayoutType.Article,
                ShowComments = typed.ShowComments,
                CanvasLayout = string.IsNullOrWhiteSpace(typed.IntroductoryHtml)
                    ? null
                    : CreateCanvas(typed.IntroductoryHtml)
            };

            return await client.Sites[siteId].Pages.PostAsync(page, cancellationToken: cancellationToken);
        })));

    [Description("Update the metadata of a modern SharePoint site page.")]
    [McpServerTool(Title = "Update SharePoint page metadata", Name = "graph_sharepoint_pages_update",
        UseStructuredContent = true, OutputSchemaType = typeof(BaseSitePage), Destructive = true,
        Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphSharePointPages_Update(
        [Description("Microsoft Graph site ID.")] string siteId,
        [Description("SharePoint page ID.")] string pageId,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Updated page title.")] string? title = null,
        [Description("Updated page file name ending in .aspx.")] string? name = null,
        [Description("Enable or disable comments.")] bool? showComments = null,
        [Description("Promote as a normal page or news post: page or newsPost.")] string? promotionKind = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new UpdatePageInput
                {
                    Title = title,
                    Name = name,
                    ShowComments = showComments,
                    PromotionKind = promotionKind
                }, cancellationToken);
            if (notAccepted is not null)
                return default(BaseSitePage);

            ValidateUpdate(typed);
            return await client.Sites[siteId].Pages[pageId].PatchAsync(
                new SitePage
                {
                    Title = NullIfWhiteSpace(typed!.Title),
                    Name = typed.Name is null ? null : NormalizePageName(typed.Name, typed.Title ?? "page"),
                    ShowComments = typed.ShowComments,
                    PromotionKind = ParsePromotionKind(typed.PromotionKind)
                }, cancellationToken: cancellationToken);
        })));

    [Description("Append a text web part to a modern SharePoint site page.")]
    [McpServerTool(Title = "Add text to SharePoint page", Name = "graph_sharepoint_pages_add_text_web_part",
        UseStructuredContent = true, OutputSchemaType = typeof(WebPart), Destructive = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphSharePointPages_AddTextWebPart(
        [Description("Microsoft Graph site ID.")] string siteId,
        [Description("SharePoint page ID.")] string pageId,
        [Description("Text web part HTML.")] string innerHtml,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new TextWebPartInput { InnerHtml = innerHtml }, cancellationToken);
            if (notAccepted is not null)
                return default(WebPart);
            ArgumentException.ThrowIfNullOrWhiteSpace(typed?.InnerHtml);

            return await client.Sites[siteId].Pages[pageId].GraphSitePage.WebParts.PostAsync(
                new TextWebPart { InnerHtml = typed.InnerHtml }, cancellationToken: cancellationToken);
        })));

    [Description("Publish the current draft of a modern SharePoint site page.")]
    [McpServerTool(Title = "Publish SharePoint page", Name = "graph_sharepoint_pages_publish",
        Destructive = true, Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphSharePointPages_Publish(
        [Description("Microsoft Graph site ID.")] string siteId,
        [Description("SharePoint page ID to publish.")] string pageId,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new PublishPageInput { Name = pageId }, cancellationToken);
            if (notAccepted is not null)
                return new CallToolResult { Content = [new TextContentBlock { Text = "Page publication cancelled." }] };

            var request = new Microsoft.Kiota.Abstractions.RequestInformation
            {
                HttpMethod = Microsoft.Kiota.Abstractions.Method.POST,
                URI = new Uri($"https://graph.microsoft.com/beta/sites/{Uri.EscapeDataString(siteId)}/pages/{Uri.EscapeDataString(typed!.Name)}/microsoft.graph.sitePage/publish")
            };
            await client.RequestAdapter.SendNoContentAsync(request, cancellationToken: cancellationToken);
            return new CallToolResult { Content = [new TextContentBlock { Text = "SharePoint page published." }] };
        }));

    [Description("Delete a modern SharePoint site page.")]
    [McpServerTool(Title = "Delete SharePoint page", Name = "graph_sharepoint_pages_delete",
        Destructive = true, Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphSharePointPages_Delete(
        [Description("Microsoft Graph site ID.")] string siteId,
        [Description("SharePoint page ID to delete.")] string pageId,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.ConfirmAndDeleteAsync<DeletePageInput>(
            pageId,
            async _ => await client.Sites[siteId].Pages[pageId].DeleteAsync(cancellationToken: cancellationToken),
            "SharePoint page deleted.", cancellationToken)));

    [Description("Please review the new SharePoint page fields.")]
    public sealed class CreatePageInput
    {
        [Required, JsonPropertyName("title")]
        public string Title { get; set; } = default!;

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("introductoryHtml")]
        public string? IntroductoryHtml { get; set; }

        [JsonPropertyName("showComments")]
        public bool ShowComments { get; set; } = true;
    }

    [Description("Please review the SharePoint page metadata changes.")]
    public sealed class UpdatePageInput
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("showComments")]
        public bool? ShowComments { get; set; }

        [JsonPropertyName("promotionKind")]
        public string? PromotionKind { get; set; }
    }

    [Description("Please review the text web part HTML.")]
    public sealed class TextWebPartInput
    {
        [Required, JsonPropertyName("innerHtml")]
        public string InnerHtml { get; set; } = default!;
    }

    [Description("Please confirm the SharePoint page ID to publish: {0}")]
    public sealed class PublishPageInput : MCPhappey.Common.Models.IHasName
    {
        [Required, JsonPropertyName("name")]
        public string Name { get; set; } = default!;
    }

    [Description("Please confirm the SharePoint page ID to delete: {0}")]
    public sealed class DeletePageInput : MCPhappey.Common.Models.IHasName
    {
        [Required, JsonPropertyName("name")]
        public string Name { get; set; } = default!;
    }

    private static CanvasLayout CreateCanvas(string html) => new()
    {
        HorizontalSections =
        [
            new HorizontalSection
            {
                Layout = HorizontalSectionLayoutType.OneColumn,
                Columns =
                [
                    new HorizontalSectionColumn
                    {
                        Width = 12,
                        Webparts = [new TextWebPart { InnerHtml = html.Trim() }]
                    }
                ]
            }
        ]
    };

    private static void ValidateCreate(CreatePageInput? input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Title);
        if (input.Name is not null && !input.Name.EndsWith(".aspx", StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("SharePoint page name must end in .aspx.");
    }

    private static void ValidateUpdate(UpdatePageInput? input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Title is null && input.Name is null && input.ShowComments is null && input.PromotionKind is null)
            throw new ValidationException("Provide at least one SharePoint page field to update.");
        if (input.Name is not null && !input.Name.EndsWith(".aspx", StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("SharePoint page name must end in .aspx.");
        _ = ParsePromotionKind(input.PromotionKind);
    }

    private static PagePromotionType? ParsePromotionKind(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" => null,
        "page" => PagePromotionType.Page,
        "newspost" => PagePromotionType.NewsPost,
        _ => throw new ValidationException("Promotion kind must be page or newsPost.")
    };

    private static string NormalizePageName(string? name, string title)
    {
        if (!string.IsNullOrWhiteSpace(name))
            return name.Trim();
        var safe = string.Concat(title.Trim().Select(c => char.IsLetterOrDigit(c) ? c : '-')).Trim('-');
        return $"{(string.IsNullOrWhiteSpace(safe) ? "page" : safe)}.aspx";
    }

    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
