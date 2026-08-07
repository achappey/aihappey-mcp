using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.Search;

public static class GraphSearchAnswers
{
    [Description("Create an organizational acronym answer in Microsoft Search.")]
    [McpServerTool(Title = "Create Microsoft Search acronym", Name = "graph_search_acronyms_create",
        Destructive = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(JsonElement))]
    public static async Task<CallToolResult?> GraphSearchAcronyms_Create(
        [Description("The acronym, abbreviation, or initialism shown in search results.")] string displayName,
        [Description("The expansion or meaning of the acronym.")] string standsFor,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional explanatory text.")] string? description = null,
        [Description("Optional absolute HTTP or HTTPS URL containing more information.")] string? webUrl = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (input, notAccepted, _) = await requestContext.Server.TryElicit(new AcronymInput
            {
                DisplayName = displayName, StandsFor = standsFor, Description = description, WebUrl = webUrl
            }, cancellationToken);
            ThrowIfNotAccepted(notAccepted, input);
            ValidateAcronym(input!);
            return await SendJsonAsync(serviceProvider, requestContext, HttpMethod.Post, "search/acronyms",
                new { displayName = input!.DisplayName.Trim(), standsFor = input.StandsFor.Trim(), input.Description, webUrl = NormalizeUrl(input.WebUrl) },
                cancellationToken);
        }));

    [Description("Update an organizational acronym answer in Microsoft Search. Only supplied values are changed.")]
    [McpServerTool(Title = "Update Microsoft Search acronym", Name = "graph_search_acronyms_update",
        Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(JsonElement))]
    public static async Task<CallToolResult?> GraphSearchAcronyms_Update(
        [Description("Microsoft Search acronym ID.")] string acronymId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("New acronym or abbreviation.")] string? displayName = null,
        [Description("New expansion or meaning.")] string? standsFor = null,
        [Description("New explanatory text.")] string? description = null,
        [Description("New absolute HTTP or HTTPS information URL.")] string? webUrl = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(acronymId);
            if (displayName is null && standsFor is null && description is null && webUrl is null)
                throw new ValidationException("At least one acronym field must be provided.");

            var (input, notAccepted, _) = await requestContext.Server.TryElicit(new AcronymPatchInput
            {
                DisplayName = displayName, StandsFor = standsFor, Description = description, WebUrl = webUrl
            }, cancellationToken);
            ThrowIfNotAccepted(notAccepted, input);
            if (input!.DisplayName is not null) ArgumentException.ThrowIfNullOrWhiteSpace(input.DisplayName);
            if (input.StandsFor is not null) ArgumentException.ThrowIfNullOrWhiteSpace(input.StandsFor);
            if (input.WebUrl is not null) ValidateUrl(input.WebUrl);

            return await SendJsonAsync(serviceProvider, requestContext, HttpMethod.Patch,
                $"search/acronyms/{Uri.EscapeDataString(acronymId)}", ToPatch(input), cancellationToken);
        }));

    [Description("Delete an organizational acronym answer from Microsoft Search.")]
    [McpServerTool(Title = "Delete Microsoft Search acronym", Name = "graph_search_acronyms_delete",
        Destructive = true, Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphSearchAcronyms_Delete(
        [Description("Microsoft Search acronym ID.")] string acronymId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await requestContext.ConfirmAndDeleteAsync<DeleteSearchAnswerInput>(acronymId,
            async ct => await SendNoContentAsync(serviceProvider, requestContext, HttpMethod.Delete,
                $"search/acronyms/{Uri.EscapeDataString(acronymId)}", ct),
            "Microsoft Search acronym deleted.", cancellationToken);

    [Description("Create an organizational question-and-answer result in Microsoft Search.")]
    [McpServerTool(Title = "Create Microsoft Search Q&A", Name = "graph_search_qnas_create",
        Destructive = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(JsonElement))]
    public static async Task<CallToolResult?> GraphSearchQnas_Create(
        [Description("Question or concise title shown in Microsoft Search.")] string displayName,
        [Description("Answer text shown in Microsoft Search.")] string description,
        [Description("Comma-separated search keywords that trigger this Q&A.")] string keywordsCsv,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional absolute HTTP or HTTPS URL containing more information.")] string? webUrl = null,
        [Description("Optional comma-separated language tags, such as en-US,nl-NL.")] string? languageTagsCsv = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (input, notAccepted, _) = await requestContext.Server.TryElicit(new QnaInput
            {
                DisplayName = displayName, Description = description, KeywordsCsv = keywordsCsv,
                WebUrl = webUrl, LanguageTagsCsv = languageTagsCsv
            }, cancellationToken);
            ThrowIfNotAccepted(notAccepted, input);
            ValidateQna(input!);
            return await SendJsonAsync(serviceProvider, requestContext, HttpMethod.Post, "search/qnas",
                new
                {
                    displayName = input!.DisplayName.Trim(), description = input.Description.Trim(),
                    keywords = ToKeywords(input.KeywordsCsv), webUrl = NormalizeUrl(input.WebUrl),
                    languageTags = SplitCsv(input.LanguageTagsCsv)
                }, cancellationToken);
        }));

    [Description("Update an organizational question-and-answer result in Microsoft Search. Only supplied values are changed.")]
    [McpServerTool(Title = "Update Microsoft Search Q&A", Name = "graph_search_qnas_update",
        Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(JsonElement))]
    public static async Task<CallToolResult?> GraphSearchQnas_Update(
        [Description("Microsoft Search Q&A ID.")] string qnaId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("New question or title.")] string? displayName = null,
        [Description("New answer text.")] string? description = null,
        [Description("Replacement comma-separated search keywords.")] string? keywordsCsv = null,
        [Description("New absolute HTTP or HTTPS information URL.")] string? webUrl = null,
        [Description("Replacement comma-separated language tags.")] string? languageTagsCsv = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(qnaId);
            if (displayName is null && description is null && keywordsCsv is null && webUrl is null && languageTagsCsv is null)
                throw new ValidationException("At least one Q&A field must be provided.");

            var (input, notAccepted, _) = await requestContext.Server.TryElicit(new QnaPatchInput
            {
                DisplayName = displayName, Description = description, KeywordsCsv = keywordsCsv,
                WebUrl = webUrl, LanguageTagsCsv = languageTagsCsv
            }, cancellationToken);
            ThrowIfNotAccepted(notAccepted, input);
            if (input!.DisplayName is not null) ArgumentException.ThrowIfNullOrWhiteSpace(input.DisplayName);
            if (input.Description is not null) ArgumentException.ThrowIfNullOrWhiteSpace(input.Description);
            if (input.KeywordsCsv is not null && SplitCsv(input.KeywordsCsv).Count == 0)
                throw new ValidationException("At least one keyword is required when replacing keywords.");
            if (input.WebUrl is not null) ValidateUrl(input.WebUrl);

            return await SendJsonAsync(serviceProvider, requestContext, HttpMethod.Patch,
                $"search/qnas/{Uri.EscapeDataString(qnaId)}", ToPatch(input), cancellationToken);
        }));

    [Description("Delete an organizational question-and-answer result from Microsoft Search.")]
    [McpServerTool(Title = "Delete Microsoft Search Q&A", Name = "graph_search_qnas_delete",
        Destructive = true, Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphSearchQnas_Delete(
        [Description("Microsoft Search Q&A ID.")] string qnaId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await requestContext.ConfirmAndDeleteAsync<DeleteSearchAnswerInput>(qnaId,
            async ct => await SendNoContentAsync(serviceProvider, requestContext, HttpMethod.Delete,
                $"search/qnas/{Uri.EscapeDataString(qnaId)}", ct),
            "Microsoft Search Q&A deleted.", cancellationToken);

    private static async Task<JsonElement> SendJsonAsync(IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext, HttpMethod method, string relativePath,
        object body, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(serviceProvider, requestContext, method, relativePath, body, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static async Task SendNoContentAsync(IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext, HttpMethod method, string relativePath,
        CancellationToken cancellationToken) =>
        (await SendAsync(serviceProvider, requestContext, method, relativePath, null, cancellationToken)).Dispose();

    private static async Task<HttpResponseMessage> SendAsync(IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext, HttpMethod method, string relativePath,
        object? body, CancellationToken cancellationToken)
    {
        var client = await serviceProvider.GetGraphHttpClient(requestContext.Server);
        using var request = new HttpRequestMessage(method, $"https://graph.microsoft.com/beta/{relativePath}");
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, MediaTypeNames.Application.Json);
        var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return response;
    }

    private static object ToPatch(AcronymPatchInput input)
    {
        var patch = new Dictionary<string, object?>();
        if (input.DisplayName is not null) patch["displayName"] = input.DisplayName.Trim();
        if (input.StandsFor is not null) patch["standsFor"] = input.StandsFor.Trim();
        if (input.Description is not null) patch["description"] = input.Description;
        if (input.WebUrl is not null) patch["webUrl"] = NormalizeUrl(input.WebUrl);
        return patch;
    }

    private static object ToPatch(QnaPatchInput input)
    {
        var patch = new Dictionary<string, object?>();
        if (input.DisplayName is not null) patch["displayName"] = input.DisplayName.Trim();
        if (input.Description is not null) patch["description"] = input.Description.Trim();
        if (input.KeywordsCsv is not null) patch["keywords"] = ToKeywords(input.KeywordsCsv);
        if (input.WebUrl is not null) patch["webUrl"] = NormalizeUrl(input.WebUrl);
        if (input.LanguageTagsCsv is not null) patch["languageTags"] = SplitCsv(input.LanguageTagsCsv);
        return patch;
    }

    private static void ValidateAcronym(AcronymInput input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input.DisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.StandsFor);
        if (input.WebUrl is not null) ValidateUrl(input.WebUrl);
    }

    private static void ValidateQna(QnaInput input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input.DisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Description);
        if (SplitCsv(input.KeywordsCsv).Count == 0) throw new ValidationException("At least one keyword is required.");
        if (input.WebUrl is not null) ValidateUrl(input.WebUrl);
    }

    private static void ValidateUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ValidationException("webUrl must be an absolute HTTP or HTTPS URL.");
    }

    private static string? NormalizeUrl(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static List<string> SplitCsv(string? value) => string.IsNullOrWhiteSpace(value) ? [] :
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();
    private static object ToKeywords(string value) => new { keywords = SplitCsv(value) };

    private static void ThrowIfNotAccepted<T>(object? notAccepted, T? input) where T : class
    {
        if (notAccepted is not null || input is null) throw new OperationCanceledException("The elicitation was not accepted.");
    }

    [Description("Please review the Microsoft Search acronym fields.")]
    public sealed class AcronymInput
    {
        [Required, JsonPropertyName("displayName")] public string DisplayName { get; set; } = default!;
        [Required, JsonPropertyName("standsFor")] public string StandsFor { get; set; } = default!;
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("webUrl")] public string? WebUrl { get; set; }
    }

    [Description("Please review the Microsoft Search acronym changes.")]
    public sealed class AcronymPatchInput
    {
        [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
        [JsonPropertyName("standsFor")] public string? StandsFor { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("webUrl")] public string? WebUrl { get; set; }
    }

    [Description("Please review the Microsoft Search Q&A fields.")]
    public sealed class QnaInput
    {
        [Required, JsonPropertyName("displayName")] public string DisplayName { get; set; } = default!;
        [Required, JsonPropertyName("description")] public string Description { get; set; } = default!;
        [Required, JsonPropertyName("keywordsCsv")] public string KeywordsCsv { get; set; } = default!;
        [JsonPropertyName("webUrl")] public string? WebUrl { get; set; }
        [JsonPropertyName("languageTagsCsv")] public string? LanguageTagsCsv { get; set; }
    }

    [Description("Please review the Microsoft Search Q&A changes.")]
    public sealed class QnaPatchInput
    {
        [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("keywordsCsv")] public string? KeywordsCsv { get; set; }
        [JsonPropertyName("webUrl")] public string? WebUrl { get; set; }
        [JsonPropertyName("languageTagsCsv")] public string? LanguageTagsCsv { get; set; }
    }

    [Description("Please confirm the Microsoft Search answer ID to delete: {0}")]
    public sealed class DeleteSearchAnswerInput : MCPhappey.Common.Models.IHasName
    {
        [Required, JsonPropertyName("name")] public string Name { get; set; } = default!;
    }
}
