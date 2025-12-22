using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using MCPhappey.Tools.Memory.OneDrive;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NAudio.Wave;

namespace MCPhappey.Tools.AI;

public static partial class CanvasService
{
    private static string Normalize(string path, bool isDirectory = false)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new Exception("Path is required.");

        var p = path.Replace("\\", "/").Trim();
        if (!p.StartsWith("/")) p = "/" + p;

        if (!isDirectory && !p.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            p += ".md";

        return p;
    }

    // ---------- READ ----------
    [Description("Read a canvas (.md) file from OneDrive, or list a directory if path points to a folder.")]
    [McpServerTool(
        Title = "Read Canvas",
        Name = "onedrive_canvas_read",
        ReadOnly = true,
        OpenWorld = false,
        Destructive = false)]
    public static async Task<CallToolResult?> OneDriveCanvas_Read(
        [Description("Canvas path (e.g. /Projects/Notes/Todo.md or /Projects/Notes). If no .md extension is provided, a directory is assumed unless the exact file exists.")]
           string path,
        RequestContext<CallToolRequestParams> context,
        [Description("Optional: [startLine, endLine] (1-based)")] int[]? view_range = null,
        [Description("Optional: OneDrive Drive ID. If omitted, the user's default drive is used.")] string? driveId = null,
        CancellationToken cancellationToken = default)
        => await context.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async graph =>
    {
        // If caller passed a directory (ends with / or no dot), treat as dir
        var isDir = !path.Contains('.') || path.EndsWith("/");
        var normalized = Normalize(path, isDirectory: isDir);

        var drive = driveId != null
            ? await graph.Drives[driveId].GetAsync(cancellationToken: cancellationToken) ?? throw new Exception($"Drive not found: {driveId}")
            : await graph.GetDefaultDriveAsync(cancellationToken) ?? throw new Exception("Could not resolve default OneDrive.");

        await graph.EnsureRootFolderExistsAsync(drive.Id!, cancellationToken);

        if (isDir)
        {
            var files = await graph.ListFilesAsync(drive.Id!, normalized, cancellationToken);
            return normalized.ToMemoryListResult(files);
        }

        var content = await graph.ReadTextFileAsync(drive.Id!, normalized, cancellationToken)
                      ?? throw new Exception("File not found");

        if (view_range is { Length: 2 })
        {
            var lines = content.Split('\n');
            var start = Math.Max(1, view_range[0]);
            var end = Math.Min(lines.Length, view_range[1]);
            if (start <= end)
                content = string.Join("\n", lines.Skip(start - 1).Take(end - start + 1));
        }

        return content.ToTextContentBlock().ToCallToolResult();
    }));

    // ---------- CREATE ----------
    [Description("Create or overwrite a canvas (.md) file at the specified OneDrive path.")]
    [McpServerTool(
        Title = "Create Canvas",
        Name = "onedrive_canvas_create",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> OneDriveCanvas_Create(
        [Description("Canvas file path, e.g. /Projects/Notes/Todo.md ('.md' auto-appended if missing).")]
        string path,
        [Description("Markdown content to write.")] string file_text,
        RequestContext<CallToolRequestParams> context,
        [Description("Optional: OneDrive Drive ID. If omitted, the user's default drive is used.")] string? driveId = null,
        CancellationToken cancellationToken = default)
        => await context.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async graph =>
    {
        var normalized = Normalize(path);
        var drive = driveId != null
            ? await graph.Drives[driveId].GetAsync(cancellationToken: cancellationToken) ?? throw new Exception($"Drive not found: {driveId}")
            : await graph.GetDefaultDriveAsync(cancellationToken) ?? throw new Exception("Could not resolve default OneDrive.");
        //await graph.EnsureRootFolderExistsAsync(drive.Id!, cancellationToken);

        var (typed, notAccepted, _) = await context.Server.TryElicit(
             new CanvasCreateInput { Text = file_text }, cancellationToken);

        // Ensure folder structure
        var trimmed = normalized.Trim('/');
        var lastSlash = trimmed.LastIndexOf('/');
        if (lastSlash > 0)
        {
            var folderPart = trimmed[..lastSlash];
            //    await graph.EnsureFolderExistsAsync(drive.Id!, folderPart, cancellationToken);
        }

        var result = await graph.WriteTextFileAsync(drive.Id!, normalized, file_text, cancellationToken);
        return result.ToJsonContentBlock(result?.WebUrl!).ToCallToolResult();
    }));

    // ---------- INSERT ----------
    [Description("Insert a line into a canvas (.md) file at the given 1-based index.")]
    [McpServerTool(
        Title = "Insert Canvas Line",
        Name = "onedrive_canvas_insert",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> OneDriveCanvas_Insert(
        [Description("Canvas file path, e.g. /Projects/Notes/Todo.md ('.md' auto-appended if missing).")]
        string path,
        [Description("1-based line number to insert at. If beyond EOF, appends.")] int insert_line,
        [Description("Text to insert at the specified line.")] string insert_text,
        RequestContext<CallToolRequestParams> context,
        [Description("Optional: OneDrive Drive ID. If omitted, the user's default drive is used.")] string? driveId = null,
        CancellationToken cancellationToken = default)
        => await context.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async graph =>
    {
        var normalized = Normalize(path);
        var drive = driveId != null
            ? await graph.Drives[driveId].GetAsync(cancellationToken: cancellationToken) ?? throw new Exception($"Drive not found: {driveId}")
            : await graph.GetDefaultDriveAsync(cancellationToken) ?? throw new Exception("Could not resolve default OneDrive.");
        await graph.EnsureRootFolderExistsAsync(drive.Id!, cancellationToken);

        var (typed, notAccepted, _) = await context.Server.TryElicit(
            new CanvasInsertInput { Line = insert_line, Text = insert_text }, cancellationToken);

        // Ensure folder structure
        var trimmed = normalized.Trim('/');
        var lastSlash = trimmed.LastIndexOf('/');
        if (lastSlash > 0)
        {
            var folderPart = trimmed[..lastSlash];
            await graph.EnsureFolderExistsAsync(drive.Id!, folderPart, cancellationToken);
        }

        var content = await graph.ReadTextFileAsync(drive.Id!, normalized, cancellationToken)
                      ?? throw new Exception("File not found");
        var lines = content.Split('\n').ToList();

        typed.Line = Math.Max(1, typed.Line);
        if (typed.Line > lines.Count + 1)
            lines.Add(typed.Text);
        else
            lines.Insert(typed.Line - 1, typed.Text);

        await graph.WriteTextFileAsync(drive.Id!, normalized, string.Join("\n", lines), cancellationToken);
        return "OK".ToTextContentBlock().ToCallToolResult();
    }));

    // ---------- REPLACE ----------
    [Description("Replace text inside a canvas (.md) file (simple find/replace).")]
    [McpServerTool(
        Title = "Replace Canvas Text",
        Name = "onedrive_canvas_replace",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> OneDriveCanvas_Replace(
        [Description("Canvas file path, e.g. /Projects/Notes/Todo.md ('.md' auto-appended if missing).")]
        string path,
        [Description("Text to find.")] string old_str,
        [Description("Replacement text.")] string new_str,
        RequestContext<CallToolRequestParams> context,
        [Description("Optional: OneDrive Drive ID. If omitted, the user's default drive is used.")] string? driveId = null,
        CancellationToken cancellationToken = default)
        => await context.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async graph =>
    {
        var normalized = Normalize(path);
        var drive = driveId != null
            ? await graph.Drives[driveId].GetAsync(cancellationToken: cancellationToken) ?? throw new Exception($"Drive not found: {driveId}")
            : await graph.GetDefaultDriveAsync(cancellationToken) ?? throw new Exception("Could not resolve default OneDrive.");
        await graph.EnsureRootFolderExistsAsync(drive.Id!, cancellationToken);

        var (typed, notAccepted, _) = await context.Server.TryElicit(
            new CanvasReplaceInput { TextToReplace = old_str, NewText = new_str }, cancellationToken);

        var content = await graph.ReadTextFileAsync(drive.Id!, normalized, cancellationToken)
                      ?? throw new Exception("File not found");

        content = content.Replace(typed.TextToReplace, typed.NewText);
        await graph.WriteTextFileAsync(drive.Id!, normalized, content, cancellationToken);
        return "OK".ToTextContentBlock().ToCallToolResult();
    }));

    // ---------- ELICIT INPUT TYPES ----------
    [Description("Please fill in the canvas content.")]
    public class CanvasCreateInput
    {
        [JsonPropertyName("text")]
        [Required]
        [Description("Markdown content to write to the file.")]
        public string Text { get; set; } = default!;
    }

    [Description("Please fill in the canvas insert details.")]
    public class CanvasInsertInput
    {
        [JsonPropertyName("line")]
        [Required]
        [Description("1-based line number to insert at.")]
        public int Line { get; set; }

        [JsonPropertyName("text")]
        [Required]
        [Description("Text to insert.")]
        public string Text { get; set; } = default!;
    }

    [Description("Please fill in the canvas replace details.")]
    public class CanvasReplaceInput
    {
        [JsonPropertyName("text_to_replace")]
        [Required]
        [Description("Text to replace.")]
        public string TextToReplace { get; set; } = default!;

        [JsonPropertyName("new_text")]
        [Required]
        [Description("Replacement text.")]
        public string NewText { get; set; } = default!;
    }
}

