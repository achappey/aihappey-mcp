using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.DeepL;

public static class DeepLGlossaries
{
    [Description("Create a DeepL multilingual glossary with one required dictionary and an optional second dictionary. Parameters are always confirmed before execution.")]
    [McpServerTool(
        Title = "DeepL create glossary",
        Name = "deepl_glossaries_create",
        OpenWorld = true,
        ReadOnly = false,
        Destructive = false)]
    public static async Task<CallToolResult?> DeepL_Glossaries_Create(
        [Description("Glossary display name.")] string name,
        [Description("Primary dictionary source language (e.g. en).")]
        string sourceLang,
        [Description("Primary dictionary target language (e.g. de).")]
        string targetLang,
        [Description("Primary dictionary entries string in chosen format.")]
        string entries,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Primary dictionary entries format: tsv or csv.")]
        string entriesFormat = "tsv",
        [Description("Optional second dictionary source language.")]
        string? sourceLang2 = null,
        [Description("Optional second dictionary target language.")]
        string? targetLang2 = null,
        [Description("Optional second dictionary entries string.")]
        string? entries2 = null,
        [Description("Optional second dictionary entries format: tsv or csv.")]
        string? entriesFormat2 = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, _, _) = await requestContext.Server.TryElicit(new CreateGlossaryRequest
            {
                Name = name,
                SourceLang = sourceLang,
                TargetLang = targetLang,
                Entries = entries,
                EntriesFormat = entriesFormat,
                SourceLang2 = sourceLang2,
                TargetLang2 = targetLang2,
                Entries2 = entries2,
                EntriesFormat2 = entriesFormat2,
             
            }, cancellationToken);

            ValidateRequired(typed.Name, nameof(name));
            ValidateRequired(typed.SourceLang, nameof(sourceLang));
            ValidateRequired(typed.TargetLang, nameof(targetLang));
            ValidateRequired(typed.Entries, nameof(entries));

            var dictionaries = new JsonArray
            {
                BuildDictionary(
                    typed.SourceLang!,
                    typed.TargetLang!,
                    typed.Entries!,
                    typed.EntriesFormat)
            };

            var includeSecond =
                !string.IsNullOrWhiteSpace(typed.SourceLang2) ||
                !string.IsNullOrWhiteSpace(typed.TargetLang2) ||
                !string.IsNullOrWhiteSpace(typed.Entries2);

            if (includeSecond)
            {
                ValidateRequired(typed.SourceLang2, nameof(sourceLang2));
                ValidateRequired(typed.TargetLang2, nameof(targetLang2));
                ValidateRequired(typed.Entries2, nameof(entries2));

                dictionaries.Add(BuildDictionary(
                    typed.SourceLang2!,
                    typed.TargetLang2!,
                    typed.Entries2!,
                    typed.EntriesFormat2));
            }

            var deepL = serviceProvider.GetRequiredService<DeepLClient>();
            var json = await deepL.CreateMultilingualGlossaryAsync(
                typed.Name!,
                dictionaries,
                cancellationToken);

            return new CallToolResult
            {
                StructuredContent = json,
                Content = ["DeepL glossary created successfully.".ToTextContentBlock()]
            };
        });

    [Description("Edit glossary details (name and/or one dictionary) for a DeepL glossary. Parameters are always confirmed before execution.")]
    [McpServerTool(
        Title = "DeepL edit glossary",
        Name = "deepl_glossaries_edit",
        OpenWorld = true,
        ReadOnly = false,
        Destructive = false)]
    public static async Task<CallToolResult?> DeepL_Glossaries_Edit(
        [Description("Glossary ID to edit.")] string glossaryId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional new glossary name.")] string? name = null,
        [Description("Optional dictionary source language.")] string? sourceLang = null,
        [Description("Optional dictionary target language.")] string? targetLang = null,
        [Description("Optional dictionary entries string.")] string? entries = null,
        [Description("Optional dictionary entries format: tsv or csv.")] string? entriesFormat = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, _, _) = await requestContext.Server.TryElicit(new PatchGlossaryRequest
            {
                GlossaryId = glossaryId,
                Name = name,
                SourceLang = sourceLang,
                TargetLang = targetLang,
                Entries = entries,
                EntriesFormat = entriesFormat
            }, cancellationToken);

            ValidateRequired(typed.GlossaryId, nameof(glossaryId));

            var hasName = !string.IsNullOrWhiteSpace(typed.Name);
            var hasDictionaryField =
                !string.IsNullOrWhiteSpace(typed.SourceLang) ||
                !string.IsNullOrWhiteSpace(typed.TargetLang) ||
                !string.IsNullOrWhiteSpace(typed.Entries) ||
                !string.IsNullOrWhiteSpace(typed.EntriesFormat);

            JsonArray? dictionaries = null;
            if (hasDictionaryField)
            {
                ValidateRequired(typed.SourceLang, nameof(sourceLang));
                ValidateRequired(typed.TargetLang, nameof(targetLang));
                ValidateRequired(typed.Entries, nameof(entries));

                dictionaries = new JsonArray
                {
                    BuildDictionary(typed.SourceLang!, typed.TargetLang!, typed.Entries!, typed.EntriesFormat)
                };
            }

            if (!hasName && dictionaries == null)
                return "At least one update is required: name or a complete dictionary (sourceLang, targetLang, entries).".ToErrorCallToolResponse();

            var deepL = serviceProvider.GetRequiredService<DeepLClient>();
            var json = await deepL.PatchMultilingualGlossaryAsync(
                typed.GlossaryId!,
                typed.Name,
                dictionaries,
                cancellationToken);

            return new CallToolResult
            {
                StructuredContent = json,
                Content = ["DeepL glossary updated successfully.".ToTextContentBlock()]
            };
        });

    [Description("Replace or create a single dictionary in an existing DeepL glossary. Parameters are always confirmed before execution.")]
    [McpServerTool(
        Title = "DeepL replace glossary dictionary",
        Name = "deepl_glossaries_replace_dictionary",
        OpenWorld = true,
        ReadOnly = false,
        Destructive = false)]
    public static async Task<CallToolResult?> DeepL_Glossaries_ReplaceDictionary(
        [Description("Glossary ID that contains the dictionary.")] string glossaryId,
        [Description("Dictionary source language.")] string sourceLang,
        [Description("Dictionary target language.")] string targetLang,
        [Description("Dictionary entries string.")] string entries,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Dictionary entries format: tsv or csv.")]
        string entriesFormat = "tsv",
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, _, _) = await requestContext.Server.TryElicit(new ReplaceDictionaryRequest
            {
                GlossaryId = glossaryId,
                SourceLang = sourceLang,
                TargetLang = targetLang,
                Entries = entries,
                EntriesFormat = entriesFormat
            }, cancellationToken);

            ValidateRequired(typed.GlossaryId, nameof(glossaryId));
            ValidateRequired(typed.SourceLang, nameof(sourceLang));
            ValidateRequired(typed.TargetLang, nameof(targetLang));
            ValidateRequired(typed.Entries, nameof(entries));

            var deepL = serviceProvider.GetRequiredService<DeepLClient>();
            var json = await deepL.ReplaceDictionaryAsync(
                typed.GlossaryId!,
                NormalizeLang(typed.SourceLang!),
                NormalizeLang(typed.TargetLang!),
                typed.Entries!,
                NormalizeEntriesFormat(typed.EntriesFormat),
                cancellationToken);

            return new CallToolResult
            {
                StructuredContent = json,
                Content = ["DeepL glossary dictionary replaced successfully.".ToTextContentBlock()]
            };
        });

    [Description("Delete a glossary by glossary ID using default delete confirmation flow.")]
    [McpServerTool(
        Title = "DeepL delete glossary",
        Name = "deepl_glossaries_delete",
        OpenWorld = true,
        ReadOnly = false,
        Destructive = true)]
    public static async Task<CallToolResult?> DeepL_Glossaries_Delete(
        [Description("Glossary ID to delete.")] string glossaryId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            ValidateRequired(glossaryId, nameof(glossaryId));
            var deepL = serviceProvider.GetRequiredService<DeepLClient>();

            return await requestContext.ConfirmAndDeleteAsync<ConfirmDeleteGlossary>(
                glossaryId,
                async ct => await deepL.DeleteGlossaryAsync(glossaryId, ct),
                $"Glossary '{glossaryId}' deleted successfully.",
                cancellationToken);
        });

    [Description("Delete a dictionary from a glossary by language pair.")]
    [McpServerTool(
        Title = "DeepL delete glossary dictionary",
        Name = "deepl_glossaries_delete_dictionary",
        OpenWorld = true,
        ReadOnly = false,
        Destructive = true)]
    public static async Task<CallToolResult?> DeepL_Glossaries_DeleteDictionary(
        [Description("Glossary ID that contains the dictionary.")] string glossaryId,
        [Description("Dictionary source language.")] string sourceLang,
        [Description("Dictionary target language.")] string targetLang,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            ValidateRequired(glossaryId, nameof(glossaryId));
            ValidateRequired(sourceLang, nameof(sourceLang));
            ValidateRequired(targetLang, nameof(targetLang));

            var deepL = serviceProvider.GetRequiredService<DeepLClient>();
            var expected = $"{glossaryId}:{NormalizeLang(sourceLang)}->{NormalizeLang(targetLang)}";

            return await requestContext.ConfirmAndDeleteAsync<ConfirmDeleteDictionary>(
                expected,
                async ct => await deepL.DeleteDictionaryAsync(
                    glossaryId,
                    NormalizeLang(sourceLang),
                    NormalizeLang(targetLang),
                    ct),
                $"Dictionary '{NormalizeLang(sourceLang)}->{NormalizeLang(targetLang)}' deleted from glossary '{glossaryId}'.",
                cancellationToken);
        });

    private static JsonObject BuildDictionary(string sourceLang, string targetLang, string entries, string? entriesFormat)
        => new()
        {
            ["source_lang"] = NormalizeLang(sourceLang),
            ["target_lang"] = NormalizeLang(targetLang),
            ["entries"] = entries,
            ["entries_format"] = NormalizeEntriesFormat(entriesFormat)
        };

    private static string NormalizeEntriesFormat(string? value)
    {
        var format = (value ?? "tsv").Trim().ToLowerInvariant();
        return format is "csv" ? "csv" : "tsv";
    }

    private static string NormalizeLang(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Contains('-')) return trimmed.ToUpperInvariant();
        return trimmed.ToLowerInvariant();
    }

    private static void ValidateRequired(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{fieldName} is required.");
    }

    public sealed class CreateGlossaryRequest
    {
        [Required]
        [JsonPropertyName("name")]
        [Description("Glossary display name.")]
        public string? Name { get; set; }

        [Required]
        [JsonPropertyName("sourceLang")]
        [Description("Primary dictionary source language.")]
        public string? SourceLang { get; set; }

        [Required]
        [JsonPropertyName("targetLang")]
        [Description("Primary dictionary target language.")]
        public string? TargetLang { get; set; }

        [Required]
        [JsonPropertyName("entries")]
        [Description("Primary dictionary entries string.")]
        public string? Entries { get; set; }

        [JsonPropertyName("entriesFormat")]
        [Description("Primary entries format: tsv or csv.")]
        public string? EntriesFormat { get; set; } = "tsv";

        [JsonPropertyName("sourceLang2")]
        [Description("Optional second dictionary source language.")]
        public string? SourceLang2 { get; set; }

        [JsonPropertyName("targetLang2")]
        [Description("Optional second dictionary target language.")]
        public string? TargetLang2 { get; set; }

        [JsonPropertyName("entries2")]
        [Description("Optional second dictionary entries string.")]
        public string? Entries2 { get; set; }

        [JsonPropertyName("entriesFormat2")]
        [Description("Optional second entries format: tsv or csv.")]
        public string? EntriesFormat2 { get; set; }

    }

    public sealed class PatchGlossaryRequest
    {
        [Required]
        [JsonPropertyName("glossaryId")]
        [Description("Glossary ID to update.")]
        public string? GlossaryId { get; set; }

        [JsonPropertyName("name")]
        [Description("Optional new glossary name.")]
        public string? Name { get; set; }

        [JsonPropertyName("sourceLang")]
        [Description("Optional dictionary source language.")]
        public string? SourceLang { get; set; }

        [JsonPropertyName("targetLang")]
        [Description("Optional dictionary target language.")]
        public string? TargetLang { get; set; }

        [JsonPropertyName("entries")]
        [Description("Optional dictionary entries string.")]
        public string? Entries { get; set; }

        [JsonPropertyName("entriesFormat")]
        [Description("Optional dictionary entries format: tsv or csv.")]
        public string? EntriesFormat { get; set; }     
    }

    public sealed class ReplaceDictionaryRequest
    {
        [Required]
        [JsonPropertyName("glossaryId")]
        [Description("Glossary ID.")]
        public string? GlossaryId { get; set; }

        [Required]
        [JsonPropertyName("sourceLang")]
        [Description("Dictionary source language.")]
        public string? SourceLang { get; set; }

        [Required]
        [JsonPropertyName("targetLang")]
        [Description("Dictionary target language.")]
        public string? TargetLang { get; set; }

        [Required]
        [JsonPropertyName("entries")]
        [Description("Dictionary entries string.")]
        public string? Entries { get; set; }

        [JsonPropertyName("entriesFormat")]
        [Description("Dictionary entries format: tsv or csv.")]
        public string? EntriesFormat { get; set; } = "tsv";

    }

    public sealed class ConfirmDeleteGlossary : IHasName
    {
        [Required]
        [JsonPropertyName("name")]
        [Description("Type the exact glossary ID to confirm deletion.")]
        public string? Name { get; set; }
    }

    public sealed class ConfirmDeleteDictionary : IHasName
    {
        [Required]
        [JsonPropertyName("name")]
        [Description("Type '<glossaryId>:<sourceLang>-><targetLang>' exactly to confirm dictionary deletion.")]
        public string? Name { get; set; }
    }
}

