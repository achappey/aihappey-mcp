using System.ComponentModel;
using System.Text;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Markdig;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DocumentFormat.OpenXml;
using MCPhappey.Tools.Extensions;
using MCPhappey.Common.Extensions;
using Microsoft.Extensions.DependencyInjection;
using MCPhappey.Core.Services;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Markdown;
using QuestPDF.Infrastructure;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MCPhappey.Tools.Memory.OneDrive;
using System.Text.Json;

namespace MCPhappey.Tools.AI;

public static class MarkdownService
{
    // 5) MARKDOWN TEXT -> PDF (.pdf), upload to OneDrive
    [Description("Create a PDF (.pdf) from Markdown TEXT and upload to OneDrive.")]
    [McpServerTool(Name = "markdown_to_pdf_from_text", ReadOnly = false, OpenWorld = false, Idempotent = false, Destructive = false)]
    public static async Task<CallToolResult?> MarkdownToPdf_FromText(
        [Description("Target filename without .pdf extension")] string fileName,
        [Description("Markdown content")] string markdown,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async graphClient =>
            {
                if (string.IsNullOrWhiteSpace(markdown))
                    throw new ArgumentException("Markdown content is required.", nameof(markdown));

                var safeName = SanitizeFileName(fileName);

                QuestPDF.Settings.License = LicenseType.Community;

                byte[] pdfBytes = QuestPDF.Fluent.Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Margin(2, Unit.Centimetre);
                        page.PageColor(Colors.White);
                        page.Content().Markdown(markdown);
                    });
                }).GeneratePdf();

                using var ms = new MemoryStream(pdfBytes);
                var uploaded = await graphClient.Upload(
                    $"{safeName}.pdf",
                    await BinaryData.FromStreamAsync(ms, cancellationToken),
                    cancellationToken);

                return uploaded?.ToCallToolResult();
            })
        );

    // 6) MARKDOWN URL -> PDF (.pdf), fetch via DownloadService, upload to OneDrive
    [Description("Create a PDF (.pdf) from a Markdown URL and upload to OneDrive.")]
    [McpServerTool(Name = "markdown_to_pdf_from_url", ReadOnly = false, OpenWorld = true, Idempotent = false, Destructive = false)]
    public static async Task<CallToolResult?> MarkdownToPdf_FromUrl(
        [Description("Markdown file URL (.md/.markdown), supports protected sources via DownloadService")] string markdownUrl,
        [Description("Target filename without .pdf extension")] string fileName,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async graphClient =>
            {
                if (string.IsNullOrWhiteSpace(markdownUrl))
                    throw new ArgumentException("markdownUrl is required.", nameof(markdownUrl));

                var downloadService = serviceProvider.GetRequiredService<DownloadService>();
                var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, markdownUrl, cancellationToken);
                var file = files.FirstOrDefault() ?? throw new FileNotFoundException($"No content found at {markdownUrl}");

                var md = Encoding.UTF8.GetString(file.Contents.ToArray());

                var safeName = SanitizeFileName(
                    string.IsNullOrWhiteSpace(fileName) ? GuessNameFromUrl(markdownUrl) : fileName
                );

                QuestPDF.Settings.License = LicenseType.Community;

                byte[] pdfBytes = QuestPDF.Fluent.Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Margin(2, Unit.Centimetre);
                        page.PageColor(Colors.White);
                        page.Content().Markdown(md);
                    });
                }).GeneratePdf();

                using var ms = new MemoryStream(pdfBytes);
                var uploaded = await graphClient.Upload(
                    $"{safeName}.pdf",
                    await BinaryData.FromStreamAsync(ms, cancellationToken),
                    cancellationToken);

                return uploaded?.ToCallToolResult();
            })
        );


    // 3) MARKDOWN TEXT -> HTML (.html), upload to OneDrive
    [Description("Create an HTML file (.html) from Markdown TEXT and upload to OneDrive.")]
    [McpServerTool(Name = "markdown_to_html_from_text", ReadOnly = false, OpenWorld = false, Idempotent = false, Destructive = false)]
    public static async Task<CallToolResult?> MarkdownToHtml_FromText(
        [Description("Target filename without .html extension")] string fileName,
        [Description("Markdown content")] string markdown,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async graphClient =>
            {
                if (string.IsNullOrWhiteSpace(markdown))
                    throw new ArgumentException("Markdown content is required.", nameof(markdown));

                var safeName = SanitizeFileName(fileName);
                var html = Markdig.Markdown.ToHtml(markdown ?? string.Empty);
                var wrappedHtml = WrapHtml(html);

                using var ms = new MemoryStream(Encoding.UTF8.GetBytes(wrappedHtml));
                var uploaded = await graphClient.Upload(
                    $"{safeName}.html",
                    await BinaryData.FromStreamAsync(ms, cancellationToken),
                    cancellationToken);

                return uploaded?.ToCallToolResult();
            })
        );

    // 4) MARKDOWN URL -> HTML (.html), fetch via DownloadService, upload to OneDrive
    [Description("Create an HTML file (.html) from a Markdown URL and upload to OneDrive.")]
    [McpServerTool(Name = "markdown_to_html_from_url", ReadOnly = false, OpenWorld = true, Idempotent = false, Destructive = false)]
    public static async Task<CallToolResult?> MarkdownToHtml_FromUrl(
        [Description("Publicly accessible Markdown file URL (.md/.markdown)")] string markdownUrl,
        [Description("Target filename without .html extension")] string fileName,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async graphClient =>
            {
                if (string.IsNullOrWhiteSpace(markdownUrl))
                    throw new ArgumentException("markdownUrl is required.", nameof(markdownUrl));

                var downloadService = serviceProvider.GetRequiredService<DownloadService>();
                var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, markdownUrl, cancellationToken);
                var file = files.FirstOrDefault() ?? throw new FileNotFoundException($"No content found at {markdownUrl}");

                var md = Encoding.UTF8.GetString(file.Contents.ToArray());

                var safeName = SanitizeFileName(
                    string.IsNullOrWhiteSpace(fileName) ? GuessNameFromUrl(markdownUrl) : fileName
                );

                var html = Markdig.Markdown.ToHtml(md ?? string.Empty);
                var wrappedHtml = WrapHtml(html);

                using var ms = new MemoryStream(Encoding.UTF8.GetBytes(wrappedHtml));
                var uploaded = await graphClient.Upload(
                    $"{safeName}.html",
                    await BinaryData.FromStreamAsync(ms, cancellationToken),
                    cancellationToken);

                return uploaded?.ToCallToolResult();
            })
        );



    [Description("Create a Word document (.docx) from Markdown TEXT and upload to OneDrive.")]
    [McpServerTool(Name = "markdown_to_word_from_text", ReadOnly = false, OpenWorld = false, Idempotent = false, Destructive = false)]
    public static async Task<CallToolResult?> MarkdownToWord_FromText(
        [Description("Target filename without .docx extension")] string fileName,
        [Description("Markdown content")] string markdown,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async graphClient =>
            {
                if (string.IsNullOrWhiteSpace(markdown))
                    throw new ArgumentException("Markdown content is required.", nameof(markdown));

                var safeName = SanitizeFileName(fileName);
                var html = Markdown.ToHtml(markdown);
                var wrappedHtml = WrapHtml(html);

                using var ms = new MemoryStream();
                CreateDocWithHtml(ms, wrappedHtml);
                ms.Position = 0;

                var uploaded = await graphClient.Upload(
                    $"{safeName}.docx",
                    await BinaryData.FromStreamAsync(ms, cancellationToken),
                    cancellationToken);

                return uploaded?.ToCallToolResult();
            })
        );

    // 2) MARKDOWN URL -> WORD (.docx), fetch via HttpClientFactory, upload to OneDrive
    [Description("Create a Word document (.docx) from a Markdown URL and upload to OneDrive.")]
    [McpServerTool(Name = "markdown_to_word_from_url", ReadOnly = false, OpenWorld = true, Idempotent = false, Destructive = false)]
    public static async Task<CallToolResult?> MarkdownToWord_FromUrl(
        [Description("Publicly accessible Markdown file URL (.md/.markdown)")] string markdownUrl,
        [Description("Target filename without .docx extension")] string fileName,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async graphClient =>
            {
                if (string.IsNullOrWhiteSpace(markdownUrl))
                    throw new ArgumentException("markdownUrl is required.", nameof(markdownUrl));

                var httpFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
                var downloadService = serviceProvider.GetRequiredService<DownloadService>();
                var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, markdownUrl, cancellationToken);
                var file = files.FirstOrDefault();

                var md = file?.Contents.ToString() ?? throw new Exception("Markdown not found");

                var safeName = SanitizeFileName(
                    string.IsNullOrWhiteSpace(fileName) ? GuessNameFromUrl(markdownUrl) : fileName
                );

                var html = Markdown.ToHtml(md);
                var wrappedHtml = WrapHtml(html);

                using var ms = new MemoryStream();
                CreateDocWithHtml(ms, wrappedHtml);
                ms.Position = 0;

                var uploaded = await graphClient.Upload(
                    $"{safeName}.docx",
                    await BinaryData.FromStreamAsync(ms, cancellationToken),
                    cancellationToken);

                return uploaded?.ToCallToolResult();
            })
        );

    // ---------- Helpers (same style as your OpenXML helpers) ----------
    private static void CreateDocWithHtml(Stream stream, string wrappedHtml)
    {
        using (var word = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var main = word.AddMainDocumentPart();
            main.Document = new DocumentFormat.OpenXml.Wordprocessing.Document(new Body());

            var chunk = main.AddAlternativeFormatImportPart(AlternativeFormatImportPartType.Html);
            using (var chunkStream = chunk.GetStream(FileMode.Create, FileAccess.Write))
            using (var writer = new StreamWriter(chunkStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(wrappedHtml);
            }

            main.Document.Body!.AppendChild(new AltChunk { Id = main.GetIdOfPart(chunk) });
            main.Document.Save();
        }
        stream.Flush();
    }

    private static string WrapHtml(string html)
    {
        var h = html ?? string.Empty;
        if (h.Contains("<html", StringComparison.OrdinalIgnoreCase)) return h;

        return $@"<!DOCTYPE html>
<html>
<head>
  <meta charset=""UTF-8"">
  <meta http-equiv=""X-UA-Compatible"" content=""IE=edge"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
  <title>Markdown</title>
</head>
<body>
{h}
</body>
</html>";
    }

    private static string SanitizeFileName(string? name)
    {
        var n = string.IsNullOrWhiteSpace(name) ? "markdown" : name.Trim();
        foreach (var ch in Path.GetInvalidFileNameChars()) n = n.Replace(ch, '_');
        return n.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) ? n[..^5] : n;
    }

    private static string GuessNameFromUrl(string url)
    {
        try
        {
            var u = new Uri(url);
            var file = Path.GetFileName(u.LocalPath);
            if (string.IsNullOrWhiteSpace(file)) return "markdown";
            var name = file.Replace(".markdown", "", StringComparison.OrdinalIgnoreCase)
                           .Replace(".md", "", StringComparison.OrdinalIgnoreCase);
            return string.IsNullOrWhiteSpace(name) ? "markdown" : name;
        }
        catch { return "markdown"; }
    }

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
    [Description("Read a Markdown (.md) file from OneDrive, or list a folder if path is a directory.")]
    [McpServerTool(
        Title = "Read Markdown",
        Name = "markdown_read",
        ReadOnly = true,
        OpenWorld = false,
        Destructive = false)]
    public static async Task<CallToolResult?> Markdown_Read(
        [Description("Markdown file or folder path (e.g. /Notes/Todo.md or /Notes).")] string path,
        RequestContext<CallToolRequestParams> context,
        [Description("Optional: [startLine, endLine] (1-based)")] int[]? view_range = null,
        [Description("Optional: OneDrive Drive ID (defaults to user drive).")] string? driveId = null,
        CancellationToken cancellationToken = default)
        => await context.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async graph =>
    {
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
    [Description("Create or overwrite a Markdown (.md) file at the given OneDrive path.")]
    [McpServerTool(
        Title = "Create Markdown",
        Name = "markdown_create",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> Markdown_Create(
        [Description("Markdown file path, e.g. /Projects/Notes/Todo.md ('.md' auto-appended).")] string path,
        [Description("Markdown content to write.")] string file_text,
        RequestContext<CallToolRequestParams> context,
        [Description("Optional: OneDrive Drive ID (defaults to user drive).")] string? driveId = null,
        CancellationToken cancellationToken = default)
        => await context.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async graph =>
    {
        var normalized = Normalize(path);
        var drive = driveId != null
            ? await graph.Drives[driveId].GetAsync(cancellationToken: cancellationToken) ?? throw new Exception($"Drive not found: {driveId}")
            : await graph.GetDefaultDriveAsync(cancellationToken) ?? throw new Exception("Could not resolve default OneDrive.");

        var (typed, notAccepted, _) = await context.Server.TryElicit(
            new MarkdownCreateInput { Text = file_text }, cancellationToken);

        var trimmed = normalized.Trim('/');
        var lastSlash = trimmed.LastIndexOf('/');
        if (lastSlash > 0)
        {
            var folderPart = trimmed[..lastSlash];
            await graph.EnsureFolderExistsAsync(drive.Id!, folderPart, cancellationToken);
        }

        var result = await graph.WriteTextFileAsync(drive.Id!, normalized, file_text, cancellationToken);
        return result.ToJsonContentBlock(result?.WebUrl!).ToCallToolResult();
    }));


    // ---------- INSERT ----------
    [Description("Insert text into a Markdown (.md) file at a specific line.")]
    [McpServerTool(
        Title = "Insert Markdown Line",
        Name = "markdown_insert",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> Markdown_Insert(
        [Description("Markdown file path, e.g. /Notes/TaskList.md ('.md' auto-appended).")] string path,
        [Description("1-based line number to insert at.")] int insert_line,
        [Description("Text to insert at the specified line.")] string insert_text,
        RequestContext<CallToolRequestParams> context,
        [Description("Optional: OneDrive Drive ID (defaults to user drive).")] string? driveId = null,
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
            new MarkdownInsertInput { Line = insert_line, Text = insert_text }, cancellationToken);

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
    [Description("Find and replace text inside a Markdown (.md) file.")]
    [McpServerTool(
        Title = "Replace Markdown Text",
        Name = "markdown_replace",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> Markdown_Replace(
        [Description("Markdown file path, e.g. /Notes/Ideas.md ('.md' auto-appended).")] string path,
        [Description("Text to find.")] string old_str,
        [Description("Replacement text.")] string new_str,
        RequestContext<CallToolRequestParams> context,
        [Description("Optional: OneDrive Drive ID (defaults to user drive).")] string? driveId = null,
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
            new MarkdownReplaceInput { TextToReplace = old_str, NewText = new_str }, cancellationToken);

        var content = await graph.ReadTextFileAsync(drive.Id!, normalized, cancellationToken)
                      ?? throw new Exception("File not found");

        content = content.Replace(typed.TextToReplace, typed.NewText);
        await graph.WriteTextFileAsync(drive.Id!, normalized, content, cancellationToken);
        return "OK".ToTextContentBlock().ToCallToolResult();
    }));


    // ---------- INPUT MODELS ----------
    [Description("Markdown content to create a new file.")]
    public class MarkdownCreateInput
    {
        [JsonPropertyName("text")]
        [Required]
        [Description("Markdown text to write to the file.")]
        public string Text { get; set; } = default!;
    }

    [Description("Parameters for inserting text into a Markdown file.")]
    public class MarkdownInsertInput
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

    [Description("Parameters for replacing text in a Markdown file.")]
    public class MarkdownReplaceInput
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
