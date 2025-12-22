using System.ComponentModel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using DocumentFormat.OpenXml;
using MCPhappey.Common.Extensions;
using Markdig;
using System.Text;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MCPhappey.Core.Services;
using MCPhappey.Tools.Extensions;
using System.ComponentModel.DataAnnotations;

namespace MCPhappey.Tools.OpenXML.Word;

public static class WordPlugin
{
    [Description("Replace specific text in a Word document with tracked changes (shows insert/delete in Word)")]
    [McpServerTool(Name = "openxml_word_replace_text_with_track_changes", ReadOnly = false, OpenWorld = false, Idempotent = false, Destructive = true)]
    public static async Task<CallToolResult?> OpenXMLWord_ReplaceTextWithTrackChanges(
    [Description("Target Word document URL (.docx)")] string documentUrl,
    [Description("Original text to search for (case-insensitive)"), MinLength(50)] string originalText,
    [Description("Replacement text to insert")] string replacementText,
    IServiceProvider serviceProvider,
    RequestContext<CallToolRequestParams> requestContext,
    CancellationToken cancellationToken = default)
    => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async (graphClient) =>
        {
            if (string.IsNullOrWhiteSpace(originalText))
                throw new ArgumentException("Original text cannot be empty.", nameof(originalText));

            var downloadService = serviceProvider.GetRequiredService<DownloadService>();

            // 1️⃣ Download the existing document
            var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, documentUrl, cancellationToken);
            var file = files.FirstOrDefault() ?? throw new FileNotFoundException($"No document found at {documentUrl}");

            using var input = new MemoryStream();
            await file.Contents.ToStream().CopyToAsync(input, cancellationToken);
            input.Position = 0;

            // 2️⃣ Modify in memory with tracked changes
            using (WordprocessingDocument wordDoc = WordprocessingDocument.Open(input, true))
            {
                var mainPart = wordDoc.MainDocumentPart ?? throw new InvalidOperationException("Missing MainDocumentPart");

                // Ensure track revisions is enabled
                var settingsPart = mainPart.DocumentSettingsPart ?? mainPart.AddNewPart<DocumentSettingsPart>();
                settingsPart.Settings ??= new Settings();

                if (!settingsPart.Settings.Elements<TrackRevisions>().Any())
                    settingsPart.Settings.AppendChild(new TrackRevisions());

                // Search each paragraph
                var paragraphs = mainPart.Document.Body?.Descendants<Paragraph>() ?? Enumerable.Empty<Paragraph>();

                foreach (var paragraph in paragraphs)
                {
                    var text = paragraph.InnerText;
                    if (string.IsNullOrWhiteSpace(text))
                        continue;

                    int matchIndex = text.IndexOf(originalText, StringComparison.OrdinalIgnoreCase);
                    if (matchIndex < 0)
                        continue;

                    // Wipe and rebuild paragraph runs
                    paragraph.RemoveAllChildren<Run>();

                    // Text before match
                    if (matchIndex > 0)
                    {
                        var before = text[..matchIndex];
                        paragraph.AppendChild(new Run(new Text(before) { Space = SpaceProcessingModeValues.Preserve }));
                    }

                    // Deleted run (original)
                    var deletedRun = new DeletedRun()
                    {
                        Author = requestContext.Server.ServerOptions.ServerInfo?.Title,
                        Date = DateTime.Now,
                        Id = Guid.NewGuid().ToString()
                    };
                    deletedRun.AppendChild(new Run(new Text(originalText) { Space = SpaceProcessingModeValues.Preserve }));
                    paragraph.AppendChild(deletedRun);

                    // Inserted run (replacement)
                    var insertedRun = new InsertedRun()
                    {
                        Author = requestContext.Server.ServerOptions.ServerInfo?.Title,
                        Date = DateTime.Now,
                        Id = Guid.NewGuid().ToString()
                    };
                    insertedRun.AppendChild(new Run(new Text(replacementText) { Space = SpaceProcessingModeValues.Preserve }));
                    paragraph.AppendChild(insertedRun);

                    // Remaining text after match
                    int afterStart = matchIndex + originalText.Length;
                    if (afterStart < text.Length)
                    {
                        var after = text[afterStart..];
                        paragraph.AppendChild(new Run(new Text(after) { Space = SpaceProcessingModeValues.Preserve }));
                    }
                }

                mainPart.Document.Save();
            }

            // 3️⃣ Upload the updated doc back
            input.Flush();
            input.Position = 0;

            var updated = await graphClient.UploadBinaryDataAsync(documentUrl, new BinaryData(input.ToArray()), cancellationToken);
            return updated?.ToResourceLinkBlock(updated?.Name!).ToCallToolResult();
        }));


    // 1) CREATE NEW DOCX FROM INPUT FILE (auto-detect MIME)
    [Description("Create a new Word document (.docx) from an input file URL (auto MIME detect: md/html/txt)")]
    [McpServerTool(Name = "openxml_word_create_from_input_file", ReadOnly = false, OpenWorld = false, Idempotent = false, Destructive = false)]
    public static async Task<CallToolResult?> OpenXMLWord_CreateFromInputFile(
        [Description("Input file URL (md, html, txt, xml, mht/mhtml, docx)")] string inputFileUrl,
        [Description("Target filename without .docx extension")] string targetFileName,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async (graphClient) =>
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();

        // Download content
        var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, inputFileUrl, cancellationToken);
        var src = files.FirstOrDefault() ?? throw new FileNotFoundException($"No content found at {inputFileUrl}");

        var effectiveMime = GetEffectiveMimeType(src.MimeType, inputFileUrl);
        var data = src.Contents.ToArray();

        // Prepare payload (convert md → html if needed)
        var (partType, payload) = PrepareImportPayload(effectiveMime, data);

        var safeName = SanitizeFileName(targetFileName);
        using var ms = new MemoryStream();

        using (var word = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
        {
            var main = word.AddMainDocumentPart();
            main.Document = new Document(new Body());

            var chunk = main.AddAlternativeFormatImportPart(partType);
            using (var s = chunk.GetStream(FileMode.Create, FileAccess.Write))
                s.Write(payload, 0, payload.Length);

            main.Document.Body!.AppendChild(new AltChunk { Id = main.GetIdOfPart(chunk) });
            main.Document.Save();
        }

        ms.Flush();
        ms.Position = 0;

        var uploaded = await graphClient.Upload(
            $"{safeName}.docx",
            await BinaryData.FromStreamAsync(ms, cancellationToken),
            cancellationToken);

        return uploaded?.ToCallToolResult();
    }));

    // 2) CREATE NEW DOCX FROM .DOTX TEMPLATE + INPUT FILE (auto-detect MIME)
    [Description("Create a new Word document (.docx) from a .dotx template URL and an input file URL (auto MIME detect)")]
    [McpServerTool(Name = "openxml_word_create_from_template_file", ReadOnly = false, OpenWorld = false, Idempotent = false, Destructive = false)]
    public static async Task<CallToolResult?> OpenXMLWord_CreateFromTemplateFile(
        [Description("Target filename without .docx extension")] string fileName,
        [Description("Template URL (.dotx or .docx)")] string templateUrl,
        [Description("Input file URL (md, html, txt)")] string inputFileUrl,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async (graphClient) =>
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();

        // Download template
        var tFiles = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, templateUrl, cancellationToken);
        var tFile = tFiles.FirstOrDefault() ?? throw new FileNotFoundException($"No template found at {templateUrl}");

        using var templateMs = new MemoryStream();
        await tFile.Contents.ToStream().CopyToAsync(templateMs, cancellationToken);
        templateMs.Position = 0;

        // Download input file
        var sFiles = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, inputFileUrl, cancellationToken);
        var sFile = sFiles.FirstOrDefault() ?? throw new FileNotFoundException($"No content found at {inputFileUrl}");

        var effectiveMime = GetEffectiveMimeType(sFile.MimeType, inputFileUrl);
        var srcBytes = sFile.Contents.ToArray();
        var (partType, payload) = PrepareImportPayload(effectiveMime, srcBytes);

        // Inject into template
        using (var wordDoc = WordprocessingDocument.Open(templateMs, true))
        {
            if (wordDoc.DocumentType != WordprocessingDocumentType.Document)
                wordDoc.ChangeDocumentType(WordprocessingDocumentType.Document);

            var main = wordDoc.MainDocumentPart ?? wordDoc.AddMainDocumentPart();
            main.Document ??= new Document(new Body());
            main.Document.Body ??= new Body();

            var chunk = main.AddAlternativeFormatImportPart(partType);
            using (var s = chunk.GetStream(FileMode.Create, FileAccess.Write))
                s.Write(payload, 0, payload.Length);

            main.Document.Body.AppendChild(new AltChunk { Id = main.GetIdOfPart(chunk) });
            main.Document.Save();
        }

        templateMs.Flush();
        templateMs.Position = 0;

        var safeName = SanitizeFileName(fileName);
        var uploaded = await graphClient.Upload(
            $"{safeName}.docx",
            await BinaryData.FromStreamAsync(templateMs, cancellationToken),
            cancellationToken);

        return uploaded?.ToCallToolResult();
    }));

    // 3) APPEND CONTENT TO EXISTING DOCX FROM INPUT FILE (auto-detect MIME)
    [Description("Append content from an input file URL (md/html/txt) to an existing Word document (.docx)")]
    [McpServerTool(Name = "openxml_word_append_from_file", ReadOnly = false, OpenWorld = false, Idempotent = false, Destructive = true)]
    public static async Task<CallToolResult?> OpenXMLWord_AppendFromFile(
        [Description("Target Word document URL (.docx)")] string targetUrl,
        [Description("Input file URL (md, html, txt)")] string inputFileUrl,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async (graphClient) =>
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();

        // Download target docx
        var tFiles = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, targetUrl, cancellationToken);
        var tFile = tFiles.FirstOrDefault() ?? throw new FileNotFoundException($"No Word document found at {targetUrl}");

        using var docStream = new MemoryStream();
        await tFile.Contents.ToStream().CopyToAsync(docStream, cancellationToken);
        docStream.Position = 0;

        // Download input file
        var sFiles = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, inputFileUrl, cancellationToken);
        var sFile = sFiles.FirstOrDefault() ?? throw new FileNotFoundException($"No content found at {inputFileUrl}");

        var effectiveMime = GetEffectiveMimeType(sFile.MimeType, inputFileUrl);
        var srcBytes = sFile.Contents.ToArray();
        var (partType, payload) = PrepareImportPayload(effectiveMime, srcBytes);

        // Append via AltChunk
        using (var wordDoc = WordprocessingDocument.Open(docStream, true))
        {
            var main = wordDoc.MainDocumentPart ?? throw new InvalidOperationException("Missing MainDocumentPart");
            main.Document ??= new Document(new Body());
            main.Document.Body ??= new Body();

            var chunkId = "chunk_" + Guid.NewGuid().ToString("N");
            var chunk = main.AddAlternativeFormatImportPart(partType, chunkId);

            using (var s = chunk.GetStream(FileMode.Create, FileAccess.Write))
                s.Write(payload, 0, payload.Length);

            main.Document.Body.AppendChild(new AltChunk { Id = chunkId });
            main.Document.Save();
        }

        // Upload back in-place
        docStream.Flush();
        docStream.Position = 0;

        var updated = await graphClient.UploadBinaryDataAsync(targetUrl, new BinaryData(docStream.ToArray()), cancellationToken);
        return updated?.ToResourceLinkBlock(updated?.Name!).ToCallToolResult();
    }));


    private static (PartTypeInfo partType, byte[] payload) PrepareImportPayload(string mimeType, byte[] data)
    {
        // Normalize markdown → HTML; choose proper AltChunk type
        var mt = (mimeType ?? string.Empty).Trim().ToLowerInvariant();

        if (mt is "text/markdown" or "markdown")
        {
            var md = Encoding.UTF8.GetString(data ?? Array.Empty<byte>());
            var html = Markdig.Markdown.ToHtml(md ?? string.Empty);
            var wrapped = WrapHtml(html);
            return (AlternativeFormatImportPartType.Html, Encoding.UTF8.GetBytes(wrapped));
        }

        // For text/html ensure it is a full HTML doc
        if (mt is "text/html" or "application/xhtml+xml")
        {
            var html = Encoding.UTF8.GetString(data ?? Array.Empty<byte>());
            var wrapped = WrapHtml(html);
            return (AlternativeFormatImportPartType.Html, Encoding.UTF8.GetBytes(wrapped));
        }

        // Map known types directly
        var partType = mt switch
        {
            "text/plain" => AlternativeFormatImportPartType.TextPlain,
            "application/xml" or "text/xml" => AlternativeFormatImportPartType.Xml,
            "message/rfc822" or "application/x-mimearchive" or "multipart/related"
                                                           => AlternativeFormatImportPartType.Mht,
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
                                                           => AlternativeFormatImportPartType.WordprocessingML,
            _ => throw new ArgumentOutOfRangeException(nameof(mimeType),
                 $"Unsupported or unknown MIME type '{mimeType}'. Supported: text/markdown, text/html, text/plain, application/xml, text/xml, message/rfc822, application/x-mimearchive, multipart/related, application/vnd.openxmlformats-officedocument.wordprocessingml.document.")
        };

        return (partType, data ?? Array.Empty<byte>());
    }

    private static string GetEffectiveMimeType(string? declaredMime, string url)
    {
        if (!string.IsNullOrWhiteSpace(declaredMime))
            return declaredMime;

        // Fallback by extension if server doesn't send a type
        var ext = GetExtension(url).ToLowerInvariant();
        return ext switch
        {
            ".md" => "text/markdown",
            ".markdown" => "text/markdown",
            ".htm" or ".html" => "text/html",
            ".xhtml" => "application/xhtml+xml",
            ".txt" => "text/plain",
            ".xml" => "application/xml",
            ".mht" or ".mhtml" => "multipart/related",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            _ => "text/plain" // conservative default
        };
    }

    private static string GetExtension(string url)
    {
        try
        {
            var u = new Uri(url);
            return Path.GetExtension(u.AbsolutePath);
        }
        catch
        {
            return Path.GetExtension(url);
        }
    }


    [Description("Append text, markdown, or HTML content to an existing Word document (.docx)")]
    [McpServerTool(Name = "openxml_word_append_content", ReadOnly = false, OpenWorld = false, Idempotent = false, Destructive = true)]
    public static async Task<CallToolResult?> OpenXMLWord_AppendContent(
        [Description("Target Word document URL (.docx)")] string targetUrl,
        [Description("Input MIME type: text/plain | text/markdown | text/html (aliases: text | markdown | html)")] string contentType,
        [Description("Content to append")] string content,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async (graphClient) =>
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var normalized = NormalizeContentType(contentType);

        // 1️⃣ Download existing Word document
        var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, targetUrl, cancellationToken);
        var file = files.FirstOrDefault() ?? throw new FileNotFoundException($"No Word document found at {targetUrl}");

        using var docStream = new MemoryStream();
        await file.Contents.ToStream().CopyToAsync(docStream, cancellationToken);
        docStream.Position = 0;

        // 2️⃣ Convert incoming content to HTML if needed
        string html = normalized switch
        {
            "text/plain" or "text" => PlainTextToHtml(content ?? string.Empty),
            "text/markdown" or "markdown" => Markdig.Markdown.ToHtml(content ?? string.Empty),
            "text/html" or "html" => content ?? string.Empty,
            _ => throw new ArgumentOutOfRangeException(nameof(contentType),
                $"Unsupported contentType '{contentType}'. Use text/plain, text/markdown or text/html.")
        };
        var wrappedHtml = WrapHtml(html);

        // 3️⃣ Open existing .docx and append via AltChunk
        using (var wordDoc = WordprocessingDocument.Open(docStream, true))
        {
            var main = wordDoc.MainDocumentPart ?? throw new InvalidOperationException("Missing MainDocumentPart");
            main.Document ??= new Document(new Body());
            main.Document.Body ??= new Body();

            var chunkId = "chunk_" + Guid.NewGuid().ToString("N");
            var chunk = main.AddAlternativeFormatImportPart(AlternativeFormatImportPartType.Html, chunkId);

            using (var cs = chunk.GetStream(FileMode.Create, FileAccess.Write))
            using (var writer = new StreamWriter(cs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(wrappedHtml);
            }

            main.Document.Body.AppendChild(new AltChunk { Id = chunkId });
            main.Document.Save();
        }

        // 4️⃣ Upload back to same file (in-place update)
        docStream.Flush();
        docStream.Position = 0;

        var updated = await graphClient.UploadBinaryDataAsync(targetUrl, new BinaryData(docStream.ToArray()), cancellationToken);
        return updated?.ToResourceLinkBlock(updated?.Name!).ToCallToolResult();
    }));

    [Description("Create a new Word document (.docx) from a .dotx template URL with text/markdown/HTML content")]
    [McpServerTool(Name = "openxml_word_create_from_template", ReadOnly = false, OpenWorld = false, Idempotent = false, Destructive = false)]
    public static async Task<CallToolResult?> OpenXMLWord_CreateFromTemplate(
           [Description("Filename without .docx extension")] string fileName,
           [Description("Template URL (.dotx or .docx)")] string templateUrl,
           [Description("Input MIME type: text/plain | text/markdown | text/html (aliases: text | markdown | html)")] string contentType,
           [Description("Document content matching the MIME type")] string content,
           IServiceProvider serviceProvider,
           RequestContext<CallToolRequestParams> requestContext,
           CancellationToken cancellationToken = default)
           => await requestContext.WithExceptionCheck(async () =>
               await requestContext.WithOboGraphClient(async (graphClient) =>
           {
               var downloadService = serviceProvider.GetRequiredService<DownloadService>();

               var safeName = SanitizeFileName(fileName);
               var normalized = NormalizeContentType(contentType);

               // 1) Download template (.dotx or .docx)
               var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, templateUrl, cancellationToken);
               var file = files.FirstOrDefault() ?? throw new FileNotFoundException($"No content found at templateUrl: {templateUrl}");

               using var templateMs = new MemoryStream();
               await file.Contents.ToStream().CopyToAsync(templateMs, cancellationToken);
               templateMs.Position = 0;

               // 2) Convert content to HTML if needed
               string html = normalized switch
               {
                   "text/plain" or "text" => PlainTextToHtml(content ?? string.Empty),
                   "text/markdown" or "markdown" => Markdown.ToHtml(content ?? string.Empty),
                   "text/html" or "html" => content ?? string.Empty,
                   _ => throw new ArgumentOutOfRangeException(nameof(contentType),
                       $"Unsupported contentType '{contentType}'. Use text/plain, text/markdown or text/html.")
               };

               var wrappedHtml = WrapHtml(html);

               // 3) Open template, convert to Document, inject content via AltChunk
               using (var wordDoc = WordprocessingDocument.Open(templateMs, true))
               {
                   // If it is a .dotx, convert to a .docx
                   if (wordDoc.DocumentType != WordprocessingDocumentType.Document)
                       wordDoc.ChangeDocumentType(WordprocessingDocumentType.Document);

                   var main = wordDoc.MainDocumentPart ?? wordDoc.AddMainDocumentPart();
                   main.Document ??= new Document(new Body());
                   main.Document.Body ??= new Body();

                   var chunk = main.AddAlternativeFormatImportPart(AlternativeFormatImportPartType.Html);
                   using (var cs = chunk.GetStream(FileMode.Create, FileAccess.Write))
                   using (var writer = new StreamWriter(cs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
                   {
                       writer.Write(wrappedHtml);
                   }

                   main.Document.Body.AppendChild(new AltChunk { Id = main.GetIdOfPart(chunk) });
                   main.Document.Save();
               }

               // 4) Finalize + upload
               templateMs.Flush();
               templateMs.Position = 0;

               var uploaded = await graphClient.Upload(
                   $"{safeName}.docx",
                   await BinaryData.FromStreamAsync(templateMs, cancellationToken),
                   cancellationToken);

               return uploaded?.ToCallToolResult();
           }));

    // ---- Small helper (reuse your existing helpers NormalizeContentType, WrapHtml, SanitizeFileName) ----
    private static string PlainTextToHtml(string text)
    {
        // Encode + convert double newlines to paragraphs, single newlines to <br>
        var encoded = WebUtility.HtmlEncode(text ?? string.Empty).Replace("\r\n", "\n");
        var paras = encoded.Split(new[] { "\n\n" }, StringSplitOptions.None)
                           .Select(p => p.Replace("\n", "<br/>"));
        return "<p>" + string.Join("</p><p>", paras) + "</p>";
    }

    [Description("Create a new Word document (.docx) from text, markdown, or HTML")]
    [McpServerTool(Name = "openxml_word_create_from_text", ReadOnly = false, OpenWorld = false, Idempotent = false, Destructive = false)]
    public static async Task<CallToolResult?> OpenXMLWord_CreateFromText(
        [Description("Filename without .docx extension")] string fileName,
        [Description("Input MIME type: text/plain | text/markdown | text/html (aliases: text | markdown | html)")] string contentType,
        [Description("Document content matching the MIME type")] string content,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
         await requestContext.WithOboGraphClient(async (graphClient) =>
        {

            var safeName = SanitizeFileName(fileName);
            var normalized = NormalizeContentType(contentType);

            using var ms = new MemoryStream();

            switch (normalized)
            {
                case "text/plain":
                case "text":
                    CreateDocWithPlainText(ms, content);
                    break;

                case "text/markdown":
                case "markdown":
                    {
                        var html = Markdown.ToHtml(content ?? string.Empty);
                        CreateDocWithHtml(ms, html);
                        break;
                    }

                case "text/html":
                case "html":
                    CreateDocWithHtml(ms, content ?? string.Empty);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(contentType),
                        $"Unsupported contentType '{contentType}'. Use text/plain, text/markdown or text/html.");
            }

            ms.Flush();
            ms.Position = 0;

            var uploaded = await graphClient.Upload(
                $"{safeName}.docx",
                await BinaryData.FromStreamAsync(ms, cancellationToken),
                cancellationToken);

            return uploaded?.ToCallToolResult();
        }));

    // ---------- Helpers ----------

    private static void CreateDocWithPlainText(Stream stream, string text)
    {
        using (var word = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var main = word.AddMainDocumentPart();
            main.Document = new Document(new Body());

            var body = main.Document.Body!;

            // Normalize line endings and split paragraphs on blank lines
            var normalized = (text ?? string.Empty).Replace("\r\n", "\n");
            var paragraphs = normalized.Split(new[] { "\n\n" }, StringSplitOptions.None);

            foreach (var paraText in paragraphs)
            {
                var p = new Paragraph();
                var lines = paraText.Split('\n');

                for (int i = 0; i < lines.Length; i++)
                {
                    // Preserve spaces; add <w:br/> for single newlines
                    p.AppendChild(new Run(new Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve }));
                    if (i < lines.Length - 1)
                        p.AppendChild(new Run(new Break()));
                }

                body.AppendChild(p);
            }

            main.Document.Save();
        }
        stream.Flush();
        stream.Position = 0;
    }

    private static void CreateDocWithHtml(Stream stream, string html)
    {
        using (var word = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var main = word.AddMainDocumentPart();
            main.Document = new Document(new Body());

            var wrapped = WrapHtml(html);

            // Use AltChunk to import HTML (desktop Word will materialize on open)
            var chunk = main.AddAlternativeFormatImportPart(AlternativeFormatImportPartType.Html);
            using (var chunkStream = chunk.GetStream(FileMode.Create, FileAccess.Write))
            using (var writer = new StreamWriter(chunkStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(wrapped);
            }

            main.Document.Body!.AppendChild(new AltChunk { Id = main.GetIdOfPart(chunk) });
            main.Document.Save();
        }
        stream.Flush();
        stream.Position = 0;
    }

    private static string WrapHtml(string html)
    {
        var h = html ?? string.Empty;
        // If caller already passed a full document, keep it
        if (h.Contains("<html", StringComparison.OrdinalIgnoreCase)) return h;

        return $@"<!DOCTYPE html>
            <html>
            <head>
                <meta charset=""UTF-8"">
                <meta http-equiv=""X-UA-Compatible"" content=""IE=edge"">
                <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
                <title>Document</title>
            </head>
            <body>
                {h}
            </body>
            </html>";
    }

    private static string NormalizeContentType(string? ct)
    {
        var s = (ct ?? string.Empty).Trim().ToLowerInvariant();
        return s switch
        {
            "text" => "text/plain",
            "markdown" => "text/markdown",
            "html" => "text/html",
            "text/plain" or "text/markdown" or "text/html" => s,
            _ => s
        };
    }

    private static string SanitizeFileName(string? name)
    {
        var n = string.IsNullOrWhiteSpace(name) ? "document" : name.Trim();
        foreach (var ch in Path.GetInvalidFileNameChars())
            n = n.Replace(ch, '_');
        return n.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) ? n[..^5] : n;
    }


}
