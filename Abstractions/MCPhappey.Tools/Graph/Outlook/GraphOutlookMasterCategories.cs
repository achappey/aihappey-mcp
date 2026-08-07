using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using Microsoft.Graph.Beta.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.Outlook;

public static class GraphOutlookMasterCategories
{
    [Description("Create a category in the signed-in user's Outlook master category list.")]
    [McpServerTool(Title = "Create Outlook master category", Name = "graph_outlook_master_categories_create",
        UseStructuredContent = true, OutputSchemaType = typeof(OutlookCategory),
        Destructive = false, OpenWorld = false)]
    public static async Task<CallToolResult?> Create(
        [Description("Category display name.")] string displayName,
        [Description("Outlook category color preset.")] CategoryColor color,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (input, rejected, _) = await requestContext.Server.TryElicit(
                new MasterCategoryInput { DisplayName = displayName, Color = color }, cancellationToken);
            ThrowIfRejected(input, rejected);
            ArgumentException.ThrowIfNullOrWhiteSpace(input!.DisplayName);
            return await client.Me.Outlook.MasterCategories.PostAsync(new OutlookCategory
            {
                DisplayName = input.DisplayName.Trim(),
                Color = input.Color
            }, cancellationToken: cancellationToken);
        })));

    [Description("Update the display name or color of an Outlook master category.")]
    [McpServerTool(Title = "Update Outlook master category", Name = "graph_outlook_master_categories_update",
        UseStructuredContent = true, OutputSchemaType = typeof(OutlookCategory),
        Destructive = true, Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> Update(
        [Description("Master category ID.")] string categoryId,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Replacement display name.")] string? displayName = null,
        [Description("Replacement Outlook category color preset.")] CategoryColor? color = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            if (displayName is null && color is null)
                throw new ValidationException("A displayName or color is required.");
            var (input, rejected, _) = await requestContext.Server.TryElicit(
                new MasterCategoryPatchInput { DisplayName = displayName, Color = color }, cancellationToken);
            ThrowIfRejected(input, rejected);
            if (input!.DisplayName is not null) ArgumentException.ThrowIfNullOrWhiteSpace(input.DisplayName);
            return await client.Me.Outlook.MasterCategories[categoryId].PatchAsync(new OutlookCategory
            {
                DisplayName = input.DisplayName?.Trim(),
                Color = input.Color
            }, cancellationToken: cancellationToken);
        })));

    [Description("Delete an Outlook master category. Existing items retain the category name until changed.")]
    [McpServerTool(Title = "Delete Outlook master category", Name = "graph_outlook_master_categories_delete",
        Destructive = true, Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> Delete(
        [Description("Master category ID to delete.")] string categoryId,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.ConfirmAndDeleteAsync<DeleteMasterCategoryInput>(categoryId,
            async _ => await client.Me.Outlook.MasterCategories[categoryId].DeleteAsync(cancellationToken: cancellationToken),
            "Outlook master category deleted.", cancellationToken)));

    private static void ThrowIfRejected(object? input, object? rejected)
    {
        if (input is null || rejected is not null) throw new Exception(JsonSerializer.Serialize(rejected));
    }

    [Description("Review the Outlook master category fields.")]
    public sealed class MasterCategoryInput
    {
        [Required, JsonPropertyName("displayName")] public string DisplayName { get; set; } = default!;
        [Required, JsonPropertyName("color"), JsonConverter(typeof(JsonStringEnumConverter))]
        public CategoryColor? Color { get; set; }
    }

    [Description("Review the Outlook master category changes.")]
    public sealed class MasterCategoryPatchInput
    {
        [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
        [JsonPropertyName("color"), JsonConverter(typeof(JsonStringEnumConverter))]
        public CategoryColor? Color { get; set; }
    }

    [Description("Please confirm the Outlook master category ID to delete: {0}")]
    public sealed class DeleteMasterCategoryInput : MCPhappey.Common.Models.IHasName
    {
        [Required] public string Name { get; set; } = default!;
    }
}
