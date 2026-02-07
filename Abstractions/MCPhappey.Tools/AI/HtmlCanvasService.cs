using System.ComponentModel;
using HtmlAgilityPack;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.Extensions;
using MCPhappey.Tools.Memory.OneDrive;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.AI;

public static class HtmlCanvasService
{
    private static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new Exception("Path is required.");

        var p = path.Replace("\\", "/").Trim();
        if (!p.StartsWith("/")) p = "/" + p;
        if (!p.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            p += ".html";

        return p;
    }

    // ---------- READ ----------
    [Description("Read an HTML file from OneDrive.")]
    [McpServerTool(
        Title = "Read HTML Canvas",
        Name = "onedrive_html_read",
        ReadOnly = true,
        OpenWorld = false,
        Destructive = false)]
    public static async Task<CallToolResult?> OneDriveHtml_Read(
        string path,
        RequestContext<CallToolRequestParams> context,
        string? driveId = null,
        CancellationToken cancellationToken = default)
        => await context.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async graph =>
    {
        var normalized = Normalize(path);

        var drive = driveId != null
            ? await graph.Drives[driveId].GetAsync(cancellationToken: cancellationToken)
            : await graph.GetDefaultDriveAsync(cancellationToken)
            ?? throw new Exception("Drive not found.");

        var content = await graph.ReadTextFileAsync(drive?.Id!, normalized, cancellationToken)
                      ?? throw new Exception("File not found");

        return content.ToTextContentBlock().ToCallToolResult();
    }));

    // ---------- CREATE / OVERWRITE ----------
    [Description("Create or overwrite an HTML file.")]
    [McpServerTool(
        Title = "Create HTML Canvas",
        Name = "onedrive_html_create",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> OneDriveHtml_Create(
        string path,
        string html,
        RequestContext<CallToolRequestParams> context,
        string? driveId = null,
        CancellationToken cancellationToken = default)
        => await context.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async graph =>
    {
        var normalized = Normalize(path);

        // Validate HTML
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        if (doc.ParseErrors.Any())
            throw new Exception("Invalid HTML.");

        var drive = (driveId != null
            ? await graph.Drives[driveId].GetAsync(cancellationToken: cancellationToken)
            : await graph.GetDefaultDriveAsync(cancellationToken))
            ?? throw new Exception("Drive not found.");

        await graph.WriteTextFileAsync(drive.Id!, normalized, doc.DocumentNode.OuterHtml, cancellationToken);

        return drive.WebUrl!.ToTextContentBlock().ToCallToolResult();
    }));

    // ---------- SET INNER HTML ----------
    [Description("Replace inner HTML of an element selected by CSS/XPath.")]
    [McpServerTool(
        Title = "Set HTML element content",
        Name = "onedrive_html_set_inner",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> OneDriveHtml_SetInner(
        string path,
        [Description("Selector to locate the target HTML element. Uses XPath (not CSS). Examples: \"//body\", \"//div[@id='main']\", \"//section[contains(@class,'content')]\". Must resolve to exactly one element.")] string selector,
        string html,
        RequestContext<CallToolRequestParams> context,
        string? driveId = null,
        CancellationToken cancellationToken = default)
        => await context.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async graph =>
    {
        var normalized = Normalize(path);

        var drive = driveId != null
            ? await graph.Drives[driveId].GetAsync(cancellationToken: cancellationToken)
            : await graph.GetDefaultDriveAsync(cancellationToken)
            ?? throw new Exception("Drive not found.");

        var content = await graph.ReadTextFileAsync(drive?.Id!, normalized, cancellationToken)
                      ?? throw new Exception("File not found");

        var doc = new HtmlDocument();
        doc.LoadHtml(content);

        var node = doc.DocumentNode.SelectSingleNode(selector)
                   ?? throw new Exception("Target element not found.");

        node.InnerHtml = html;

        await graph.WriteTextFileAsync(drive?.Id!, normalized, doc.DocumentNode.OuterHtml, cancellationToken);
        return "OK".ToTextContentBlock().ToCallToolResult();
    }));

    // ---------- REPLACE ELEMENT ----------
    [Description("Replace an entire HTML element selected by CSS/XPath.")]
    [McpServerTool(
        Title = "Replace HTML element",
        Name = "onedrive_html_replace_element",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> OneDriveHtml_ReplaceElement(
        string path,
        [Description("Selector to locate the target HTML element. Uses XPath (not CSS). Examples: \"//body\", \"//div[@id='main']\", \"//section[contains(@class,'content')]\". Must resolve to exactly one element.")] string selector,
        string replacement_html,
        RequestContext<CallToolRequestParams> context,
        string? driveId = null,
        CancellationToken cancellationToken = default)
        => await context.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async graph =>
    {
        var normalized = Normalize(path);

        var drive = driveId != null
            ? await graph.Drives[driveId].GetAsync(cancellationToken: cancellationToken)
            : await graph.GetDefaultDriveAsync(cancellationToken)
            ?? throw new Exception("Drive not found.");

        var content = await graph.ReadTextFileAsync(drive?.Id!, normalized, cancellationToken)
                      ?? throw new Exception("File not found");

        var doc = new HtmlDocument();
        doc.LoadHtml(content);

        var target = doc.DocumentNode.SelectSingleNode(selector)
                     ?? throw new Exception("Target element not found.");

        var fragment = new HtmlDocument();
        fragment.LoadHtml(replacement_html);

        var newNode = fragment.DocumentNode.FirstChild
                      ?? throw new Exception("Invalid replacement HTML.");

        target.ParentNode.ReplaceChild(newNode, target);

        await graph.WriteTextFileAsync(drive?.Id!, normalized, doc.DocumentNode.OuterHtml, cancellationToken);
        return "OK".ToTextContentBlock().ToCallToolResult();
    }));

    // ---------- COPY (BY URL) ----------
    [Description("Copy an HTML document referenced by a OneDrive sharing URL into your own OneDrive using a specified filename, and return the new relative path.")]
    [McpServerTool(
        Title = "Copy HTML Canvas (by URL)",
        Name = "onedrive_html_copy",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = false)]
    public static async Task<CallToolResult?> OneDriveHtml_Copy(
        [Description("OneDrive or SharePoint sharing URL of the HTML file.")]
        string sourceLink,
        [Description("Filename for the new HTML file (e.g. file-subject.html). .html extension is enforced.")]
        string filename,
        [Description("Destination folder path in your OneDrive (e.g. /Canvases). Defaults to root.")]
        string? destinationFolder,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default)
        => await context.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async graph =>
        await context.WithStructuredContent(async () =>
    {
        if (string.IsNullOrWhiteSpace(filename))
            throw new Exception("Filename is required.");

        var safeFilename = filename.Trim().Replace("\\", "/");
        if (safeFilename.Contains("/"))
            throw new Exception("Filename must not contain path separators.");

        if (!safeFilename.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            safeFilename += ".html";

        var destFolder = string.IsNullOrWhiteSpace(destinationFolder)
            ? "/"
            : destinationFolder!.Replace("\\", "/").TrimEnd('/');

        // 1. Resolve shared item via /shares/{encodedUrl}
        var sharedItem = await graph.GetDriveItem(
            sourceLink,
            cancellationToken: cancellationToken);

        if (sharedItem?.Name!.EndsWith(".html", StringComparison.OrdinalIgnoreCase) != true)
            throw new Exception("Shared file is not an HTML document.");

        // 2. Download content
        var content = await graph.ReadTextFileAsync(
            sharedItem.ParentReference!.DriveId!,
            sharedItem.ParentReference.Path!.Split(":").Last() + "/" + sharedItem.Name,
            cancellationToken)
            ?? throw new Exception("Failed to read shared HTML content.");

        // 3. Write into own drive with explicit filename
        var ownDrive = await graph.GetDefaultDriveAsync(cancellationToken)
            ?? throw new Exception("Own drive not found.");

        var targetPath = $"{destFolder}/{safeFilename}";

        var driveItem = await graph.WriteTextFileAsync(
            ownDrive.Id!,
            targetPath,
            content,
            cancellationToken);

        return new
        {
            path = targetPath,
            url = driveItem?.WebUrl
        };
    })));


    // ---------- CLEAR ELEMENT ----------
    [Description("Clear all inner content of an HTML element, leaving the element itself intact.")]
    [McpServerTool(
        Title = "Clear HTML element",
        Name = "onedrive_html_clear_element",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> OneDriveHtml_ClearElement(
        string path,
        [Description("Selector to locate the target HTML element. Uses XPath (not CSS). Examples: \"//body\", \"//main[@class='content']\", \"//div[@id='main']\". Must resolve to exactly one element.")]
    string selector,
        RequestContext<CallToolRequestParams> context,
        string? driveId = null,
        CancellationToken cancellationToken = default)
        => await context.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async graph =>
    {
        var normalized = Normalize(path);

        var drive = driveId != null
            ? await graph.Drives[driveId].GetAsync(cancellationToken: cancellationToken)
            : await graph.GetDefaultDriveAsync(cancellationToken)
            ?? throw new Exception("Drive not found.");

        var content = await graph.ReadTextFileAsync(drive?.Id!, normalized, cancellationToken)
                      ?? throw new Exception("File not found.");

        var doc = new HtmlDocument();
        doc.LoadHtml(content);

        var node = doc.DocumentNode.SelectSingleNode(selector)
                   ?? throw new Exception("Target element not found.");

        // INTENT: clear, not replace
        node.RemoveAllChildren();

        await graph.WriteTextFileAsync(
            drive?.Id!,
            normalized,
            doc.DocumentNode.OuterHtml,
            cancellationToken);

        return "OK".ToTextContentBlock().ToCallToolResult();
    }));

    // ---------- ADD IMAGE (BASE64 INTO HTML) ----------
    [Description("Download an image from a OneDrive or SharePoint URL, convert it to base64, and inject it into an HTML file placeholder (e.g. {imageBase64}).")]
    [McpServerTool(
        Title = "Add image into HTML template",
        Name = "onedrive_html_add_image_base64",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> OneDriveHtml_AddImageBase64(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("SharePoint/OneDrive URL of the image file.")]
    string imageFileUrl,
        [Description("SharePoint/OneDrive URL of the HTML file that contains the placeholder.")]
    string htmlFileUrl,
        [Description("Placeholder variable name to replace. You can pass either 'imageBase64' or '{imageBase64}'.")]
    string variableName,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async graphClient =>
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var mcpServer = requestContext.Server;

        // ---------- DOWNLOAD IMAGE ----------
        var imageFiles = await downloadService.DownloadContentAsync(
            serviceProvider,
            mcpServer,
            imageFileUrl,
            cancellationToken);

        var imageFile = imageFiles.FirstOrDefault()
            ?? throw new FileNotFoundException($"No image content found at '{imageFileUrl}'.");

        var mimeType = string.IsNullOrWhiteSpace(imageFile.MimeType)
            ? "image/jpeg"
            : imageFile.MimeType;

        var base64 = Convert.ToBase64String(imageFile.Contents);

        // ready for <img src="">
        var dataUri = $"data:{mimeType};base64,{base64}";

        // ---------- DOWNLOAD HTML ----------
        var htmlFiles = await downloadService.DownloadContentAsync(
            serviceProvider,
            mcpServer,
            htmlFileUrl,
            cancellationToken);

        var htmlFile = htmlFiles.FirstOrDefault()
            ?? throw new FileNotFoundException($"No HTML content found at '{htmlFileUrl}'.");

        var html = htmlFile.Contents.ToString();

        // ---------- PLACEHOLDER ----------
        var token = variableName.Trim();
        if (token.StartsWith("{") && token.EndsWith("}") && token.Length > 2)
            token = token[1..^1];

        var placeholder = $"{{{token}}}";

        var updatedHtml = html.Replace(
            placeholder,
            dataUri,
            StringComparison.Ordinal);

        // ---------- UPLOAD ----------
        var updated = await graphClient.UploadBinaryDataAsync(
            htmlFileUrl,
            BinaryData.FromString(updatedHtml),
            cancellationToken)
            ?? throw new FileNotFoundException($"Failed to update HTML file at '{htmlFileUrl}'.");

        return updated.ToResourceLinkBlock(updated.Name!).ToCallToolResult();
    }));



}
