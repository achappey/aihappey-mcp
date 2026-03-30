using System.ComponentModel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Validation;
using Markdig;
using System.Text;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MCPhappey.Core.Services;
using MCPhappey.Tools.Extensions;
using System.ComponentModel.DataAnnotations;
using W15 = DocumentFormat.OpenXml.Office2013.Word;
using W16Cid = DocumentFormat.OpenXml.Office2019.Word.Cid;
using W16CEx = DocumentFormat.OpenXml.Office2021.Word.CommentsExt;

namespace MCPhappey.Tools.OpenXML.Word;

public static class WordPlugin
{
  /*  [Description("Add a Word comment to the first matching text occurrence in a Word document (.docx)")]
    [McpServerTool(Name = "openxml_word_add_comment_to_text", ReadOnly = false, OpenWorld = false, Idempotent = false, Destructive = true)]
    public static async Task<CallToolResult?> OpenXMLWord_AddCommentToText(
    [Description("Target Word document URL (.docx)")] string documentUrl,
    [Description("Text to search for and annotate with a Word comment (case-insensitive)"), MinLength(1)] string targetText,
    [Description("Comment text to place in the Word sidebar")] string commentText,
    [Description("Optional comment author shown in Word")] string? author,
    [Description("Optional comment initials shown in Word")] string? initials,
    [Description("Optional 1-based occurrence index among supported editable matches. If omitted and multiple matches exist, the tool returns an error.")][Range(1, int.MaxValue)] int? occurrence,
    IServiceProvider serviceProvider,
    RequestContext<CallToolRequestParams> requestContext,
    CancellationToken cancellationToken = default)
    => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async (graphClient) =>
        {
            if (string.IsNullOrWhiteSpace(targetText))
                throw new ArgumentException("Target text cannot be empty.", nameof(targetText));

            if (string.IsNullOrWhiteSpace(commentText))
                throw new ArgumentException("Comment text cannot be empty.", nameof(commentText));

            var downloadService = serviceProvider.GetRequiredService<DownloadService>();

            var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, documentUrl, cancellationToken);
            var file = files.FirstOrDefault() ?? throw new FileNotFoundException($"No document found at {documentUrl}");

            using var input = new MemoryStream();
            await file.Contents.ToStream().CopyToAsync(input, cancellationToken);
            input.Position = 0;

            using (var wordDoc = WordprocessingDocument.Open(input, true))
            {
                var mainPart = wordDoc.MainDocumentPart ?? throw new InvalidOperationException("Missing MainDocumentPart");
                NormalizeRevisionIds(mainPart.Document);
                NormalizeDocumentRunProperties(mainPart.Document);
                var paragraphs = mainPart.Document?.Body?.Descendants<Paragraph>() ?? Enumerable.Empty<Paragraph>();
                var analysis = AnalyzeWholeRunTextMatches(paragraphs, targetText);

                if (!TrySelectSupportedMatch(analysis, targetText, occurrence, out var match, out var diagnostic))
                    return diagnostic.ToErrorCallToolResponse();

                var commentsPart = EnsureCommentsPart(mainPart);
                var commentId = GetNextCommentId(commentsPart);
                var effectiveAuthor = string.IsNullOrWhiteSpace(author)
                    ? requestContext.Server.ServerOptions.ServerInfo?.Title ?? "MCPhappey"
                    : author;
                var effectiveInitials = string.IsNullOrWhiteSpace(initials)
                    ? BuildInitials(effectiveAuthor)
                    : initials;

                EnsureCommentCompatibilityNamespaces(mainPart, commentsPart);
                EnsureParagraphCompatibilityIds(match!.Paragraph);
                var commentMetadata = AppendComment(mainPart, commentsPart, commentId, commentText, effectiveAuthor, effectiveInitials);
                ApplyCommentToMatch(match!, commentId);

                if (!HasExpectedCommentMarkup(match!.Paragraph, match.Run, commentsPart, commentId, commentText))
                    return $"Comment verification failed for '{targetText}'; no document uploaded.".ToErrorCallToolResponse();

                if (!HasExpectedModernCommentMetadata(mainPart, effectiveAuthor, commentMetadata))
                    return $"Modern comment metadata verification failed for '{targetText}'; no document uploaded.".ToErrorCallToolResponse();

                var validationErrors = ValidateOpenXmlPackage(wordDoc, maxErrors: 5);
                if (validationErrors.Count > 0)
                    return $"Comment insertion produced invalid OpenXML and was not uploaded: {string.Join(" | ", validationErrors)}".ToErrorCallToolResponse();

                commentsPart.Comments!.Save();
                SaveModernCommentMetadata(mainPart);
                mainPart.Document?.Save();
            }

            input.Flush();
            input.Position = 0;

            var updated = await graphClient.UploadBinaryDataAsync(documentUrl,
                new BinaryData(input.ToArray()), cancellationToken) ?? throw new FileNotFoundException("No content found");

            return updated.ToResourceLinkBlock(updated?.Name!).ToCallToolResult();
        }));*/

    [Description("Highlight the first matching text occurrence in a Word document (.docx)")]
    [McpServerTool(Name = "openxml_word_highlight_text", ReadOnly = false, OpenWorld = false, Idempotent = false, Destructive = true)]
    public static async Task<CallToolResult?> OpenXMLWord_HighlightText(
    [Description("Target Word document URL (.docx)")] string documentUrl,
    [Description("Text to search for and highlight (case-insensitive)"), MinLength(1)] string targetText,
    [Description("Optional Word highlight color. Defaults to yellow.")] string? color,
    [Description("Optional 1-based occurrence index among supported editable matches. If omitted and multiple matches exist, the tool returns an error.")][Range(1, int.MaxValue)] int? occurrence,
    IServiceProvider serviceProvider,
    RequestContext<CallToolRequestParams> requestContext,
    CancellationToken cancellationToken = default)
    => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async (graphClient) =>
        {
            if (string.IsNullOrWhiteSpace(targetText))
                throw new ArgumentException("Target text cannot be empty.", nameof(targetText));

            var downloadService = serviceProvider.GetRequiredService<DownloadService>();

            var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, documentUrl, cancellationToken);
            var file = files.FirstOrDefault() ?? throw new FileNotFoundException($"No document found at {documentUrl}");

            using var input = new MemoryStream();
            await file.Contents.ToStream().CopyToAsync(input, cancellationToken);
            input.Position = 0;

            using (var wordDoc = WordprocessingDocument.Open(input, true))
            {
                var mainPart = wordDoc.MainDocumentPart ?? throw new InvalidOperationException("Missing MainDocumentPart");
                NormalizeRevisionIds(mainPart.Document);
                NormalizeDocumentRunProperties(mainPart.Document);
                var paragraphs = mainPart.Document?.Body?.Descendants<Paragraph>() ?? Enumerable.Empty<Paragraph>();
                var highlightColor = NormalizeHighlightColor(color);
                var analysis = AnalyzeSingleRunTextMatches(paragraphs, targetText);

                if (!TrySelectSupportedMatch(analysis, targetText, occurrence, out var match, out var diagnostic))
                    return diagnostic.ToErrorCallToolResponse();

                var highlightedRun = ApplyHighlightToMatch(match!, highlightColor);

                if (!HasExpectedHighlight(highlightedRun, highlightColor, match!.TargetText))
                    return $"Highlight verification failed for '{targetText}'; no document uploaded.".ToErrorCallToolResponse();

                mainPart.Document?.Save();
            }

            input.Flush();
            input.Position = 0;

            var updated = await graphClient.UploadBinaryDataAsync(documentUrl,
                new BinaryData(input.ToArray()), cancellationToken) ?? throw new FileNotFoundException("No content found");

            return updated.ToResourceLinkBlock(updated?.Name!).ToCallToolResult();
        }));

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
                NormalizeRevisionIds(mainPart.Document);

                // Ensure track revisions is enabled
                var settingsPart = mainPart.DocumentSettingsPart ?? mainPart.AddNewPart<DocumentSettingsPart>();
                settingsPart.Settings ??= new Settings();

                if (!settingsPart.Settings.Elements<TrackRevisions>().Any())
                    settingsPart.Settings.AppendChild(new TrackRevisions());

                // Search each paragraph
                var paragraphs = mainPart.Document?.Body?.Descendants<Paragraph>() ?? Enumerable.Empty<Paragraph>();

                var nextRevisionId = GetNextRevisionId(mainPart.Document);

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
                        Id = (nextRevisionId++).ToString()
                    };
                    deletedRun.AppendChild(new Run(new Text(originalText) { Space = SpaceProcessingModeValues.Preserve }));
                    paragraph.AppendChild(deletedRun);

                    // Inserted run (replacement)
                    var insertedRun = new InsertedRun()
                    {
                        Author = requestContext.Server.ServerOptions.ServerInfo?.Title,
                        Date = DateTime.Now,
                        Id = (nextRevisionId++).ToString()
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

                mainPart.Document?.Save();
            }

            // 3️⃣ Upload the updated doc back
            input.Flush();
            input.Position = 0;

            var updated = await graphClient.UploadBinaryDataAsync(documentUrl,
                new BinaryData(input.ToArray()), cancellationToken) ?? throw new FileNotFoundException($"No content found");

            return updated.ToResourceLinkBlock(updated?.Name!).ToCallToolResult();
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
            cancellationToken) ?? throw new FileNotFoundException($"No content found");

        return uploaded.ToCallToolResult();
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
            cancellationToken) ?? throw new FileNotFoundException($"No content found");

        return uploaded.ToCallToolResult();
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

        var updated = await graphClient.UploadBinaryDataAsync(targetUrl,
            new BinaryData(docStream.ToArray()), cancellationToken) ?? throw new FileNotFoundException($"No content found");
        return updated.ToResourceLinkBlock(updated?.Name!).ToCallToolResult();
    }));


    private static (PartTypeInfo partType, byte[] payload) PrepareImportPayload(string mimeType, byte[] data)
    {
        // Normalize markdown → HTML; choose proper AltChunk type
        var mt = (mimeType ?? string.Empty).Trim().ToLowerInvariant();

        if (mt is "text/markdown" or "markdown")
        {
            var md = Encoding.UTF8.GetString(data ?? Array.Empty<byte>());
            var html = Markdown.ToHtml(md ?? string.Empty);
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

    private sealed record SingleRunTextMatch(
        Paragraph Paragraph,
        Run Run,
        Text Text,
        int MatchIndex,
        string TargetText,
        int ParagraphIndex);

    private sealed record CommentMetadata(
        string CommentId,
        string CommentParagraphId,
        string DurableId);

    private sealed record SingleRunMatchAnalysis(
        List<SingleRunTextMatch> SupportedMatches,
        List<int> UnsupportedParagraphs,
        List<int> SplitAcrossRunsParagraphs);

    private static SingleRunMatchAnalysis AnalyzeWholeRunTextMatches(IEnumerable<Paragraph> paragraphs, string targetText)
    {
        var supportedMatches = new List<SingleRunTextMatch>();
        var unsupportedParagraphs = new List<int>();
        var splitAcrossRunsParagraphs = new List<int>();
        var paragraphIndex = 0;

        foreach (var paragraph in paragraphs)
        {
            paragraphIndex++;

            if (ParagraphHasUnsupportedStructure(paragraph))
            {
                if (ParagraphContainsTargetText(paragraph, targetText))
                    unsupportedParagraphs.Add(paragraphIndex);

                continue;
            }

            var paragraphMatches = 0;

            foreach (var run in paragraph.Elements<Run>())
            {
                if (!TryGetSinglePlainTextNode(run, out var textNode))
                    continue;

                var runText = textNode.Text ?? string.Empty;
                if (!string.Equals(runText, targetText, StringComparison.OrdinalIgnoreCase))
                    continue;

                supportedMatches.Add(new SingleRunTextMatch(paragraph, run, textNode, 0, runText, paragraphIndex));
                paragraphMatches++;
            }

            if (paragraphMatches == 0 && ParagraphContainsTargetText(paragraph, targetText))
                splitAcrossRunsParagraphs.Add(paragraphIndex);
        }

        return new SingleRunMatchAnalysis(supportedMatches, unsupportedParagraphs, splitAcrossRunsParagraphs);
    }

    private static SingleRunMatchAnalysis AnalyzeSingleRunTextMatches(IEnumerable<Paragraph> paragraphs, string targetText)
    {
        var supportedMatches = new List<SingleRunTextMatch>();
        var unsupportedParagraphs = new List<int>();
        var splitAcrossRunsParagraphs = new List<int>();
        var paragraphIndex = 0;

        foreach (var paragraph in paragraphs)
        {
            paragraphIndex++;

            if (ParagraphHasUnsupportedStructure(paragraph))
            {
                if (ParagraphContainsTargetText(paragraph, targetText))
                    unsupportedParagraphs.Add(paragraphIndex);

                continue;
            }

            foreach (var run in paragraph.Elements<Run>())
            {
                if (!TryGetSinglePlainTextNode(run, out var textNode))
                    continue;

                var runText = textNode.Text ?? string.Empty;
                foreach (var matchIndex in EnumerateMatchIndexes(runText, targetText))
                    supportedMatches.Add(new SingleRunTextMatch(paragraph, run, textNode, matchIndex, targetText, paragraphIndex));
            }

            if (!supportedMatches.Any(m => ReferenceEquals(m.Paragraph, paragraph)) && ParagraphContainsTargetText(paragraph, targetText))
                splitAcrossRunsParagraphs.Add(paragraphIndex);
        }

        return new SingleRunMatchAnalysis(supportedMatches, unsupportedParagraphs, splitAcrossRunsParagraphs);
    }

    private static bool TrySelectSupportedMatch(SingleRunMatchAnalysis analysis, string targetText, int? occurrence, out SingleRunTextMatch? match, out string diagnostic)
    {
        match = null;
        diagnostic = string.Empty;

        if (analysis.SupportedMatches.Count == 0)
        {
            if (analysis.UnsupportedParagraphs.Count > 0)
            {
                diagnostic = $"Found '{targetText}' only in unsupported/complex Word paragraph structures (paragraphs: {string.Join(", ", analysis.UnsupportedParagraphs)}); no change applied.";
                return false;
            }

            if (analysis.SplitAcrossRunsParagraphs.Count > 0)
            {
                diagnostic = $"Found '{targetText}' in supported paragraphs, but the match spans multiple runs instead of a single text run (paragraphs: {string.Join(", ", analysis.SplitAcrossRunsParagraphs)}); no change applied.";
                return false;
            }

            diagnostic = $"Could not find '{targetText}' in a supported single text run; no change applied.";
            return false;
        }

        if (!occurrence.HasValue)
        {
            if (analysis.SupportedMatches.Count > 1)
            {
                diagnostic = $"Found {analysis.SupportedMatches.Count} supported matches for '{targetText}'. Specify the optional occurrence parameter to choose which one to highlight.";
                return false;
            }

            if (analysis.UnsupportedParagraphs.Count > 0 || analysis.SplitAcrossRunsParagraphs.Count > 0)
            {
                diagnostic = $"Found one supported editable match for '{targetText}', but there are additional ambiguous occurrences in complex or split-run paragraphs. Specify occurrence=1 to highlight the supported match explicitly.";
                return false;
            }

            match = analysis.SupportedMatches[0];
            diagnostic = $"Resolved a single supported match for '{targetText}'.";
            return true;
        }

        if (occurrence.Value > analysis.SupportedMatches.Count)
        {
            diagnostic = $"Requested occurrence {occurrence.Value} for '{targetText}', but only {analysis.SupportedMatches.Count} supported editable matches were found.";
            return false;
        }

        match = analysis.SupportedMatches[occurrence.Value - 1];
        diagnostic = $"Resolved occurrence {occurrence.Value} for '{targetText}'.";
        return true;
    }

    private static IEnumerable<int> EnumerateMatchIndexes(string source, string target)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target))
            yield break;

        var searchIndex = 0;
        while (searchIndex <= source.Length - target.Length)
        {
            var matchIndex = source.IndexOf(target, searchIndex, StringComparison.OrdinalIgnoreCase);
            if (matchIndex < 0)
                yield break;

            yield return matchIndex;
            searchIndex = matchIndex + target.Length;
        }
    }

    private static bool ParagraphHasUnsupportedStructure(Paragraph paragraph)
    {
        foreach (var child in paragraph.ChildElements)
        {
            if (child is ParagraphProperties)
                continue;

            if (child is not Run)
                return true;
        }

        foreach (var run in paragraph.Elements<Run>())
        {
            if (!TryGetSinglePlainTextNode(run, out _))
                return true;
        }

        return false;
    }

    private static bool TryGetSinglePlainTextNode(Run run, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Text? textNode)
    {
        textNode = null;

        foreach (var child in run.ChildElements)
        {
            if (child is RunProperties)
                continue;

            if (child is not Text)
                return false;
        }

        var textNodes = run.Elements<Text>().ToList();
        if (textNodes.Count != 1)
            return false;

        textNode = textNodes[0];
        return true;
    }

    private static bool ParagraphContainsTargetText(Paragraph paragraph, string targetText)
        => !string.IsNullOrWhiteSpace(paragraph.InnerText)
           && paragraph.InnerText.IndexOf(targetText, StringComparison.OrdinalIgnoreCase) >= 0;

    private static void ApplyCommentToMatch(SingleRunTextMatch match, string commentId)
    {
        match.Run.InsertBeforeSelf(new CommentRangeStart() { Id = commentId });
        var endMarker = match.Run.InsertAfterSelf(new CommentRangeEnd() { Id = commentId });
        endMarker.InsertAfterSelf(CreateCommentReferenceRun(commentId));
    }

    private static Run ApplyHighlightToMatch(SingleRunTextMatch match, HighlightColorValues highlightColor)
    {
        var originalText = match.Text.Text ?? string.Empty;
        var before = originalText[..match.MatchIndex];
        var matched = originalText.Substring(match.MatchIndex, match.TargetText.Length);
        var after = originalText[(match.MatchIndex + match.TargetText.Length)..];

        var replacements = new List<OpenXmlElement>();

        if (before.Length > 0)
            replacements.Add(CreateRunFromTemplate(match.Run, before));

        var highlightedRun = CreateRunFromTemplate(match.Run, matched, props => SetHighlight(props, highlightColor));
        replacements.Add(highlightedRun);

        if (after.Length > 0)
            replacements.Add(CreateRunFromTemplate(match.Run, after));

        ReplaceElementWithMany(match.Run, replacements);
        return highlightedRun;
    }

    private static Run CreateRunFromTemplate(Run templateRun, string text, Action<RunProperties>? mutateProperties = null)
    {
        var run = new Run();
        var runProperties = templateRun.RunProperties?.CloneNode(true) as RunProperties;

        if (runProperties is null && mutateProperties is not null)
            runProperties = new RunProperties();

        if (runProperties is not null)
        {
            mutateProperties?.Invoke(runProperties);
            if (runProperties.ChildElements.Count > 0)
                run.AppendChild(runProperties);
        }

        run.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        return run;
    }

    private static Run CreateCommentReferenceRun(string commentId)
        => new(
            new CommentReference() { Id = commentId });

    private static void ReplaceElementWithMany(OpenXmlElement original, IEnumerable<OpenXmlElement> replacements)
    {
        foreach (var replacement in replacements)
            original.InsertBeforeSelf(replacement);

        original.Remove();
    }

    private static void SetHighlight(RunProperties runProperties, HighlightColorValues highlightColor)
    {
        runProperties.RemoveAllChildren<Highlight>();
        InsertHighlightInCanonicalPosition(runProperties, new Highlight() { Val = highlightColor });
    }

    private static bool HasExpectedHighlight(Run run, HighlightColorValues expectedColor, string expectedText)
    {
        var highlight = run.RunProperties?.GetFirstChild<Highlight>();
        var text = string.Concat(run.Elements<Text>().Select(t => t.Text));

        return string.Equals(text, expectedText, StringComparison.Ordinal)
            && highlight?.Val?.Value == expectedColor;
    }

    private static bool HasExpectedCommentMarkup(Paragraph paragraph, Run targetRun, WordprocessingCommentsPart commentsPart, string commentId, string expectedCommentText)
    {
        var previousElement = targetRun.PreviousSibling<OpenXmlElement>();
        var nextElement = targetRun.NextSibling<OpenXmlElement>();
        var nextAfterEnd = nextElement?.NextSibling<OpenXmlElement>();

        var hasRangeStart = previousElement is CommentRangeStart start && start.Id?.Value == commentId;
        var hasRangeEnd = nextElement is CommentRangeEnd end && end.Id?.Value == commentId;
        var hasReference = nextAfterEnd is Run referenceRun
            && referenceRun.GetFirstChild<CommentReference>()?.Id?.Value == commentId;
        var hasComment = commentsPart.Comments?
            .Elements<Comment>()
            .Any(c => c.Id?.Value == commentId
                && string.Equals(c.InnerText, expectedCommentText, StringComparison.Ordinal)) == true;

        return hasRangeStart && hasRangeEnd && hasReference && hasComment;
    }

    private static WordprocessingCommentsPart EnsureCommentsPart(MainDocumentPart mainPart)
    {
        var commentsPart = mainPart.WordprocessingCommentsPart ?? mainPart.AddNewPart<WordprocessingCommentsPart>();
        commentsPart.Comments ??= new Comments();
        return commentsPart;
    }

    private static string GetNextCommentId(WordprocessingCommentsPart commentsPart)
    {
        var nextId = commentsPart.Comments?
            .Elements<Comment>()
            .Select(c => int.TryParse(c.Id?.Value, out var id) ? id : 0)
            .DefaultIfEmpty(0)
            .Max() + 1 ?? 1;

        return nextId.ToString();
    }

    private static CommentMetadata AppendComment(MainDocumentPart mainPart, WordprocessingCommentsPart commentsPart, string commentId, string commentText, string author, string initials)
    {
        commentsPart.Comments ??= new Comments();

        var commentParagraphId = GenerateHexId(8);
        var commentTextId = GenerateHexId(8);
        var durableId = GenerateHexId(16);

        var commentParagraph = new Paragraph(
            new ParagraphProperties(
                new ParagraphStyleId() { Val = "CommentText" }),
            new Run(
                new RunProperties(
                    new RunStyle() { Val = "CommentReference" }),
                new AnnotationReferenceMark()),
            new Run(
                new Text(commentText) { Space = SpaceProcessingModeValues.Preserve }))
        {
            ParagraphId = commentParagraphId,
            TextId = commentTextId
        };

        var comment = new Comment()
        {
            Id = commentId,
            Author = author,
            Initials = initials,
            Date = DateTime.Now
        };

        comment.AppendChild(commentParagraph);

        commentsPart.Comments.AppendChild(comment);
        EnsureModernCommentMetadata(mainPart, author, commentParagraphId, durableId);
        return new CommentMetadata(commentId, commentParagraphId, durableId);
    }

    private static void EnsureCommentCompatibilityNamespaces(MainDocumentPart mainPart, WordprocessingCommentsPart commentsPart)
    {
        if (mainPart.Document is not null)
        {
            EnsureNamespaceDeclaration(mainPart.Document, "mc", "http://schemas.openxmlformats.org/markup-compatibility/2006");
            EnsureNamespaceDeclaration(mainPart.Document, "w14", "http://schemas.microsoft.com/office/word/2010/wordml");
            EnsureNamespaceDeclaration(mainPart.Document, "w15", "http://schemas.microsoft.com/office/word/2012/wordml");
            EnsureNamespaceDeclaration(mainPart.Document, "w16cid", "http://schemas.microsoft.com/office/word/2016/wordml/cid");
            EnsureNamespaceDeclaration(mainPart.Document, "w16cex", "http://schemas.microsoft.com/office/word/2018/wordml/cex");
            EnsureIgnorablePrefixes(mainPart.Document, "w14", "w15", "w16cid", "w16cex");
        }

        if (commentsPart.Comments is not null)
        {
            EnsureNamespaceDeclaration(commentsPart.Comments, "mc", "http://schemas.openxmlformats.org/markup-compatibility/2006");
            EnsureNamespaceDeclaration(commentsPart.Comments, "w14", "http://schemas.microsoft.com/office/word/2010/wordml");
            EnsureIgnorablePrefixes(commentsPart.Comments, "w14");
        }
    }

    private static void EnsureParagraphCompatibilityIds(Paragraph paragraph)
    {
        if (string.IsNullOrWhiteSpace(paragraph.ParagraphId?.Value))
            paragraph.ParagraphId = GenerateHexId(8);

        if (string.IsNullOrWhiteSpace(paragraph.TextId?.Value))
            paragraph.TextId = GenerateHexId(8);
    }

    private static void EnsureModernCommentMetadata(MainDocumentPart mainPart, string author, string commentParagraphId, string durableId)
    {
        var peoplePart = mainPart.WordprocessingPeoplePart ?? mainPart.AddNewPart<WordprocessingPeoplePart>();
        peoplePart.People ??= new W15.People();
        EnsureNamespaceDeclaration(peoplePart.People, "w15", "http://schemas.microsoft.com/office/word/2012/wordml");

        if (!peoplePart.People.Elements<W15.Person>()
            .Any(p => string.Equals(p.Author?.Value, author, StringComparison.OrdinalIgnoreCase)))
        {
            peoplePart.People.AppendChild(new W15.Person()
            {
                Author = author
            });
        }

        var commentsExPart = mainPart.WordprocessingCommentsExPart ?? mainPart.AddNewPart<WordprocessingCommentsExPart>();
        commentsExPart.CommentsEx ??= new W15.CommentsEx();
        EnsureNamespaceDeclaration(commentsExPart.CommentsEx, "w15", "http://schemas.microsoft.com/office/word/2012/wordml");

        if (!commentsExPart.CommentsEx.Elements<W15.CommentEx>()
            .Any(c => c.ParaId?.Value == commentParagraphId))
        {
            commentsExPart.CommentsEx.AppendChild(new W15.CommentEx()
            {
                ParaId = commentParagraphId,
                ParaIdParent = "00000000",
                Done = false
            });
        }

        var commentsIdsPart = mainPart.WordprocessingCommentsIdsPart ?? mainPart.AddNewPart<WordprocessingCommentsIdsPart>();
        commentsIdsPart.CommentsIds ??= new W16Cid.CommentsIds();
        EnsureNamespaceDeclaration(commentsIdsPart.CommentsIds, "w16cid", "http://schemas.microsoft.com/office/word/2016/wordml/cid");

        if (!commentsIdsPart.CommentsIds.Elements<W16Cid.CommentId>()
            .Any(c => c.ParaId?.Value == commentParagraphId))
        {
            commentsIdsPart.CommentsIds.AppendChild(new W16Cid.CommentId()
            {
                ParaId = commentParagraphId,
                DurableId = durableId
            });
        }

        var commentsExtensiblePart = mainPart.WordCommentsExtensiblePart ?? mainPart.AddNewPart<WordCommentsExtensiblePart>();
        commentsExtensiblePart.CommentsExtensible ??= new W16CEx.CommentsExtensible();
        EnsureNamespaceDeclaration(commentsExtensiblePart.CommentsExtensible, "w16cex", "http://schemas.microsoft.com/office/word/2018/wordml/cex");

        if (!commentsExtensiblePart.CommentsExtensible.Elements<W16CEx.CommentExtensible>()
            .Any(c => c.DurableId?.Value == durableId))
        {
            commentsExtensiblePart.CommentsExtensible.AppendChild(new W16CEx.CommentExtensible()
            {
                DurableId = durableId,
                DateUtc = DateTime.UtcNow,
                IntelligentPlaceholder = false
            });
        }
    }

    private static bool HasExpectedModernCommentMetadata(MainDocumentPart mainPart, string author, CommentMetadata metadata)
    {
        var hasPerson = mainPart.WordprocessingPeoplePart?.People?
            .Elements<W15.Person>()
            .Any(p => string.Equals(p.Author?.Value, author, StringComparison.OrdinalIgnoreCase)) == true;

        var hasCommentEx = mainPart.WordprocessingCommentsExPart?.CommentsEx?
            .Elements<W15.CommentEx>()
            .Any(c => c.ParaId?.Value == metadata.CommentParagraphId) == true;

        var hasCommentId = mainPart.WordprocessingCommentsIdsPart?.CommentsIds?
            .Elements<W16Cid.CommentId>()
            .Any(c => c.ParaId?.Value == metadata.CommentParagraphId
                && c.DurableId?.Value == metadata.DurableId) == true;

        var hasCommentExtensible = mainPart.WordCommentsExtensiblePart?.CommentsExtensible?
            .Elements<W16CEx.CommentExtensible>()
            .Any(c => c.DurableId?.Value == metadata.DurableId) == true;

        return hasPerson && hasCommentEx && hasCommentId && hasCommentExtensible;
    }

    private static void SaveModernCommentMetadata(MainDocumentPart mainPart)
    {
        mainPart.WordprocessingPeoplePart?.People?.Save();
        mainPart.WordprocessingCommentsExPart?.CommentsEx?.Save();
        mainPart.WordprocessingCommentsIdsPart?.CommentsIds?.Save();
        mainPart.WordCommentsExtensiblePart?.CommentsExtensible?.Save();
    }

    private static void EnsureNamespaceDeclaration(OpenXmlElement element, string prefix, string uri)
    {
        if (element.NamespaceDeclarations.Any(ns => ns.Key == prefix && ns.Value == uri))
            return;

        element.AddNamespaceDeclaration(prefix, uri);
    }

    private static void EnsureIgnorablePrefixes(OpenXmlElement element, params string[] prefixes)
    {
        var existing = (element.MCAttributes?.Ignorable?.Value ?? string.Empty)
            .Split([' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var prefix in prefixes.Where(p => !string.IsNullOrWhiteSpace(p)))
            existing.Add(prefix);

        var mcAttributes = element.MCAttributes ?? new MarkupCompatibilityAttributes();
        mcAttributes.Ignorable = string.Join(' ', existing.OrderBy(p => p, StringComparer.Ordinal));
        element.MCAttributes = mcAttributes;
    }

    private static string GenerateHexId(int length)
    {
        var hex = Convert.ToHexString(Guid.NewGuid().ToByteArray());
        return length >= hex.Length
            ? hex
            : hex[..length];
    }

    private static List<string> ValidateOpenXmlPackage(WordprocessingDocument wordDoc, int maxErrors)
    {
        var validator = new OpenXmlValidator();

        return validator.Validate(wordDoc)
            .Take(Math.Max(1, maxErrors))
            .Select(e => $"{e.Path?.XPath}:{e.Description}")
            .ToList();
    }

    private static void NormalizeRevisionIds(Document? document)
    {
        if (document is null)
            return;

        var nextRevisionId = 1;

        foreach (var revision in document.Descendants<InsertedRun>())
        {
            revision.Id = nextRevisionId.ToString();
            nextRevisionId++;
        }

        foreach (var revision in document.Descendants<DeletedRun>())
        {
            revision.Id = nextRevisionId.ToString();
            nextRevisionId++;
        }
    }

    private static int GetNextRevisionId(Document? document)
    {
        if (document is null)
            return 1;

        var ids = document.Descendants<InsertedRun>()
            .Select(r => r.Id?.Value)
            .Concat(document.Descendants<DeletedRun>().Select(r => r.Id?.Value))
            .Select(static id => int.TryParse(id, out var parsed) ? parsed : 0);

        return ids.DefaultIfEmpty(0).Max() + 1;
    }

    private static void NormalizeDocumentRunProperties(Document? document)
    {
        if (document is null)
            return;

        foreach (var runProperties in document.Descendants<RunProperties>())
            NormalizeRunProperties(runProperties);
    }

    private static void NormalizeRunProperties(RunProperties runProperties)
    {
        var existingHighlight = runProperties.Elements<Highlight>().LastOrDefault();
        if (existingHighlight is null)
            return;

        var normalizedHighlight = new Highlight() { Val = existingHighlight.Val };
        runProperties.RemoveAllChildren<Highlight>();
        InsertHighlightInCanonicalPosition(runProperties, normalizedHighlight);
    }

    private static void InsertHighlightInCanonicalPosition(RunProperties runProperties, Highlight highlight)
    {
        var insertBefore = runProperties.ChildElements.FirstOrDefault(static child => child is
            Underline or
            Border or
            Shading or
            FitText or
            VerticalTextAlignment or
            RightToLeftText or
            ComplexScript or
            Emphasis or
            Languages or
            EastAsianLayout or
            SpecVanish);

        if (insertBefore is not null)
            runProperties.InsertBefore(highlight, insertBefore);
        else
            runProperties.AppendChild(highlight);
    }

    private static string BuildInitials(string? author)
    {
        if (string.IsNullOrWhiteSpace(author))
            return "MC";

        var parts = author
            .Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Take(3)
            .Select(p => char.ToUpperInvariant(p[0]));

        var initials = new string(parts.ToArray());
        return string.IsNullOrWhiteSpace(initials) ? "MC" : initials;
    }

    private static HighlightColorValues NormalizeHighlightColor(string? color)
    {
        var normalized = (color ?? "yellow").Trim().ToLowerInvariant();

        return normalized switch
        {
            "yellow" => HighlightColorValues.Yellow,
            "green" => HighlightColorValues.Green,
            "cyan" => HighlightColorValues.Cyan,
            "magenta" => HighlightColorValues.Magenta,
            "blue" => HighlightColorValues.Blue,
            "red" => HighlightColorValues.Red,
            "darkblue" or "dark-blue" => HighlightColorValues.DarkBlue,
            "darkcyan" or "dark-cyan" => HighlightColorValues.DarkCyan,
            "darkgreen" or "dark-green" => HighlightColorValues.DarkGreen,
            "darkmagenta" or "dark-magenta" => HighlightColorValues.DarkMagenta,
            "darkred" or "dark-red" => HighlightColorValues.DarkRed,
            "darkyellow" or "dark-yellow" => HighlightColorValues.DarkYellow,
            "darkgray" or "dark-gray" or "darkgrey" or "dark-grey" => HighlightColorValues.DarkGray,
            "lightgray" or "light-gray" or "lightgrey" or "light-grey" => HighlightColorValues.LightGray,
            "black" => HighlightColorValues.Black,
            "white" => HighlightColorValues.White,
            _ => throw new ArgumentOutOfRangeException(nameof(color), $"Unsupported highlight color '{color}'.")
        };
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
            "text/markdown" or "markdown" => Markdown.ToHtml(content ?? string.Empty),
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

        var updated = await graphClient.UploadBinaryDataAsync(targetUrl,
            new BinaryData(docStream.ToArray()), cancellationToken) ?? throw new FileNotFoundException($"No content found");
        return updated.ToResourceLinkBlock(updated?.Name!).ToCallToolResult();
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
                   cancellationToken) ?? throw new FileNotFoundException($"No content found");

               return uploaded.ToCallToolResult();
           }));

    // ---- Small helper (reuse your existing helpers NormalizeContentType, WrapHtml, SanitizeFileName) ----
    private static string PlainTextToHtml(string text)
    {
        // Encode + convert double newlines to paragraphs, single newlines to <br>
        var encoded = WebUtility.HtmlEncode(text ?? string.Empty).Replace("\r\n", "\n");
        var paras = encoded.Split(["\n\n"], StringSplitOptions.None)
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
                cancellationToken) ?? throw new FileNotFoundException($"No content found"); ;

            return uploaded.ToCallToolResult();
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
            var paragraphs = normalized.Split(["\n\n"], StringSplitOptions.None);

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
