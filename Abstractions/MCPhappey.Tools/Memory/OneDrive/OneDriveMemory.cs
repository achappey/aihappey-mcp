using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Memory.OneDrive;

public static class OneDriveMemory
{
    internal const string RootFolderName = "memories";

    private static string Normalize(string p)
    {
        if (string.IsNullOrWhiteSpace(p))
            throw new Exception("Path is required.");

        p = p.Replace("\\", "/").Trim();

        if (!p.StartsWith("/")) p = "/" + p;

        if (!p.StartsWith("/" + RootFolderName))
            throw new Exception($"All memory paths must start with /{RootFolderName}");

        return p;
    }

    [Description("View file or directory contents in OneDrive memory.")]
    [McpServerTool(
     Title = "View OneDrive memory",
     Name = "onedrive_memory_view",
     ReadOnly = true,
     OpenWorld = false,
     Destructive = false)]
    public static async Task<CallToolResult?> OneDriveMemory_View(
     [Description("Path inside /memories. Path should always start with '/memories'")] string path,
     RequestContext<CallToolRequestParams> context,
     [Description("Optional: [startLine, endLine]")] int[]? view_range = null,
     CancellationToken cancellationToken = default) =>
     await context.WithExceptionCheck(async () =>
     await context.WithOboGraphClient(async (graph) =>
 {
     await context.Server.SendMessageNotificationAsync($"Normalizing path: {path}", LoggingLevel.Info, cancellationToken);

     // 1) Normalize path and validate leading /memories
     path = Normalize(path);

     await context.Server.SendMessageNotificationAsync($"Normalized path: {path}", LoggingLevel.Info, cancellationToken);

     // 2) Resolve drive and ensure root /memories exists
     var drive = await graph.GetDefaultDriveAsync(cancellationToken)
                ?? throw new Exception("Could not resolve default OneDrive.");

     await context.Server.SendMessageNotificationAsync($"Ensuring root folder: {drive.Id}", LoggingLevel.Debug, cancellationToken);

     await graph.EnsureRootFolderExistsAsync(drive.Id!, cancellationToken);

     // Ensure subfolders exist for directory browsing or file reading
     var trimmed = path.Trim('/');
     var lastSlash = trimmed.LastIndexOf('/');
     if (lastSlash > 0)
     {
         var folderPart = trimmed[..lastSlash];

         await context.Server.SendMessageNotificationAsync($"Ensuring subfolder: {folderPart}", LoggingLevel.Debug, cancellationToken);

         await graph.EnsureFolderExistsAsync(drive.Id!, folderPart, cancellationToken);
     }

     // 3) Determine if user is requesting a directory listing
     var isDir = !path.Contains('.')
                 || path.Equals("/" + RootFolderName, StringComparison.OrdinalIgnoreCase);

     if (isDir)
     {
         await context.Server.SendMessageNotificationAsync($"Reading files: {path}", LoggingLevel.Info, cancellationToken);

         var files = await graph.ListFilesAsync(drive.Id!, path, cancellationToken);

         await context.Server.SendMessageNotificationAsync($"Files: {files.Count}", LoggingLevel.Debug, cancellationToken);

         return path.ToMemoryListResult(files);
     }

     await context.Server.SendMessageNotificationAsync($"Reading text file: {path}", LoggingLevel.Info, cancellationToken);

     // 4) File read
     var content = await graph.ReadTextFileAsync(drive.Id!, path, cancellationToken);
     if (content is null)
         throw new Exception("File not found");

     // 5) Optional slicing for line ranges
     if (view_range is { Length: 2 })
     {
         var lines = content.Split('\n');
         var start = Math.Max(1, view_range[0]);
         var end = Math.Min(lines.Length, view_range[1]);

         if (start <= end)
             content = string.Join("\n", lines.Skip(start - 1).Take(end - start + 1));
     }

     await context.Server.SendMessageNotificationAsync($"Reading text file completed", LoggingLevel.Info, cancellationToken);

     return content.ToMemoryTextResult();
 }));

    [Description("Create or overwrite a memory file in OneDrive.")]
    [McpServerTool(
        Title = "Create OneDrive memory",
        Name = "onedrive_memory_create",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> OneDriveMemory_Create(
        string path,
        string file_text,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default) =>
        await context.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async (graph) =>
    {
        path = Normalize(path);

        var drive = await graph.GetDefaultDriveAsync(cancellationToken)
                   ?? throw new Exception("Could not resolve default OneDrive.");

        await graph.EnsureRootFolderExistsAsync(drive.Id!, cancellationToken);

        var (typed, notAccepted, _) = await context.Server.TryElicit(
                     new OneDriveCreateMemory { Text = file_text },
                     cancellationToken);

        // Ensure full folder structure for nested paths
        var trimmed = path.Trim('/');
        var lastSlash = trimmed.LastIndexOf('/');
        if (lastSlash > 0)
        {
            var folderPart = trimmed[..lastSlash];
            await graph.EnsureFolderExistsAsync(drive.Id!, folderPart, cancellationToken);
        }

        await graph.WriteTextFileAsync(drive.Id!, path, typed.Text, cancellationToken);

        return "OK".ToTextContentBlock().ToCallToolResult();
    }));

    [Description("Replace text inside a memory file in OneDrive.")]
    [McpServerTool(
        Title = "Replace OneDrive memory",
        Name = "onedrive_memory_str_replace",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> OneDriveMemory_Str_Replace(
        string path,
        string old_str,
        string new_str,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default) =>
        await context.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async (graph) =>
    {
        path = Normalize(path);

        var drive = await graph.GetDefaultDriveAsync(cancellationToken)
                   ?? throw new Exception("Could not resolve default OneDrive.");
        await graph.EnsureRootFolderExistsAsync(drive.Id!, cancellationToken);

        var (typed, notAccepted, _) = await context.Server.TryElicit(
                          new OneDriveReplaceMemory { TextToReplace = old_str, NewText = new_str },
                          cancellationToken);

        var trimmed = path.Trim('/');
        var lastSlash = trimmed.LastIndexOf('/');
        if (lastSlash > 0)
        {
            var folderPart = trimmed[..lastSlash];
            await graph.EnsureFolderExistsAsync(drive.Id!, folderPart, cancellationToken);
        }

        var content = await graph.ReadTextFileAsync(drive.Id!, path, cancellationToken)
                      ?? throw new Exception("File not found");

        content = content.Replace(typed.TextToReplace, typed.NewText);

        await graph.WriteTextFileAsync(drive.Id!, path, content, cancellationToken);

        return "OK".ToTextContentBlock().ToCallToolResult();
    }));

    [Description("Insert a line into a memory file in OneDrive.")]
    [McpServerTool(
        Title = "Insert OneDrive memory line",
        Name = "onedrive_memory_insert",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> OneDriveMemory_Insert(
        string path,
        int insert_line,
        string insert_text,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default) =>
        await context.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async (graph) =>
    {
        path = Normalize(path);

        var drive = await graph.GetDefaultDriveAsync(cancellationToken)
                   ?? throw new Exception("Could not resolve default OneDrive.");
        await graph.EnsureRootFolderExistsAsync(drive.Id!, cancellationToken);

        var (typed, notAccepted, _) = await context.Server.TryElicit(
                    new OneDriveInsertMemory { Text = insert_text, Line = insert_line },
                    cancellationToken);

        var trimmed = path.Trim('/');
        var lastSlash = trimmed.LastIndexOf('/');
        if (lastSlash > 0)
        {
            var folderPart = trimmed[..lastSlash];
            await graph.EnsureFolderExistsAsync(drive.Id!, folderPart, cancellationToken);
        }

        var content = await graph.ReadTextFileAsync(drive.Id!, path, cancellationToken)
                      ?? throw new Exception("File not found");

        var lines = content.Split('\n').ToList();
        typed.Line = Math.Max(1, typed.Line);

        if (typed.Line > lines.Count + 1)
            lines.Add(typed.Text);
        else
            lines.Insert(typed.Line - 1, typed.Text);

        await graph.WriteTextFileAsync(drive.Id!, path, string.Join("\n", lines), cancellationToken);

        return "OK".ToTextContentBlock().ToCallToolResult();
    }));

    [Description("Please fill in the memory path: {0}")]
    public class OneDriveDeleteMemory : IHasName
    {
        [JsonPropertyName("name")]
        [Description("The full path of the memory to delete (must match exactly).")]
        public string Name { get; set; } = default!;
    }

    [Description("Delete a memory file in OneDrive.")]
    [McpServerTool(
        Title = "Delete OneDrive memory",
        Name = "onedrive_memory_delete",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> OneDriveMemory_Delete(
        [Description("The full path of the memory to delete.")] string path,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default) =>
        await context.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async graph =>
        await context.WithStructuredContent(async () =>
        {
            path = Normalize(path);

            var drive = await graph.GetDefaultDriveAsync(cancellationToken)
                 ?? throw new Exception("Could not resolve default OneDrive.");

            return await context.ConfirmAndDeleteAsync<OneDriveDeleteMemory>(
                expectedName: path,
                deleteAction: async _ =>
                {
                    // Attempt delete. No folder recreation here.
                    await graph.DeleteFileAsync(drive.Id!, path, cancellationToken);
                },
                successText: $"Memory '{path}' deleted successfully.",
                ct: cancellationToken
            );
        })));


    [Description("Rename or move a memory file/directory in OneDrive.")]
    [McpServerTool(
        Title = "Rename OneDrive memory",
        Name = "onedrive_memory_rename",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> OneDriveMemory_Rename(
        string old_path,
        string new_path,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default) =>
        await context.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async (graph) =>
    {
        old_path = Normalize(old_path);
        new_path = Normalize(new_path);

        var drive = await graph.GetDefaultDriveAsync(cancellationToken)
                   ?? throw new Exception("Could not resolve default OneDrive.");
        await graph.EnsureRootFolderExistsAsync(drive.Id!, cancellationToken);

        // Ensure target folder exists
        var trimmed = new_path.Trim('/');
        var lastSlash = trimmed.LastIndexOf('/');
        if (lastSlash > 0)
        {
            var folderPart = trimmed[..lastSlash];
            await graph.EnsureFolderExistsAsync(drive.Id!, folderPart, cancellationToken);
        }

        var content = await graph.ReadTextFileAsync(drive.Id!, old_path, cancellationToken)
                      ?? throw new Exception("File not found");

        await graph.WriteTextFileAsync(drive.Id!, new_path, content, cancellationToken);
        await graph.DeleteFileAsync(drive.Id!, old_path, cancellationToken);

        return "OK".ToTextContentBlock().ToCallToolResult();
    }));

    [Description("Please fill in the details for the new OneDrive memory file.")]
    public class OneDriveCreateMemory
    {

        [Required]
        [JsonPropertyName("text")]
        [Description("Text content to write to the file.")]
        public string Text { get; set; } = default!;
    }

    [Description("Fill in the details to insert a line into the memory file.")]
    public class OneDriveInsertMemory
    {

        [Required]
        [JsonPropertyName("line")]
        [Description("Line number to insert at (1-based).")]
        public int Line { get; set; }

        [Required]
        [JsonPropertyName("text")]
        [Description("The text to insert at the specified line.")]
        public string Text { get; set; } = default!;
    }

    [Description("Fill in the details to insert a line into the memory file.")]
    public class OneDriveReplaceMemory
    {
        [Required]
        [JsonPropertyName("text_to_replace")]
        [Description("Text to replace.")]
        public string TextToReplace { get; set; } = default!;

        [Required]
        [JsonPropertyName("new_text")]
        [Description("New text.")]
        public string NewText { get; set; } = default!;
    }


}

