using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.Purview;

public static class GraphEdiscoveryCases
{
    private const string CasesUrl = "https://graph.microsoft.com/v1.0/security/cases/ediscoveryCases";

    [Description("Create a Microsoft Purview eDiscovery case.")]
    [McpServerTool(Title = "Create Purview eDiscovery case", Name = "graph_purview_ediscovery_cases_create",
        Destructive = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(JsonElement))]
    public static async Task<CallToolResult?> GraphEdiscoveryCases_Create(
        [Description("Display name of the new eDiscovery case.")] string displayName,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional case description.")] string? description = null,
        [Description("Optional external case reference used by another system.")] string? externalId = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (input, rejected, _) = await requestContext.Server.TryElicit(new EdiscoveryCaseInput
            {
                DisplayName = displayName,
                Description = description,
                ExternalId = externalId
            }, cancellationToken);
            ThrowIfRejected(rejected);
            ValidateDisplayName(input?.DisplayName);

            return await SendJsonAsync(serviceProvider, requestContext, HttpMethod.Post, CasesUrl,
                new
                {
                    displayName = input!.DisplayName.Trim(),
                    description = input.Description,
                    externalId = input.ExternalId
                }, cancellationToken);
        }));

    [Description("Update the editable metadata of a Microsoft Purview eDiscovery case. Only supplied values are changed.")]
    [McpServerTool(Title = "Update Purview eDiscovery case", Name = "graph_purview_ediscovery_cases_update",
        Destructive = true, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(JsonElement))]
    public static async Task<CallToolResult?> GraphEdiscoveryCases_Update(
        [Description("eDiscovery case ID.")] string caseId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Replacement display name.")] string? displayName = null,
        [Description("Replacement description. Supply an empty string to clear it.")] string? description = null,
        [Description("Replacement external case reference. Supply an empty string to clear it.")] string? externalId = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(caseId);
            if (displayName is null && description is null && externalId is null)
                throw new ValidationException("At least one case field must be supplied.");

            var (input, rejected, _) = await requestContext.Server.TryElicit(new EdiscoveryCasePatchInput
            {
                DisplayName = displayName,
                Description = description,
                ExternalId = externalId
            }, cancellationToken);
            ThrowIfRejected(rejected);
            if (input?.DisplayName is not null) ValidateDisplayName(input.DisplayName);

            var fields = new Dictionary<string, object?>();
            if (input?.DisplayName is not null) fields["displayName"] = input.DisplayName.Trim();
            if (input?.Description is not null) fields["description"] = input.Description;
            if (input?.ExternalId is not null) fields["externalId"] = input.ExternalId;

            return await SendJsonAsync(serviceProvider, requestContext, HttpMethod.Patch,
                $"{CasesUrl}/{Uri.EscapeDataString(caseId.Trim())}", fields, cancellationToken);
        }));

    [Description("Permanently delete a Microsoft Purview eDiscovery case after explicit confirmation.")]
    [McpServerTool(Title = "Delete Purview eDiscovery case", Name = "graph_purview_ediscovery_cases_delete",
        Destructive = true, Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphEdiscoveryCases_Delete(
        [Description("eDiscovery case ID.")] string caseId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(caseId);
            return await requestContext.ConfirmAndDeleteAsync<DeleteEdiscoveryCaseInput>(caseId,
                async ct => await SendNoContentAsync(serviceProvider, requestContext, HttpMethod.Delete,
                    $"{CasesUrl}/{Uri.EscapeDataString(caseId.Trim())}", ct),
                "Microsoft Purview eDiscovery case deleted.", cancellationToken);
        });

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
        RequestContext<CallToolRequestParams> requestContext, HttpMethod method, string url,
        CancellationToken cancellationToken)
    {
        var client = await serviceProvider.GetGraphHttpClient(requestContext.Server);
        using var request = new HttpRequestMessage(method, url);
        using var response = await client.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var error = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(
            $"Microsoft Graph eDiscovery request failed ({(int)response.StatusCode} {response.StatusCode}): {error}");
    }

    private static void ValidateDisplayName(string? value) => ArgumentException.ThrowIfNullOrWhiteSpace(value);

    private static void ThrowIfRejected(object? rejected)
    {
        if (rejected is not null) throw new Exception(JsonSerializer.Serialize(rejected));
    }

    [Description("Please review the new Microsoft Purview eDiscovery case.")]
    public sealed class EdiscoveryCaseInput
    {
        [Required, JsonPropertyName("displayName")] public string DisplayName { get; set; } = default!;
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("externalId")] public string? ExternalId { get; set; }
    }

    [Description("Please review the Microsoft Purview eDiscovery case changes.")]
    public sealed class EdiscoveryCasePatchInput
    {
        [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("externalId")] public string? ExternalId { get; set; }
    }

    [Description("Please confirm the eDiscovery case ID to permanently delete: {0}")]
    public sealed class DeleteEdiscoveryCaseInput : MCPhappey.Common.Models.IHasName
    {
        [Required, JsonPropertyName("name")] public string Name { get; set; } = default!;
    }
}
