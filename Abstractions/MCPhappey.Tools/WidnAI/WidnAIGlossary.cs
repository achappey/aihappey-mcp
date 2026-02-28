using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.WidnAI;

public static class WidnAIGlossary
{
    [Description("Create a WidnAI glossary with optional items. Input is always confirmed through elicitation before execution.")]
    [McpServerTool(
        Name = "widnai_glossary_create",
        Title = "WidnAI glossary create",
        ReadOnly = false,
        OpenWorld = true,
        Destructive = false)]
    public static async Task<CallToolResult?> WidnAI_Glossary_Create(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Glossary name.")] string name,
        [Description("Source locale (e.g., en).")]
        string sourceLocale,
        [Description("Target locale (e.g., es).")]
        string targetLocale,
        [Description("Optional glossary items (term + translation).")]
        List<WidnGlossaryItemInput>? items = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new WidnCreateGlossaryRequest
                {
                    Name = name,
                    SourceLocale = sourceLocale,
                    TargetLocale = targetLocale,
                    Items = items
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            ArgumentException.ThrowIfNullOrWhiteSpace(typed.Name);
            ArgumentException.ThrowIfNullOrWhiteSpace(typed.SourceLocale);
            ArgumentException.ThrowIfNullOrWhiteSpace(typed.TargetLocale);

            var body = new
            {
                name = typed.Name,
                sourceLocale = typed.SourceLocale,
                targetLocale = typed.TargetLocale,
                items = typed.Items?.Select(i => new { term = i.Term, translation = i.Translation }).ToArray()
            };

            var widn = serviceProvider.GetRequiredService<WidnAIClient>();
            var raw = await widn.PostJsonAsync("glossary", body, cancellationToken) ?? new JsonObject();

            return BuildStructuredToolResult(
                operation: "create-glossary",
                input: ToJsonNode(body),
                result: raw);
        });

    [Description("Update a WidnAI glossary. Full required body is always confirmed through elicitation before execution.")]
    [McpServerTool(
        Name = "widnai_glossary_update",
        Title = "WidnAI glossary update",
        ReadOnly = false,
        OpenWorld = true,
        Destructive = false)]
    public static async Task<CallToolResult?> WidnAI_Glossary_Update(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Glossary ID to update.")] string glossaryId,
        [Description("Glossary name.")] string name,
        [Description("Source locale (e.g., en).")]
        string sourceLocale,
        [Description("Target locale (e.g., es).")]
        string targetLocale,
        [Description("Optional glossary items (term + translation).")]
        List<WidnGlossaryItemInput>? items = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new WidnUpdateGlossaryRequest
                {
                    GlossaryId = glossaryId,
                    Name = name,
                    SourceLocale = sourceLocale,
                    TargetLocale = targetLocale,
                    Items = items
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            ArgumentException.ThrowIfNullOrWhiteSpace(typed.GlossaryId);
            ArgumentException.ThrowIfNullOrWhiteSpace(typed.Name);
            ArgumentException.ThrowIfNullOrWhiteSpace(typed.SourceLocale);
            ArgumentException.ThrowIfNullOrWhiteSpace(typed.TargetLocale);

            var body = new
            {
                name = typed.Name,
                sourceLocale = typed.SourceLocale,
                targetLocale = typed.TargetLocale,
                items = typed.Items?.Select(i => new { term = i.Term, translation = i.Translation }).ToArray()
            };

            var path = $"glossary/{Uri.EscapeDataString(typed.GlossaryId)}";
            var widn = serviceProvider.GetRequiredService<WidnAIClient>();
            await widn.PutJsonNoContentAsync(path, body, cancellationToken);

            return BuildStructuredToolResult(
                operation: "update-glossary",
                input: new JsonObject
                {
                    ["glossaryId"] = typed.GlossaryId,
                    ["body"] = ToJsonNode(body)
                },
                result: new JsonObject
                {
                    ["status"] = 204,
                    ["message"] = "Glossary updated successfully."
                });
        });

    [Description("Delete a WidnAI glossary by ID using the default delete confirmation flow.")]
    [McpServerTool(
        Name = "widnai_glossary_delete",
        Title = "WidnAI glossary delete",
        ReadOnly = false,
        OpenWorld = true,
        Destructive = true)]
    public static async Task<CallToolResult?> WidnAI_Glossary_Delete(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Glossary ID to delete.")] string glossaryId,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(glossaryId);

            var widn = serviceProvider.GetRequiredService<WidnAIClient>();
            return await requestContext.ConfirmAndDeleteAsync<ConfirmDeleteWidnGlossary>(
                expectedName: glossaryId,
                deleteAction: async ct => await widn.DeleteNoContentAsync($"glossary/{Uri.EscapeDataString(glossaryId)}", ct),
                successText: $"Glossary '{glossaryId}' deleted successfully.",
                ct: cancellationToken);
        });

    [Description("Add a new item (term + translation) to a WidnAI glossary. Input is always confirmed through elicitation before execution.")]
    [McpServerTool(
        Name = "widnai_glossary_item_create",
        Title = "WidnAI glossary item create",
        ReadOnly = false,
        OpenWorld = true,
        Destructive = false)]
    public static async Task<CallToolResult?> WidnAI_GlossaryItem_Create(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Glossary ID.")] string glossaryId,
        [Description("Source term.")] string term,
        [Description("Translated value.")] string translation,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new WidnCreateGlossaryItemRequest
                {
                    GlossaryId = glossaryId,
                    Term = term,
                    Translation = translation
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            ArgumentException.ThrowIfNullOrWhiteSpace(typed.GlossaryId);
            ArgumentException.ThrowIfNullOrWhiteSpace(typed.Term);
            ArgumentException.ThrowIfNullOrWhiteSpace(typed.Translation);

            var body = new
            {
                term = typed.Term,
                translation = typed.Translation
            };

            var path = $"glossary/{Uri.EscapeDataString(typed.GlossaryId)}/item";
            var widn = serviceProvider.GetRequiredService<WidnAIClient>();
            var raw = await widn.PostJsonAsync(path, body, cancellationToken) ?? new JsonObject();

            return BuildStructuredToolResult(
                operation: "create-glossary-item",
                input: new JsonObject
                {
                    ["glossaryId"] = typed.GlossaryId,
                    ["body"] = ToJsonNode(body)
                },
                result: raw);
        });

    [Description("Update an existing WidnAI glossary item. Full required body is always confirmed through elicitation before execution.")]
    [McpServerTool(
        Name = "widnai_glossary_item_update",
        Title = "WidnAI glossary item update",
        ReadOnly = false,
        OpenWorld = true,
        Destructive = false)]
    public static async Task<CallToolResult?> WidnAI_GlossaryItem_Update(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Glossary ID.")] string glossaryId,
        [Description("Item ID.")] string itemId,
        [Description("Source term.")] string term,
        [Description("Translated value.")] string translation,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new WidnUpdateGlossaryItemRequest
                {
                    GlossaryId = glossaryId,
                    ItemId = itemId,
                    Term = term,
                    Translation = translation
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            ArgumentException.ThrowIfNullOrWhiteSpace(typed.GlossaryId);
            ArgumentException.ThrowIfNullOrWhiteSpace(typed.ItemId);
            ArgumentException.ThrowIfNullOrWhiteSpace(typed.Term);
            ArgumentException.ThrowIfNullOrWhiteSpace(typed.Translation);

            var body = new
            {
                term = typed.Term,
                translation = typed.Translation
            };

            var path = $"glossary/{Uri.EscapeDataString(typed.GlossaryId)}/item/{Uri.EscapeDataString(typed.ItemId)}";
            var widn = serviceProvider.GetRequiredService<WidnAIClient>();
            await widn.PutJsonNoContentAsync(path, body, cancellationToken);

            return BuildStructuredToolResult(
                operation: "update-glossary-item",
                input: new JsonObject
                {
                    ["glossaryId"] = typed.GlossaryId,
                    ["itemId"] = typed.ItemId,
                    ["body"] = ToJsonNode(body)
                },
                result: new JsonObject
                {
                    ["status"] = 204,
                    ["message"] = "Glossary item updated successfully."
                });
        });

    [Description("Delete a specific item from a WidnAI glossary by glossary ID and item ID using the default delete confirmation flow.")]
    [McpServerTool(
        Name = "widnai_glossary_item_delete",
        Title = "WidnAI glossary item delete",
        ReadOnly = false,
        OpenWorld = true,
        Destructive = true)]
    public static async Task<CallToolResult?> WidnAI_GlossaryItem_Delete(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Glossary ID.")] string glossaryId,
        [Description("Item ID.")] string itemId,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(glossaryId);
            ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

            var expected = $"{glossaryId}/{itemId}";
            var widn = serviceProvider.GetRequiredService<WidnAIClient>();
            return await requestContext.ConfirmAndDeleteAsync<ConfirmDeleteWidnGlossaryItem>(
                expectedName: expected,
                deleteAction: async ct => await widn.DeleteNoContentAsync(
                    $"glossary/{Uri.EscapeDataString(glossaryId)}/item/{Uri.EscapeDataString(itemId)}",
                    ct),
                successText: $"Glossary item '{itemId}' deleted successfully from glossary '{glossaryId}'.",
                ct: cancellationToken);
        });

    private static CallToolResult BuildStructuredToolResult(string operation, JsonNode input, JsonNode result)
    {
        var structured = new JsonObject
        {
            ["provider"] = "widnai",
            ["type"] = "glossary",
            ["operation"] = operation,
            ["input"] = input.DeepClone(),
            ["result"] = result.DeepClone()
        };

        return new CallToolResult
        {
            StructuredContent = structured,
            Content =
            [
                JsonSerializer.Serialize(structured, new JsonSerializerOptions { WriteIndented = true }).ToTextContentBlock()
            ]
        };
    }

    private static JsonNode ToJsonNode(object value)
        => JsonSerializer.SerializeToNode(value) ?? new JsonObject();
}

public sealed class WidnGlossaryItemInput
{
    [Required]
    [JsonPropertyName("term")]
    [Description("Source term.")]
    public string? Term { get; set; }

    [Required]
    [JsonPropertyName("translation")]
    [Description("Translated value for the term.")]
    public string? Translation { get; set; }
}

public sealed class WidnCreateGlossaryRequest
{
    [Required]
    [JsonPropertyName("name")]
    [Description("Glossary name.")]
    public string? Name { get; set; }

    [Required]
    [JsonPropertyName("sourceLocale")]
    [Description("Glossary source locale.")]
    public string? SourceLocale { get; set; }

    [Required]
    [JsonPropertyName("targetLocale")]
    [Description("Glossary target locale.")]
    public string? TargetLocale { get; set; }

    [JsonPropertyName("items")]
    [Description("Optional glossary items.")]
    public List<WidnGlossaryItemInput>? Items { get; set; }
}

public sealed class WidnUpdateGlossaryRequest
{
    [Required]
    [JsonPropertyName("glossaryId")]
    [Description("Glossary ID to update.")]
    public string? GlossaryId { get; set; }

    [Required]
    [JsonPropertyName("name")]
    [Description("Glossary name.")]
    public string? Name { get; set; }

    [Required]
    [JsonPropertyName("sourceLocale")]
    [Description("Glossary source locale.")]
    public string? SourceLocale { get; set; }

    [Required]
    [JsonPropertyName("targetLocale")]
    [Description("Glossary target locale.")]
    public string? TargetLocale { get; set; }

    [JsonPropertyName("items")]
    [Description("Optional glossary items.")]
    public List<WidnGlossaryItemInput>? Items { get; set; }
}

public sealed class WidnCreateGlossaryItemRequest
{
    [Required]
    [JsonPropertyName("glossaryId")]
    [Description("Glossary ID.")]
    public string? GlossaryId { get; set; }

    [Required]
    [JsonPropertyName("term")]
    [Description("Source term.")]
    public string? Term { get; set; }

    [Required]
    [JsonPropertyName("translation")]
    [Description("Translated value.")]
    public string? Translation { get; set; }
}

public sealed class WidnUpdateGlossaryItemRequest
{
    [Required]
    [JsonPropertyName("glossaryId")]
    [Description("Glossary ID.")]
    public string? GlossaryId { get; set; }

    [Required]
    [JsonPropertyName("itemId")]
    [Description("Item ID.")]
    public string? ItemId { get; set; }

    [Required]
    [JsonPropertyName("term")]
    [Description("Source term.")]
    public string? Term { get; set; }

    [Required]
    [JsonPropertyName("translation")]
    [Description("Translated value.")]
    public string? Translation { get; set; }
}

public sealed class ConfirmDeleteWidnGlossary : IHasName
{
    [Required]
    [JsonPropertyName("name")]
    [Description("Type the exact glossary ID to confirm deletion.")]
    public string? Name { get; set; }
}

public sealed class ConfirmDeleteWidnGlossaryItem : IHasName
{
    [Required]
    [JsonPropertyName("name")]
    [Description("Type '<glossaryId>/<itemId>' exactly to confirm glossary item deletion.")]
    public string? Name { get; set; }
}
