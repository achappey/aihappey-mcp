using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using Microsoft.Graph.Beta.Models.Search;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.Search;

public static class GraphSearchBookmarks
{
    [Description("Create a Microsoft Search bookmark for the organization.")]
    [McpServerTool(Title = "Create Microsoft Search bookmark", Name = "graph_search_bookmarks_create",
        Destructive = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(Bookmark))]
    public static async Task<CallToolResult?> GraphSearchBookmarks_Create(
        [Description("Bookmark display name shown in Microsoft Search results.")] string displayName,
        [Description("Absolute target URL opened by the bookmark.")] string webUrl,
        [Description("Comma-separated search keywords that trigger the bookmark.")] string keywordsCsv,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional bookmark description.")] string? description = null,
        [Description("Comma-separated bookmark categories.")] string? categoriesCsv = null,
        [Description("Comma-separated language tags, such as en-US,nl-NL.")] string? languageTagsCsv = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (input, notAccepted, _) = await requestContext.Server.TryElicit(new BookmarkInput
            {
                DisplayName = displayName, WebUrl = webUrl, KeywordsCsv = keywordsCsv,
                Description = description, CategoriesCsv = categoriesCsv, LanguageTagsCsv = languageTagsCsv
            }, cancellationToken);
            if (notAccepted is not null || input is null) return default(Bookmark);

            Validate(input.DisplayName, input.WebUrl, input.KeywordsCsv);
            return await client.Search.Bookmarks.PostAsync(ToBookmark(input), cancellationToken: cancellationToken);
        })));

    [Description("Update a Microsoft Search bookmark. Only supplied values are changed.")]
    [McpServerTool(Title = "Update Microsoft Search bookmark", Name = "graph_search_bookmarks_update",
        Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(Bookmark))]
    public static async Task<CallToolResult?> GraphSearchBookmarks_Update(
        [Description("Microsoft Search bookmark ID.")] string bookmarkId,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("New display name.")] string? displayName = null,
        [Description("New absolute target URL.")] string? webUrl = null,
        [Description("Replacement comma-separated search keywords.")] string? keywordsCsv = null,
        [Description("New description.")] string? description = null,
        [Description("Replacement comma-separated categories.")] string? categoriesCsv = null,
        [Description("Replacement comma-separated language tags.")] string? languageTagsCsv = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            if (displayName is null && webUrl is null && keywordsCsv is null && description is null &&
                categoriesCsv is null && languageTagsCsv is null)
                throw new ValidationException("At least one bookmark field must be provided.");

            var (input, notAccepted, _) = await requestContext.Server.TryElicit(new BookmarkPatchInput
            {
                DisplayName = displayName, WebUrl = webUrl, KeywordsCsv = keywordsCsv,
                Description = description, CategoriesCsv = categoriesCsv, LanguageTagsCsv = languageTagsCsv
            }, cancellationToken);
            if (notAccepted is not null || input is null) return default(Bookmark);

            if (input.DisplayName is not null) ArgumentException.ThrowIfNullOrWhiteSpace(input.DisplayName);
            if (input.WebUrl is not null) ValidateUrl(input.WebUrl);
            if (input.KeywordsCsv is not null && SplitCsv(input.KeywordsCsv).Count == 0)
                throw new ValidationException("At least one keyword is required when replacing keywords.");

            return await client.Search.Bookmarks[bookmarkId].PatchAsync(new Bookmark
            {
                DisplayName = input.DisplayName?.Trim(), WebUrl = input.WebUrl?.Trim(), Description = input.Description,
                Keywords = input.KeywordsCsv is null ? null : ToKeywords(input.KeywordsCsv),
                Categories = input.CategoriesCsv is null ? null : SplitCsv(input.CategoriesCsv),
                LanguageTags = input.LanguageTagsCsv is null ? null : SplitCsv(input.LanguageTagsCsv)
            }, cancellationToken: cancellationToken);
        })));

    [Description("Delete a Microsoft Search bookmark.")]
    [McpServerTool(Title = "Delete Microsoft Search bookmark", Name = "graph_search_bookmarks_delete",
        Destructive = true, Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphSearchBookmarks_Delete(
        [Description("Microsoft Search bookmark ID.")] string bookmarkId,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.ConfirmAndDeleteAsync<DeleteBookmarkInput>(bookmarkId,
            async ct => await client.Search.Bookmarks[bookmarkId].DeleteAsync(cancellationToken: ct),
            "Microsoft Search bookmark deleted.", cancellationToken));

    private static Bookmark ToBookmark(BookmarkInput input) => new()
    {
        DisplayName = input.DisplayName.Trim(), WebUrl = input.WebUrl.Trim(), Description = input.Description,
        Keywords = ToKeywords(input.KeywordsCsv), Categories = SplitCsv(input.CategoriesCsv),
        LanguageTags = SplitCsv(input.LanguageTagsCsv)
    };

    private static void Validate(string displayName, string webUrl, string keywordsCsv)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ValidateUrl(webUrl);
        if (SplitCsv(keywordsCsv).Count == 0) throw new ValidationException("At least one keyword is required.");
    }

    private static void ValidateUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ValidationException("webUrl must be an absolute HTTP or HTTPS URL.");
    }

    private static List<string> SplitCsv(string? value) => string.IsNullOrWhiteSpace(value) ? [] :
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();

    private static AnswerKeyword ToKeywords(string value) => new() { Keywords = SplitCsv(value) };

    [Description("Please review the Microsoft Search bookmark fields.")]
    public sealed class BookmarkInput
    {
        [Required, JsonPropertyName("displayName")] public string DisplayName { get; set; } = default!;
        [Required, JsonPropertyName("webUrl")] public string WebUrl { get; set; } = default!;
        [Required, JsonPropertyName("keywordsCsv")] public string KeywordsCsv { get; set; } = default!;
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("categoriesCsv")] public string? CategoriesCsv { get; set; }
        [JsonPropertyName("languageTagsCsv")] public string? LanguageTagsCsv { get; set; }
    }

    [Description("Please review the Microsoft Search bookmark changes.")]
    public sealed class BookmarkPatchInput
    {
        [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
        [JsonPropertyName("webUrl")] public string? WebUrl { get; set; }
        [JsonPropertyName("keywordsCsv")] public string? KeywordsCsv { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("categoriesCsv")] public string? CategoriesCsv { get; set; }
        [JsonPropertyName("languageTagsCsv")] public string? LanguageTagsCsv { get; set; }
    }

    [Description("Please confirm the Microsoft Search bookmark ID to delete: {0}")]
    public sealed class DeleteBookmarkInput : MCPhappey.Common.Models.IHasName
    {
        [Required, JsonPropertyName("name")] public string Name { get; set; } = default!;
    }
}
