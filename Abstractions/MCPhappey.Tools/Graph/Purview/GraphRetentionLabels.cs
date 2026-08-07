using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.Purview;

public static class GraphRetentionLabels
{
    private const string LabelsUrl = "https://graph.microsoft.com/v1.0/security/labels/retentionLabels";

    [Description("Create a Microsoft Purview retention label with an explicit retention or deletion behavior.")]
    [McpServerTool(Title = "Create Purview retention label", Name = "graph_purview_retention_labels_create",
        Destructive = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(JsonElement))]
    public static async Task<CallToolResult?> GraphRetentionLabels_Create(
        string displayName,
        string descriptionForUsers,
        string descriptionForAdmins,
        RetentionBehavior behaviorDuringRetentionPeriod,
        RetentionAction actionAfterRetentionPeriod,
        RetentionEventType retentionTrigger,
        int retentionDurationDays,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Whether content can be explicitly marked as a record with this label.")] bool isInUse = false,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (input, rejected, _) = await requestContext.Server.TryElicit(new RetentionLabelInput
            {
                DisplayName = displayName,
                DescriptionForUsers = descriptionForUsers,
                DescriptionForAdmins = descriptionForAdmins,
                BehaviorDuringRetentionPeriod = behaviorDuringRetentionPeriod,
                ActionAfterRetentionPeriod = actionAfterRetentionPeriod,
                RetentionTrigger = retentionTrigger,
                RetentionDurationDays = retentionDurationDays,
                IsInUse = isInUse
            }, cancellationToken);
            ThrowIfRejected(rejected);
            Validate(input);

            return await SendJsonAsync(serviceProvider, requestContext, HttpMethod.Post, LabelsUrl,
                ToPayload(input!), cancellationToken);
        }));

    [Description("Update editable descriptions of a Microsoft Purview retention label. Published or in-use labels have Graph service restrictions.")]
    [McpServerTool(Title = "Update Purview retention label", Name = "graph_purview_retention_labels_update",
        Destructive = true, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(JsonElement))]
    public static async Task<CallToolResult?> GraphRetentionLabels_Update(
        string labelId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Replacement user-facing description.")] string? descriptionForUsers = null,
        [Description("Replacement administrator-facing description.")] string? descriptionForAdmins = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(labelId);
            if (descriptionForUsers is null && descriptionForAdmins is null)
                throw new ValidationException("At least one retention-label description must be supplied.");

            var (input, rejected, _) = await requestContext.Server.TryElicit(new RetentionLabelPatchInput
            {
                DescriptionForUsers = descriptionForUsers,
                DescriptionForAdmins = descriptionForAdmins
            }, cancellationToken);
            ThrowIfRejected(rejected);

            var fields = new Dictionary<string, object?>();
            if (input?.DescriptionForUsers is not null) fields["descriptionForUsers"] = input.DescriptionForUsers;
            if (input?.DescriptionForAdmins is not null) fields["descriptionForAdmins"] = input.DescriptionForAdmins;

            return await SendJsonAsync(serviceProvider, requestContext, HttpMethod.Patch,
                $"{LabelsUrl}/{Uri.EscapeDataString(labelId.Trim())}", fields, cancellationToken);
        }));

    [Description("Delete an unused Microsoft Purview retention label after explicit confirmation.")]
    [McpServerTool(Title = "Delete Purview retention label", Name = "graph_purview_retention_labels_delete",
        Destructive = true, Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphRetentionLabels_Delete(
        string labelId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(labelId);
            return await requestContext.ConfirmAndDeleteAsync<DeleteRetentionLabelInput>(labelId,
                async ct => await SendNoContentAsync(serviceProvider, requestContext,
                    $"{LabelsUrl}/{Uri.EscapeDataString(labelId.Trim())}", ct),
                "Microsoft Purview retention label deleted.", cancellationToken);
        });

    private static object ToPayload(RetentionLabelInput input) => new
    {
        displayName = input.DisplayName.Trim(),
        descriptionForUsers = input.DescriptionForUsers,
        descriptionForAdmins = input.DescriptionForAdmins,
        behaviorDuringRetentionPeriod = ToGraphValue(input.BehaviorDuringRetentionPeriod),
        actionAfterRetentionPeriod = ToGraphValue(input.ActionAfterRetentionPeriod),
        retentionTrigger = ToGraphValue(input.RetentionTrigger),
        retentionDuration = new { days = input.RetentionDurationDays },
        isInUse = input.IsInUse
    };

    private static string ToGraphValue<T>(T value) where T : Enum =>
        JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    private static void Validate(RetentionLabelInput? input)
    {
        if (input is null) throw new ValidationException("Retention-label input is required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(input.DisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.DescriptionForUsers);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.DescriptionForAdmins);
        if (input.RetentionDurationDays < 1)
            throw new ValidationException("retentionDurationDays must be at least one day.");
    }

    private static async Task<JsonElement> SendJsonAsync(IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext, HttpMethod method, string url, object body,
        CancellationToken cancellationToken)
    {
        var client = await serviceProvider.GetGraphHttpClient(requestContext.Server);
        using var request = new HttpRequestMessage(method, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, MediaTypeNames.Application.Json)
        };
        using var response = await client.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static async Task SendNoContentAsync(IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext, string url, CancellationToken cancellationToken)
    {
        var client = await serviceProvider.GetGraphHttpClient(requestContext.Server);
        using var response = await client.DeleteAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var error = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(
            $"Microsoft Graph retention-label request failed ({(int)response.StatusCode} {response.StatusCode}): {error}");
    }

    private static void ThrowIfRejected(object? rejected)
    {
        if (rejected is not null) throw new Exception(JsonSerializer.Serialize(rejected));
    }

    [Description("Please review the Microsoft Purview retention-label settings.")]
    public sealed class RetentionLabelInput
    {
        [Required, JsonPropertyName("displayName")] public string DisplayName { get; set; } = default!;
        [Required, JsonPropertyName("descriptionForUsers")] public string DescriptionForUsers { get; set; } = default!;
        [Required, JsonPropertyName("descriptionForAdmins")] public string DescriptionForAdmins { get; set; } = default!;
        [Required, JsonPropertyName("behaviorDuringRetentionPeriod"), JsonConverter(typeof(JsonStringEnumConverter))]
        public RetentionBehavior BehaviorDuringRetentionPeriod { get; set; }
        [Required, JsonPropertyName("actionAfterRetentionPeriod"), JsonConverter(typeof(JsonStringEnumConverter))]
        public RetentionAction ActionAfterRetentionPeriod { get; set; }
        [Required, JsonPropertyName("retentionTrigger"), JsonConverter(typeof(JsonStringEnumConverter))]
        public RetentionEventType RetentionTrigger { get; set; }
        [Range(1, int.MaxValue), JsonPropertyName("retentionDurationDays")] public int RetentionDurationDays { get; set; }
        [JsonPropertyName("isInUse")] public bool IsInUse { get; set; }
    }

    [Description("Please review the Microsoft Purview retention-label description changes.")]
    public sealed class RetentionLabelPatchInput
    {
        [JsonPropertyName("descriptionForUsers")] public string? DescriptionForUsers { get; set; }
        [JsonPropertyName("descriptionForAdmins")] public string? DescriptionForAdmins { get; set; }
    }

    [Description("Please confirm the unused retention-label ID to delete: {0}")]
    public sealed class DeleteRetentionLabelInput : MCPhappey.Common.Models.IHasName
    {
        [Required, JsonPropertyName("name")] public string Name { get; set; } = default!;
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RetentionBehavior { DoNotRetain, Retain, RetainAsRecord, RetainAsRegulatoryRecord }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RetentionAction { None, Delete, StartDispositionReview, Relabel, PermanentlyDelete }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RetentionEventType { DateOfEvent, DateOfCreation, DateOfLastModification, DateOfLabeling }
}
