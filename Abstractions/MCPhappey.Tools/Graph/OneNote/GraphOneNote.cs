using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using Microsoft.Graph.Beta.Me.Onenote.Notebooks.Item.CopyNotebook;
using Microsoft.Graph.Beta.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.OneNote;

public static class GraphOneNote
{
    [Description("Create a new OneNote page in a specified section.")]
    [McpServerTool(Title = "Create OneNote page",
        Name = "graph_onenote_create_page",
        Destructive = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphOneNote_CreatePage(
        [Description("The ID of the section where the page will be created.")] string sectionId,
        [Description("Title of the new page.")] string title,
        [Description("Page content.")] string content,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
         await requestContext.WithExceptionCheck(async () =>
         await requestContext.WithOboGraphClient(async client =>
         await requestContext.WithStructuredContent(async () =>
    {
        var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
            new GraphNewOneNotePage()
            {
                Title = title,
                Content = content
            }, cancellationToken);

        if (notAccepted != null) throw new Exception(JsonSerializer.Serialize(notAccepted));

        return await client.Me
            .Onenote
            .Sections[sectionId]
            .Pages
            .PostAsync(new OnenotePage()
            {
                Title = typed?.Title,
                Content = BinaryData.FromString($"<html><head><title>{typed?.Title}</title></head><body>{typed?.Content}</body></html>").ToArray()
            }, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    })));

    [Description("Create a new OneNote section in a specified notebook.")]
    [McpServerTool(Title = "Create OneNote section",
        Name = "graph_onenote_create_section",
        Destructive = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphOneNote_CreateSection(
        [Description("The ID of the notebook where the section will be created.")] string notebookId,
        [Description("Displayname of the new section.")] string displayName,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
         await requestContext.WithExceptionCheck(async () =>
         await requestContext.WithOboGraphClient(async client =>
         await requestContext.WithStructuredContent(async () =>
    {
        var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
            new GraphNewOneNoteSection()
            {
                DisplayName = displayName
            }, cancellationToken);

        if (notAccepted != null) throw new Exception(JsonSerializer.Serialize(notAccepted));

        // POST /me/onenote/notebooks/{notebookId}/sections
        var section = new OnenoteSection
        {
            DisplayName = typed!.DisplayName
        };

        return await client.Me
            .Onenote
            .Notebooks[notebookId]
            .Sections
            .PostAsync(section, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    })));

    [Description("Copy a personal Notebook to a group.")]
    [McpServerTool(Title = "Copy Notebook",
       Name = "graph_onenote_copy_notebook",
       Destructive = true,
       OpenWorld = false)]
    public static async Task<CallToolResult?> GraphOneNote_CopyNotebook(
       [Description("The ID of the notebook where the section will be created.")] string notebookId,
       [Description("Id of the group.")] string groupId,
       [Description("New name of the notebook")] string renameAs,
       RequestContext<CallToolRequestParams> requestContext,
       CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
   {
       var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
           new GraphCopyNotebook()
           {
               RenameAs = renameAs,
               GroupId = groupId
           }, cancellationToken);

       var section = new CopyNotebookPostRequestBody
       {
           RenameAs = typed!.RenameAs,
           GroupId = typed!.GroupId
       };

       return await client.Me
           .Onenote
           .Notebooks[notebookId]
           .CopyNotebook
           .PostAsync(section, cancellationToken: cancellationToken)
           .ConfigureAwait(false);
   })));

    [Description("Create a new OneNote notebook for the current user.")]
    [McpServerTool(Title = "Create OneNote notebook",
        Name = "graph_onenote_create_notebook",
        Destructive = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphOneNote_CreateNotebook(
        string displayName,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
    {
        var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
            new GraphNewOneNoteNotebook()
            {
                DisplayName = displayName
            }, cancellationToken);

        // POST /me/onenote/notebooks
        var notebook = new Notebook
        {
            DisplayName = typed.DisplayName
        };

        return await client.Me
            .Onenote
            .Notebooks
            .PostAsync(notebook, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    })));

    [Description("Delete a specific OneNote page.")]
    [McpServerTool(Title = "Delete OneNote page", Name = "graph_onenote_delete_page", Destructive = true)]
    public static async Task<CallToolResult?> GraphOneNote_DeletePage(
        string pageId,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithOboGraphClient(async (client) =>
        await requestContext.ConfirmAndDeleteAsync<GraphDeleteOneNotePage>(
        pageId,
        async _ => await client.Me.Onenote.Pages[pageId].DeleteAsync(cancellationToken: cancellationToken),
        "Page deleted.",
        cancellationToken));

    [Description("Please fill in the entity id to confirm deletion: {0}")]
    public class GraphDeleteOneNotePage : IHasName
    {
        [JsonPropertyName("name")]
        [Required]
        [Description("The name value of the entity.")]
        public string Name { get; set; } = default!;


    }

    // ----- Elicited payloads -----
    [Description("Please provide details for the new OneNote page.")]
    public class GraphNewOneNotePage
    {
        [JsonPropertyName("title")]
        [Required]
        [Description("The title of the new OneNote page.")]
        public string Title { get; set; } = default!;

        [JsonPropertyName("content")]
        [Required]
        [Description("The HTML content of the new page body.")]
        public string Content { get; set; } = default!;
    }

    [Description("Please provide details for the new OneNote section.")]
    public class GraphNewOneNoteSection
    {
        [JsonPropertyName("displayName")]
        [Required]
        [Description("The name of the new section.")]
        public string DisplayName { get; set; } = default!;
    }

    [Description("Please provide details for the copy Notebook.")]
    public class GraphCopyNotebook
    {
        [JsonPropertyName("renameAs")]
        [Required]
        [Description("The new name of the notebook.")]
        public string RenameAs { get; set; } = default!;

        [JsonPropertyName("groupId")]
        [Required]
        [Description("The id of the group.")]
        public string GroupId { get; set; } = default!;
    }

    [Description("Please provide details for the new OneNote notebook.")]
    public class GraphNewOneNoteNotebook
    {
        [JsonPropertyName("displayName")]
        [Required]
        [Description("The name of the new notebook.")]
        public string DisplayName { get; set; } = default!;
    }
}