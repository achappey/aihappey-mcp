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

public static partial class GraphOutlookSettings
{
    [Description("Create a master category in the signed-in user's Outlook mailbox.")]
    [McpServerTool(Title = "Create Outlook master category",
        Name = "graph_outlook_settings_create_category",
        UseStructuredContent = true,
        OutputSchemaType = typeof(OutlookCategory),
        Destructive = false,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphOutlookSettings_CreateCategory(
        [Description("Display name of the category.")] string displayName,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Outlook category color, from preset0 through preset24, or none.")] CategoryColor? color = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new GraphOutlookCategoryInput { DisplayName = displayName, Color = color }, cancellationToken);

            if (notAccepted is not null)
                throw new Exception(JsonSerializer.Serialize(notAccepted));

            ArgumentException.ThrowIfNullOrWhiteSpace(typed?.DisplayName);
            return await client.Me.Outlook.MasterCategories.PostAsync(
                new OutlookCategory
                {
                    DisplayName = typed.DisplayName.Trim(),
                    Color = typed.Color ?? CategoryColor.None
                }, cancellationToken: cancellationToken);
        })));

    [Description("Update a master category in the signed-in user's Outlook mailbox.")]
    [McpServerTool(Title = "Update Outlook master category",
        Name = "graph_outlook_settings_update_category",
        UseStructuredContent = true,
        OutputSchemaType = typeof(OutlookCategory),
        Destructive = true,
        Idempotent = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphOutlookSettings_UpdateCategory(
        [Description("ID of the Outlook master category.")] string categoryId,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("New category display name. Leave empty to keep unchanged.")] string? displayName = null,
        [Description("New Outlook category color, from preset0 through preset24, or none. Leave empty to keep unchanged.")] CategoryColor? color = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            if (displayName is null && color is null)
                throw new ValidationException("A display name or color must be provided.");

            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new GraphOutlookCategoryUpdate { DisplayName = displayName, Color = color }, cancellationToken);

            if (notAccepted is not null)
                throw new Exception(JsonSerializer.Serialize(notAccepted));
            if (typed?.DisplayName is not null)
                ArgumentException.ThrowIfNullOrWhiteSpace(typed.DisplayName);

            return await client.Me.Outlook.MasterCategories[categoryId].PatchAsync(
                new OutlookCategory
                {
                    DisplayName = typed?.DisplayName?.Trim(),
                    Color = typed?.Color
                }, cancellationToken: cancellationToken);
        })));

    [Description("Delete a master category from the signed-in user's Outlook mailbox.")]
    [McpServerTool(Title = "Delete Outlook master category",
        Name = "graph_outlook_settings_delete_category",
        Destructive = true,
        Idempotent = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphOutlookSettings_DeleteCategory(
        [Description("ID of the Outlook master category to delete.")] string categoryId,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.ConfirmAndDeleteAsync<GraphDeleteOutlookCategory>(
            categoryId,
            async _ => await client.Me.Outlook.MasterCategories[categoryId]
                .DeleteAsync(cancellationToken: cancellationToken),
            "Outlook master category deleted.", cancellationToken)));

    [Description("Create an inbox rule in the signed-in user's Outlook mailbox using explicit conditions and actions.")]
    [McpServerTool(Title = "Create Outlook inbox rule",
        Name = "graph_outlook_settings_create_inbox_rule",
        UseStructuredContent = true,
        OutputSchemaType = typeof(MessageRule),
        Destructive = false,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphOutlookSettings_CreateInboxRule(
        [Description("Display name of the inbox rule.")] string displayName,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Whether the rule is enabled.")] bool isEnabled = true,
        [Description("Subject fragments to match, separated by commas.")] string? subjectContains = null,
        [Description("Body fragments to match, separated by commas.")] string? bodyContains = null,
        [Description("Sender name or address fragments to match, separated by commas.")] string? senderContains = null,
        [Description("Whether messages must have attachments. Leave empty to ignore attachments.")] bool? hasAttachments = null,
        [Description("Destination mail folder ID. Leave empty to not move messages.")] string? moveToFolderId = null,
        [Description("Categories to assign, separated by commas.")] string? assignCategories = null,
        [Description("Whether matching messages are marked as read.")] bool markAsRead = false,
        [Description("Whether matching messages are deleted.")] bool delete = false,
        [Description("Whether later inbox rules should be skipped after this rule matches.")] bool stopProcessingRules = true,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new GraphOutlookInboxRuleInput
                {
                    DisplayName = displayName,
                    IsEnabled = isEnabled,
                    SubjectContains = subjectContains,
                    BodyContains = bodyContains,
                    SenderContains = senderContains,
                    HasAttachments = hasAttachments,
                    MoveToFolderId = moveToFolderId,
                    AssignCategories = assignCategories,
                    MarkAsRead = markAsRead,
                    Delete = delete,
                    StopProcessingRules = stopProcessingRules
                }, cancellationToken);

            if (notAccepted is not null)
                throw new Exception(JsonSerializer.Serialize(notAccepted));

            ValidateRule(typed);
            return await client.Me.MailFolders["inbox"].MessageRules.PostAsync(
                ToMessageRule(typed!), cancellationToken: cancellationToken);
        })));

    [Description("Update an inbox rule in the signed-in user's Outlook mailbox. Provided condition and action groups replace their current values.")]
    [McpServerTool(Title = "Update Outlook inbox rule",
        Name = "graph_outlook_settings_update_inbox_rule",
        UseStructuredContent = true,
        OutputSchemaType = typeof(MessageRule),
        Destructive = true,
        Idempotent = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphOutlookSettings_UpdateInboxRule(
        [Description("ID of the inbox rule.")] string ruleId,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("New display name. Leave empty to keep unchanged.")] string? displayName = null,
        [Description("Whether the rule is enabled. Leave empty to keep unchanged.")] bool? isEnabled = null,
        [Description("Replacement subject fragments, separated by commas. An empty value clears this condition.")] string? subjectContains = null,
        [Description("Replacement body fragments, separated by commas. An empty value clears this condition.")] string? bodyContains = null,
        [Description("Replacement sender fragments, separated by commas. An empty value clears this condition.")] string? senderContains = null,
        [Description("Replacement attachment condition.")] bool? hasAttachments = null,
        [Description("Replacement destination mail folder ID. An empty value clears the move action.")] string? moveToFolderId = null,
        [Description("Replacement categories, separated by commas. An empty value clears category assignment.")] string? assignCategories = null,
        [Description("Replacement mark-as-read action.")] bool? markAsRead = null,
        [Description("Replacement delete action.")] bool? delete = null,
        [Description("Replacement stop-processing-rules action.")] bool? stopProcessingRules = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var conditionProvided = IsAnyProvided(requestContext,
                "subjectContains", "bodyContains", "senderContains", "hasAttachments");
            var actionProvided = IsAnyProvided(requestContext,
                "moveToFolderId", "assignCategories", "markAsRead", "delete", "stopProcessingRules");

            if (displayName is null && isEnabled is null && !conditionProvided && !actionProvided)
                throw new ValidationException("At least one inbox rule property must be provided.");

            var seed = new GraphOutlookInboxRuleUpdate
            {
                DisplayName = displayName,
                IsEnabled = isEnabled,
                SubjectContains = subjectContains,
                BodyContains = bodyContains,
                SenderContains = senderContains,
                HasAttachments = hasAttachments,
                MoveToFolderId = moveToFolderId,
                AssignCategories = assignCategories,
                MarkAsRead = markAsRead,
                Delete = delete,
                StopProcessingRules = stopProcessingRules
            };
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(seed, cancellationToken);

            if (notAccepted is not null)
                throw new Exception(JsonSerializer.Serialize(notAccepted));
            if (typed?.DisplayName is not null)
                ArgumentException.ThrowIfNullOrWhiteSpace(typed.DisplayName);

            var update = new MessageRule
            {
                DisplayName = typed?.DisplayName?.Trim(),
                IsEnabled = typed?.IsEnabled,
                Conditions = conditionProvided ? ToPredicates(typed) : null,
                Actions = actionProvided ? ToActions(typed) : null
            };

            return await client.Me.MailFolders["inbox"].MessageRules[ruleId]
                .PatchAsync(update, cancellationToken: cancellationToken);
        })));

    [Description("Delete an inbox rule from the signed-in user's Outlook mailbox.")]
    [McpServerTool(Title = "Delete Outlook inbox rule",
        Name = "graph_outlook_settings_delete_inbox_rule",
        Destructive = true,
        Idempotent = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphOutlookSettings_DeleteInboxRule(
        [Description("ID of the inbox rule to delete.")] string ruleId,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.ConfirmAndDeleteAsync<GraphDeleteOutlookInboxRule>(
            ruleId,
            async _ => await client.Me.MailFolders["inbox"].MessageRules[ruleId]
                .DeleteAsync(cancellationToken: cancellationToken),
            "Outlook inbox rule deleted.", cancellationToken)));

    private static MessageRule ToMessageRule(GraphOutlookInboxRuleInput input) => new()
    {
        DisplayName = input.DisplayName.Trim(),
        IsEnabled = input.IsEnabled,
        Conditions = ToPredicates(input),
        Actions = ToActions(input)
    };

    private static MessageRulePredicates ToPredicates(GraphOutlookInboxRuleInputBase? input) => new()
    {
        SubjectContains = SplitCsv(input?.SubjectContains),
        BodyContains = SplitCsv(input?.BodyContains),
        SenderContains = SplitCsv(input?.SenderContains),
        HasAttachments = input?.HasAttachments
    };

    private static MessageRuleActions ToActions(GraphOutlookInboxRuleInputBase? input) => new()
    {
        MoveToFolder = string.IsNullOrWhiteSpace(input?.MoveToFolderId) ? null : input.MoveToFolderId.Trim(),
        AssignCategories = SplitCsv(input?.AssignCategories),
        MarkAsRead = input?.MarkAsRead,
        Delete = input?.Delete,
        StopProcessingRules = input?.StopProcessingRules
    };

    private static List<string>? SplitCsv(string? value) => string.IsNullOrWhiteSpace(value)
        ? null
        : [.. value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    private static void ValidateRule(GraphOutlookInboxRuleInput? input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.DisplayName);

        var hasCondition = SplitCsv(input.SubjectContains)?.Count > 0
            || SplitCsv(input.BodyContains)?.Count > 0
            || SplitCsv(input.SenderContains)?.Count > 0
            || input.HasAttachments is not null;
        var hasAction = !string.IsNullOrWhiteSpace(input.MoveToFolderId)
            || SplitCsv(input.AssignCategories)?.Count > 0
            || input.MarkAsRead == true
            || input.Delete == true;

        if (!hasCondition)
            throw new ValidationException("At least one inbox rule condition must be provided.");
        if (!hasAction)
            throw new ValidationException("At least one inbox rule action must be provided.");
    }

    private static bool IsAnyProvided(RequestContext<CallToolRequestParams> requestContext, params string[] names) =>
        names.Any(name => requestContext.Params?.Arguments?.ContainsKey(name) == true);

    [Description("Please fill in the Outlook master category details.")]
    public sealed class GraphOutlookCategoryInput
    {
        [Required]
        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = default!;

        [JsonPropertyName("color")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public CategoryColor? Color { get; set; }
    }

    [Description("Please fill in the Outlook master category fields to update.")]
    public sealed class GraphOutlookCategoryUpdate
    {
        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("color")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public CategoryColor? Color { get; set; }
    }

    public abstract class GraphOutlookInboxRuleInputBase
    {
        [JsonPropertyName("subjectContains")]
        public string? SubjectContains { get; set; }

        [JsonPropertyName("bodyContains")]
        public string? BodyContains { get; set; }

        [JsonPropertyName("senderContains")]
        public string? SenderContains { get; set; }

        [JsonPropertyName("hasAttachments")]
        public bool? HasAttachments { get; set; }

        [JsonPropertyName("moveToFolderId")]
        public string? MoveToFolderId { get; set; }

        [JsonPropertyName("assignCategories")]
        public string? AssignCategories { get; set; }

        [JsonPropertyName("markAsRead")]
        public bool? MarkAsRead { get; set; }

        [JsonPropertyName("delete")]
        public bool? Delete { get; set; }

        [JsonPropertyName("stopProcessingRules")]
        public bool? StopProcessingRules { get; set; }
    }

    [Description("Please fill in the Outlook inbox rule details.")]
    public sealed class GraphOutlookInboxRuleInput : GraphOutlookInboxRuleInputBase
    {
        [Required]
        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = default!;

        [JsonPropertyName("isEnabled")]
        public bool IsEnabled { get; set; } = true;
    }

    [Description("Please fill in the Outlook inbox rule fields to update.")]
    public sealed class GraphOutlookInboxRuleUpdate : GraphOutlookInboxRuleInputBase
    {
        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("isEnabled")]
        public bool? IsEnabled { get; set; }
    }

    [Description("Please confirm the Outlook master category id to delete: {0}")]
    public sealed class GraphDeleteOutlookCategory : MCPhappey.Common.Models.IHasName
    {
        [Required]
        public string Name { get; set; } = default!;
    }

    [Description("Please confirm the Outlook inbox rule id to delete: {0}")]
    public sealed class GraphDeleteOutlookInboxRule : MCPhappey.Common.Models.IHasName
    {
        [Required]
        public string Name { get; set; } = default!;
    }
}
