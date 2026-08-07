using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.OneNote;

public static partial class GraphOneNoteCrud
{
    [Description("Rename a OneNote notebook owned by the signed-in user.")]
    [McpServerTool(Title = "Rename OneNote notebook", Name = "graph_onenote_rename_notebook",
        UseStructuredContent = true, OutputSchemaType = typeof(Notebook),
        Destructive = true, Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphOneNote_RenameNotebook(
        [Description("OneNote notebook ID.")] string notebookId,
        [Description("New notebook display name.")] string displayName,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var input = await ElicitName(requestContext, displayName, "notebook", cancellationToken);
            return await client.Me.Onenote.Notebooks[notebookId].PatchAsync(
                new Notebook { DisplayName = input.DisplayName.Trim() }, cancellationToken: cancellationToken);
        })));

    [Description("Rename a OneNote section owned by the signed-in user.")]
    [McpServerTool(Title = "Rename OneNote section", Name = "graph_onenote_rename_section",
        UseStructuredContent = true, OutputSchemaType = typeof(OnenoteSection),
        Destructive = true, Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphOneNote_RenameSection(
        [Description("OneNote section ID.")] string sectionId,
        [Description("New section display name.")] string displayName,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var input = await ElicitName(requestContext, displayName, "section", cancellationToken);
            return await client.Me.Onenote.Sections[sectionId].PatchAsync(
                new OnenoteSection { DisplayName = input.DisplayName.Trim() }, cancellationToken: cancellationToken);
        })));

    [Description("Update OneNote page HTML by replacing, appending, or prepending content at a target.")]
    [McpServerTool(Title = "Update OneNote page content", Name = "graph_onenote_update_page_content",
        Destructive = true, Idempotent = false, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphOneNote_UpdatePageContent(
        [Description("OneNote page ID.")] string pageId,
        [Description("Patch action: replace, append, or prepend.")] string action,
        [Description("OneNote patch target, such as 'body', 'title', or an element id.")] string target,
        [Description("HTML content to apply.")] string content,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        {
            var (input, notAccepted, _) = await requestContext.Server.TryElicit(
                new PageContentInput { Action = action, Target = target, Content = content }, cancellationToken);
            if (notAccepted is not null || input is null)
                throw new Exception(JsonSerializer.Serialize(notAccepted));

            ArgumentException.ThrowIfNullOrWhiteSpace(input.Action);
            ArgumentException.ThrowIfNullOrWhiteSpace(input.Target);
            ArgumentException.ThrowIfNullOrWhiteSpace(input.Content);
            var normalizedAction = input.Action.Trim().ToLowerInvariant();
            if (normalizedAction is not ("replace" or "append" or "prepend"))
                throw new ValidationException("Action must be replace, append, or prepend.");

            var payload = JsonSerializer.Serialize(new[]
            {
                new { target = input.Target.Trim(), action = normalizedAction, content = input.Content }
            });
            var request = new RequestInformation
            {
                HttpMethod = Method.PATCH,
                URI = new Uri($"https://graph.microsoft.com/beta/me/onenote/pages/{Uri.EscapeDataString(pageId)}/content")
            };
            request.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(payload)), "application/json");
            await client.RequestAdapter.SendNoContentAsync(request, cancellationToken: cancellationToken);

            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = "OneNote page content updated." }]
            };
        }));

    private static async Task<DisplayNameInput> ElicitName(
        RequestContext<CallToolRequestParams> requestContext,
        string displayName,
        string entity,
        CancellationToken cancellationToken)
    {
        var (input, notAccepted, _) = await requestContext.Server.TryElicit(
            new DisplayNameInput { DisplayName = displayName }, cancellationToken);
        if (notAccepted is not null || input is null)
            throw new Exception(JsonSerializer.Serialize(notAccepted));
        ArgumentException.ThrowIfNullOrWhiteSpace(input.DisplayName);
        if (input.DisplayName.Trim().Length > 128)
            throw new ValidationException($"The OneNote {entity} display name cannot exceed 128 characters.");
        return input;
    }

    [Description("Review the new OneNote display name.")]
    public sealed class DisplayNameInput
    {
        [Required]
        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = default!;
    }

    [Description("Review the OneNote page content patch.")]
    public sealed class PageContentInput
    {
        [Required]
        [JsonPropertyName("action")]
        public string Action { get; set; } = default!;

        [Required]
        [JsonPropertyName("target")]
        public string Target { get; set; } = default!;

        [Required]
        [JsonPropertyName("content")]
        public string Content { get; set; } = default!;
    }
}
