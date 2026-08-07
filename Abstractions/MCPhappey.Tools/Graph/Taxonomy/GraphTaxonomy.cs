using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using Microsoft.Graph.Beta.Models.TermStore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Term = Microsoft.Graph.Beta.Models.TermStore.Term;
using TermSet = Microsoft.Graph.Beta.Models.TermStore.Set;

namespace MCPhappey.Tools.Graph.Taxonomy;

public static class GraphTaxonomy
{
    [Description("Create a term set in a SharePoint taxonomy term-store group.")]
    [McpServerTool(Title = "Create SharePoint taxonomy term set", Name = "graph_taxonomy_term_sets_create",
        Destructive = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(TermSet))]
    public static async Task<CallToolResult?> GraphTaxonomy_CreateTermSet(
        [Description("Taxonomy group ID that will own the term set.")] string groupId,
        [Description("Default term-set name.")] string displayName,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("BCP 47 language tag for the name.")] string languageTag = "en-US",
        [Description("Optional term-set description.")] string? description = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (input, rejected, _) = await requestContext.Server.TryElicit(new TermSetInput
            { DisplayName = displayName, LanguageTag = languageTag, Description = description }, cancellationToken);
            if (rejected is not null || input is null) return default(TermSet);
            ValidateNameAndLanguage(input.DisplayName, input.LanguageTag);

            return await client.TermStore.Groups[groupId].Sets.PostAsync(new TermSet
            {
                LocalizedNames = [new LocalizedName { Name = input.DisplayName.Trim(), LanguageTag = input.LanguageTag.Trim() }],
                Description = input.Description
            }, cancellationToken: cancellationToken);
        })));

    [Description("Update a SharePoint taxonomy term set. Only supplied values are changed.")]
    [McpServerTool(Title = "Update SharePoint taxonomy term set", Name = "graph_taxonomy_term_sets_update",
        Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(TermSet))]
    public static async Task<CallToolResult?> GraphTaxonomy_UpdateTermSet(
        [Description("Taxonomy term-set ID.")] string setId,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Replacement default term-set name.")] string? displayName = null,
        [Description("BCP 47 language tag used with displayName.")] string languageTag = "en-US",
        [Description("Replacement description.")] string? description = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            if (displayName is null && description is null) throw new ValidationException("A display name or description is required.");
            var (input, rejected, _) = await requestContext.Server.TryElicit(new TermSetPatchInput
            { DisplayName = displayName, LanguageTag = languageTag, Description = description }, cancellationToken);
            if (rejected is not null || input is null) return default(TermSet);
            if (input.DisplayName is not null) ValidateNameAndLanguage(input.DisplayName, input.LanguageTag);

            return await client.TermStore.Sets[setId].PatchAsync(new TermSet
            {
                LocalizedNames = input.DisplayName is null ? null :
                    [new LocalizedName { Name = input.DisplayName.Trim(), LanguageTag = input.LanguageTag.Trim() }],
                Description = input.Description
            }, cancellationToken: cancellationToken);
        })));

    [Description("Delete a SharePoint taxonomy term set.")]
    [McpServerTool(Title = "Delete SharePoint taxonomy term set", Name = "graph_taxonomy_term_sets_delete",
        Destructive = true, Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphTaxonomy_DeleteTermSet(
        [Description("Taxonomy term-set ID.")] string setId,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.ConfirmAndDeleteAsync<DeleteTaxonomyInput>(setId,
            async ct => await client.TermStore.Sets[setId].DeleteAsync(cancellationToken: ct),
            "SharePoint taxonomy term set deleted.", cancellationToken));

    [Description("Create a top-level term in a SharePoint taxonomy term set.")]
    [McpServerTool(Title = "Create SharePoint taxonomy term", Name = "graph_taxonomy_terms_create",
        Destructive = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(Term))]
    public static async Task<CallToolResult?> GraphTaxonomy_CreateTerm(
        [Description("Taxonomy term-set ID.")] string setId,
        [Description("Default term label.")] string label,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("BCP 47 language tag for the label.")] string languageTag = "en-US",
        [Description("Optional localized term description.")] string? description = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (input, rejected, _) = await requestContext.Server.TryElicit(new TermInput
            { Label = label, LanguageTag = languageTag, Description = description }, cancellationToken);
            if (rejected is not null || input is null) return default(Term);
            ValidateNameAndLanguage(input.Label, input.LanguageTag);

            return await client.TermStore.Sets[setId].Terms.PostAsync(ToTerm(input), cancellationToken: cancellationToken);
        })));

    [Description("Update a SharePoint taxonomy term's localized label or description.")]
    [McpServerTool(Title = "Update SharePoint taxonomy term", Name = "graph_taxonomy_terms_update",
        Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(Term))]
    public static async Task<CallToolResult?> GraphTaxonomy_UpdateTerm(
        [Description("Taxonomy term-set ID.")] string setId,
        [Description("Taxonomy term ID.")] string termId,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Replacement default label.")] string? label = null,
        [Description("BCP 47 language tag used with label or description.")] string languageTag = "en-US",
        [Description("Replacement localized description.")] string? description = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            if (label is null && description is null) throw new ValidationException("A label or description is required.");
            var (input, rejected, _) = await requestContext.Server.TryElicit(new TermPatchInput
            { Label = label, LanguageTag = languageTag, Description = description }, cancellationToken);
            if (rejected is not null || input is null) return default(Term);
            ValidateLanguage(input.LanguageTag);
            if (input.Label is not null) ArgumentException.ThrowIfNullOrWhiteSpace(input.Label);

            return await client.TermStore.Sets[setId].Terms[termId].PatchAsync(new Term
            {
                Labels = input.Label is null ? null : [new LocalizedLabel
                { Name = input.Label.Trim(), LanguageTag = input.LanguageTag.Trim(), IsDefault = true }],
                Descriptions = input.Description is null ? null : [new LocalizedDescription
                { Description = input.Description, LanguageTag = input.LanguageTag.Trim() }]
            }, cancellationToken: cancellationToken);
        })));

    [Description("Delete a SharePoint taxonomy term from a term set.")]
    [McpServerTool(Title = "Delete SharePoint taxonomy term", Name = "graph_taxonomy_terms_delete",
        Destructive = true, Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphTaxonomy_DeleteTerm(
        [Description("Taxonomy term-set ID.")] string setId,
        [Description("Taxonomy term ID.")] string termId,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.ConfirmAndDeleteAsync<DeleteTaxonomyInput>(termId,
            async ct => await client.TermStore.Sets[setId].Terms[termId].DeleteAsync(cancellationToken: ct),
            "SharePoint taxonomy term deleted.", cancellationToken));

    private static Term ToTerm(TermInput input) => new()
    {
        Labels = [new LocalizedLabel { Name = input.Label.Trim(), LanguageTag = input.LanguageTag.Trim(), IsDefault = true }],
        Descriptions = input.Description is null ? null :
            [new LocalizedDescription { Description = input.Description, LanguageTag = input.LanguageTag.Trim() }]
    };

    private static void ValidateNameAndLanguage(string name, string languageTag)
    { ArgumentException.ThrowIfNullOrWhiteSpace(name); ValidateLanguage(languageTag); }
    private static void ValidateLanguage(string languageTag)
    { ArgumentException.ThrowIfNullOrWhiteSpace(languageTag); }

    [Description("Please review the taxonomy term-set fields.")]
    public sealed class TermSetInput
    {
        [Required, JsonPropertyName("displayName")] public string DisplayName { get; set; } = default!;
        [Required, JsonPropertyName("languageTag")] public string LanguageTag { get; set; } = "en-US";
        [JsonPropertyName("description")] public string? Description { get; set; }
    }
    [Description("Please review the taxonomy term-set changes.")]
    public sealed class TermSetPatchInput
    {
        [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
        [Required, JsonPropertyName("languageTag")] public string LanguageTag { get; set; } = "en-US";
        [JsonPropertyName("description")] public string? Description { get; set; }
    }
    [Description("Please review the taxonomy term fields.")]
    public sealed class TermInput
    {
        [Required, JsonPropertyName("label")] public string Label { get; set; } = default!;
        [Required, JsonPropertyName("languageTag")] public string LanguageTag { get; set; } = "en-US";
        [JsonPropertyName("description")] public string? Description { get; set; }
    }
    [Description("Please review the taxonomy term changes.")]
    public sealed class TermPatchInput
    {
        [JsonPropertyName("label")] public string? Label { get; set; }
        [Required, JsonPropertyName("languageTag")] public string LanguageTag { get; set; } = "en-US";
        [JsonPropertyName("description")] public string? Description { get; set; }
    }
    [Description("Please confirm the taxonomy object ID to delete: {0}")]
    public sealed class DeleteTaxonomyInput : MCPhappey.Common.Models.IHasName
    {
        [Required, JsonPropertyName("name")] public string Name { get; set; } = default!;
    }
}
