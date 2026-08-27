using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.People;

public static class GraphProfileCardProperties
{
    [Description("Add an existing Microsoft Entra user property to the organization's Microsoft 365 profile card.")]
    [McpServerTool(Title = "Create Microsoft 365 profile card property", Name = "graph_profile_card_properties_create",
        Destructive = false, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphProfileCardProperties_Create(
        [Description("Microsoft Entra property name, for example costCenter or extension_{appId}_employeeNumber.")] string directoryPropertyName,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional localized annotation label.")] string? displayName = null,
        [Description("BCP-47 language tag for displayName, for example en-US.")] string languageTag = "en-US",
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (input, notAccepted, _) = await requestContext.Server.TryElicit(new ProfileCardPropertyInput
            {
                DirectoryPropertyName = directoryPropertyName, DisplayName = displayName, LanguageTag = languageTag
            }, cancellationToken);
            ThrowIfNotAccepted(notAccepted, input);
            Validate(input!);
            return await SendJsonAsync(serviceProvider, requestContext, HttpMethod.Post, "admin/people/profileCardProperties",
                ToBody(input!), cancellationToken);
        }));

    [Description("Update the localized annotation label of a Microsoft 365 profile card property.")]
    [McpServerTool(Title = "Update Microsoft 365 profile card property", Name = "graph_profile_card_properties_update",
        Destructive = false, Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphProfileCardProperties_Update(
        [Description("Profile card property ID returned by Microsoft Graph.")] string propertyId,
        [Description("Localized annotation label.")] string displayName,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("BCP-47 language tag for displayName, for example en-US.")] string languageTag = "en-US",
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(propertyId);
            var (input, notAccepted, _) = await requestContext.Server.TryElicit(new ProfileCardAnnotationInput
            {
                DisplayName = displayName, LanguageTag = languageTag
            }, cancellationToken);
            ThrowIfNotAccepted(notAccepted, input);
            ArgumentException.ThrowIfNullOrWhiteSpace(input!.DisplayName);
            ArgumentException.ThrowIfNullOrWhiteSpace(input.LanguageTag);
            return await SendJsonAsync(serviceProvider, requestContext, HttpMethod.Patch,
                $"admin/people/profileCardProperties/{Uri.EscapeDataString(propertyId)}",
                new { annotations = new[] { new { displayName = input.DisplayName.Trim(), localizations = new[] { new { languageTag = input.LanguageTag.Trim(), displayName = input.DisplayName.Trim() } } } } },
                cancellationToken);
        }));

    [Description("Remove a configured property from the organization's Microsoft 365 profile card.")]
    [McpServerTool(Title = "Delete Microsoft 365 profile card property", Name = "graph_profile_card_properties_delete",
        Destructive = true, Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphProfileCardProperties_Delete(
        [Description("Profile card property ID returned by Microsoft Graph.")] string propertyId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await requestContext.ConfirmAndDeleteAsync<DeleteProfileCardPropertyInput>(propertyId,
            async ct => await SendNoContentAsync(serviceProvider, requestContext, HttpMethod.Delete,
                $"admin/people/profileCardProperties/{Uri.EscapeDataString(propertyId)}", ct),
            "Microsoft 365 profile card property deleted.", cancellationToken);

    private static object ToBody(ProfileCardPropertyInput input)
    {
        var annotations = string.IsNullOrWhiteSpace(input.DisplayName) ? null : new[]
        {
            new
            {
                displayName = input.DisplayName.Trim(),
                localizations = new[] { new { languageTag = input.LanguageTag.Trim(), displayName = input.DisplayName.Trim() } }
            }
        };
        return new { directoryPropertyName = input.DirectoryPropertyName.Trim(), annotations };
    }

    private static void Validate(ProfileCardPropertyInput input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input.DirectoryPropertyName);
        if (input.DisplayName is not null) ArgumentException.ThrowIfNullOrWhiteSpace(input.LanguageTag);
    }

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
            request.Content = new StringContent(JsonSerializer.Serialize(body, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            }), Encoding.UTF8, MediaTypeNames.Application.Json);
        var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return response;
    }

    private static void ThrowIfNotAccepted<T>(object? notAccepted, T? input) where T : class
    {
        if (notAccepted is not null || input is null) throw new OperationCanceledException("The elicitation was not accepted.");
    }

    [Description("Please review the Microsoft 365 profile card property.")]
    public sealed class ProfileCardPropertyInput
    {
        [Required, JsonPropertyName("directoryPropertyName")] public string DirectoryPropertyName { get; set; } = default!;
        [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
        [Required, JsonPropertyName("languageTag")] public string LanguageTag { get; set; } = "en-US";
    }

    [Description("Please review the Microsoft 365 profile card annotation.")]
    public sealed class ProfileCardAnnotationInput
    {
        [Required, JsonPropertyName("displayName")] public string DisplayName { get; set; } = default!;
        [Required, JsonPropertyName("languageTag")] public string LanguageTag { get; set; } = "en-US";
    }

    [Description("Please confirm the profile card property ID to delete: {0}")]
    public sealed class DeleteProfileCardPropertyInput : MCPhappey.Common.Models.IHasName
    {
        [Required, JsonPropertyName("name")] public string Name { get; set; } = default!;
    }
}
