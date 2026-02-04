using System.ComponentModel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using P = DocumentFormat.OpenXml.Presentation;
using A = DocumentFormat.OpenXml.Drawing;
using DocumentFormat.OpenXml;
using MCPhappey.Common.Extensions;

namespace MCPhappey.Tools.OpenXML.PowerPoint;

public static class PowerPointPlugin
{
    private static SlidePart GetSlidePartByIndex(PresentationPart presPart, int slideIndex)
    {
        var slideIds = presPart.Presentation?.SlideIdList?.Elements<SlideId>().ToList()
            ?? throw new InvalidDataException("No SlideIdList found");
        if (slideIndex < 0 || slideIndex >= slideIds.Count)
            throw new ArgumentOutOfRangeException(nameof(slideIndex), $"Valid range: 0..{slideIds.Count - 1}");
        var relId = slideIds[slideIndex].RelationshipId!;
        return (SlidePart)presPart.GetPartById(relId!);
    }

    private static P.Shape? GetPlaceholderShape(P.Slide slide, PlaceholderValues type)
    {
        var tree = slide.CommonSlideData?.ShapeTree;
        return tree?.Elements<P.Shape>().FirstOrDefault(s =>
        {
            var nv = s.NonVisualShapeProperties;
            var app = nv?.ApplicationNonVisualDrawingProperties;
            var ph = app?.GetFirstChild<P.PlaceholderShape>();
            return ph?.Type?.Value == type;
        });
    }

    private static void SetShapeText(P.Shape shape, IEnumerable<string> lines, bool bullets, bool replace)
    {
        var textBody = shape.TextBody ?? shape.AppendChild(new P.TextBody(
            new A.BodyProperties(), new A.ListStyle()));

        if (replace)
            textBody.RemoveAllChildren<A.Paragraph>();

        foreach (var line in lines)
        {
            var p = new A.Paragraph();

            if (bullets)
            {
                // Zorg dat er ParagraphProperties zijn
                p.ParagraphProperties ??= new A.ParagraphProperties();

                // Alleen bullet toevoegen als er nog geen bullet-definitie is
                if (!p.ParagraphProperties.Elements<A.CharacterBullet>().Any() &&
                    !p.ParagraphProperties.Elements<A.AutoNumberedBullet>().Any())
                {
                    // Alleen het teken aangeven; font/thema blijven uit de template komen
                    p.ParagraphProperties.AppendChild(new A.CharacterBullet { Char = "•" });
                }
            }

            p.AppendChild(new A.Run(new A.Text(line ?? string.Empty)));

            textBody.AppendChild(p);
        }
    }

    private static string NormalizeContentType(string? ct)
    {
        var s = (ct ?? string.Empty).Trim().ToLowerInvariant();
        return s switch
        {
            "text" => "text/plain",
            "markdown" => "text/markdown",
            "text/plain" or "text/markdown" => s,
            _ => "text/plain"
        };
    }

    private static IEnumerable<string> ParseContentLines(string contentType, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return Array.Empty<string>();

        if (contentType == "text/markdown")
        {
            // support simple "- item" or "* item" lines
            return content.Split('\n')
                .Select(l => l.Trim().TrimStart('-', '*', ' '))
                .Where(l => !string.IsNullOrWhiteSpace(l));
        }

        return content.Replace("\r\n", "\n")
                      .Split('\n')
                      .Select(l => l.Trim())
                      .Where(l => !string.IsNullOrWhiteSpace(l));
    }


    [Description("Add or replace text or markdown content on a PowerPoint slide (.pptx)")]
    [McpServerTool(
    Name = "openxml_powerpoint_add_content",
    Title = "Add PowerPoint content",
    ReadOnly = false, OpenWorld = false, Idempotent = false, Destructive = true)]
    public static async Task<CallToolResult?> OpenXMLPowerPoint_AddContent(
    [Description("PowerPoint file URL (.pptx)")] string url,
    [Description("Zero-based slide index")] int slideIndex,
    [Description("Input MIME type: text/plain | text/markdown (aliases: text | markdown)")] string contentType,
    [Description("Content to add or replace")] string content,
    [Description("If true, replace existing text; otherwise append as new paragraph")] bool replace,
    IServiceProvider serviceProvider,
    RequestContext<CallToolRequestParams> requestContext,
    [Description("Optional shape index (omit for auto title/body detection)")] int? shapeIndex = null,
    CancellationToken cancellationToken = default)
    => await requestContext.WithExceptionCheck(async () =>
    await requestContext.WithOboGraphClient(async graphClient =>
{
    var downloadService = serviceProvider.GetRequiredService<DownloadService>();
    var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, url, cancellationToken);
    var file = files.FirstOrDefault() ?? throw new FileNotFoundException($"No PowerPoint found at {url}");

    var buffer = file.Contents.ToArray();
    using var ms = new MemoryStream();
    ms.Write(buffer, 0, buffer.Length);   // copy data
    ms.Position = 0;

    using (var doc = PresentationDocument.Open(ms, true))
    {
        var presPart = doc.PresentationPart ?? throw new InvalidDataException("No PresentationPart found");
        var slidePart = GetSlidePartByIndex(presPart, slideIndex);
        var slide = slidePart.Slide ?? throw new InvalidDataException("No Slide found");

        // Normalize type
        var normalized = NormalizeContentType(contentType);
        var lines = ParseContentLines(normalized, content);

        // Select target shape
        P.Shape? shape;
        if (shapeIndex.HasValue)
        {
            shape = slide.Descendants<P.Shape>().ElementAtOrDefault(shapeIndex.Value);
        }
        else
        {
            shape = GetPlaceholderShape(slide, PlaceholderValues.Body)
                ?? GetPlaceholderShape(slide, PlaceholderValues.Title)
                ?? slide.Descendants<P.Shape>().FirstOrDefault();
        }

        if (shape == null)
            throw new InvalidOperationException("No valid text shape found on slide.");

        SetShapeText(shape, lines, bullets: normalized == "text/markdown", replace);

        slide.Save();
        presPart.Presentation?.Save();
    }

    ms.Flush();
    ms.Position = 0;

    var updated = await graphClient.UploadBinaryDataAsync(url,
        new BinaryData(ms.ToArray()), cancellationToken) ?? throw new FileNotFoundException($"No content found");
    return updated.ToResourceLinkBlock(updated?.Name!).ToCallToolResult();
}));


    [Description("List all text-containing shapes on a specific slide in a PowerPoint presentation (.pptx)")]
    [McpServerTool(Name = "openxml_powerpoint_get_shapes",
        Title = "Get shapes",
        ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> OpenXMLPowerPoint_GetShapes(
    [Description("PowerPoint file URL (.pptx)")] string url,
    [Description("Zero-based slide index")] int slideIndex,
    IServiceProvider serviceProvider,
    RequestContext<CallToolRequestParams> requestContext,
    CancellationToken cancellationToken = default)
    => await requestContext.WithExceptionCheck(async () =>
    await requestContext.WithOboGraphClient(async graphClient =>
    await requestContext.WithStructuredContent(async () =>
        {
            var downloadService = serviceProvider.GetRequiredService<DownloadService>();
            var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, url, cancellationToken);
            var file = files.FirstOrDefault() ?? throw new FileNotFoundException($"No PowerPoint found at {url}");

            using var ms = new MemoryStream(file.Contents.ToArray());
            using (var doc = PresentationDocument.Open(ms, false))
            {
                var presPart = doc.PresentationPart ?? throw new InvalidDataException("Missing PresentationPart");
                var slideId = presPart.Presentation?.SlideIdList?.Elements<SlideId>().ElementAt(slideIndex)
                    ?? throw new ArgumentOutOfRangeException(nameof(slideIndex));

                var slidePart = (SlidePart)presPart.GetPartById(slideId.RelationshipId!);
                var shapes = slidePart.Slide?.Descendants<P.Shape>()
                    .Select((shape, i) => new
                    {
                        shapeIndex = i,
                        placeholderType = shape.NonVisualShapeProperties?
                            .ApplicationNonVisualDrawingProperties?
                            .GetFirstChild<P.PlaceholderShape>()?.Type?.Value.ToString() ?? "None",
                        text = shape.TextBody?.InnerText?.Trim() ?? string.Empty
                    }).ToList() ?? new();

                return new { shapes = shapes ?? [] };
            }
        })));


    [Description("List all slides in a PowerPoint presentation (.pptx) with optional titles")]
    [McpServerTool(Name = "openxml_powerpoint_get_slides",
        Title = "Get slides",
        ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> OpenXMLPowerPoint_GetSlides(
    [Description("PowerPoint file URL (.pptx)")] string url,
    IServiceProvider serviceProvider,
    RequestContext<CallToolRequestParams> requestContext,
    CancellationToken cancellationToken = default)
    => await requestContext.WithExceptionCheck(async () =>
    await requestContext.WithOboGraphClient(async graphClient =>
    await requestContext.WithStructuredContent(async () =>
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, url, cancellationToken);
        var file = files.FirstOrDefault() ?? throw new FileNotFoundException($"No PowerPoint found at {url}");

        var slides = new List<object>();

        using var ms = new MemoryStream(file.Contents.ToArray());
        using (var doc = PresentationDocument.Open(ms, false))
        {
            var presPart = doc.PresentationPart ?? throw new InvalidDataException("Missing PresentationPart");
            var slideIds = presPart.Presentation?.SlideIdList?.Elements<SlideId>() ?? Enumerable.Empty<SlideId>();
            int i = 0;
            foreach (var slideId in slideIds)
            {
                var relId = slideId.RelationshipId!;
                var slidePart = (SlidePart)presPart.GetPartById(relId);
                var title = slidePart.Slide?.Descendants<P.Shape>()
                    .Select(s => s.TextBody?.InnerText)
                    .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? string.Empty;

                slides.Add(new
                {
                    index = i++,
                    relationshipId = relId,
                    title = title.Trim()
                });
            }
        }

        return new { slides = slides ?? [] };
    })));


    [Description("Create a new PowerPoint presentation (.pptx) from a .potx template URL")]
    [McpServerTool(Name = "openxml_powerpoint_create_from_template_file",
        Title = "New PowerPoint from template",
        ReadOnly = false, OpenWorld = false, Idempotent = false, Destructive = false)]
    public static async Task<CallToolResult?> OpenXMLPowerPoint_CreateFromTemplateFile(
    [Description("Filename without .pptx extension")] string fileName,
    [Description("Template URL (.potx or .pptx)")] string templateUrl,
    IServiceProvider serviceProvider,
    RequestContext<CallToolRequestParams> requestContext,
    CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async (graphClient) =>
{
    var downloadService = serviceProvider.GetRequiredService<DownloadService>();

    // 1️⃣ Download the template
    var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, templateUrl, cancellationToken);
    var file = files.FirstOrDefault() ?? throw new FileNotFoundException($"No template found at {templateUrl}");

    using var templateStream = new MemoryStream();
    await file.Contents.ToStream().CopyToAsync(templateStream, cancellationToken);
    templateStream.Position = 0;

    // 2️⃣ Open the POTX and convert to editable PPTX
    using (var templateDoc = PresentationDocument.Open(templateStream, true))
    {
        if (templateDoc.PresentationPart == null)
            throw new InvalidDataException("Template has no PresentationPart.");

        // Convert to a normal presentation type (editable)
        if (templateDoc.DocumentType != PresentationDocumentType.Presentation)
            templateDoc.ChangeDocumentType(PresentationDocumentType.Presentation);

        // Ensure it has at least one slide
        var presPart = templateDoc.PresentationPart;
        presPart.Presentation ??= new Presentation();
        presPart.Presentation.SlideIdList ??= new SlideIdList();

        // If template is completely empty, ensure one blank slide
        if (!presPart.SlideParts.Any())
        {
            var slideMasterPart = presPart.SlideMasterParts.FirstOrDefault()
                ?? throw new InvalidDataException("Template missing SlideMasterPart.");
            var layoutPart = slideMasterPart.SlideLayoutParts.FirstOrDefault()
                ?? throw new InvalidDataException("Template missing SlideLayoutPart.");

            var slidePart = presPart.AddNewPart<SlidePart>();
            slidePart.Slide = new P.Slide(new P.CommonSlideData(new P.ShapeTree(
                new P.NonVisualGroupShapeProperties(
                    new P.NonVisualDrawingProperties { Id = 1U, Name = "New Slide" },
                    new P.NonVisualGroupShapeDrawingProperties(),
                    new P.ApplicationNonVisualDrawingProperties()),
                new P.GroupShapeProperties(new A.TransformGroup())
            )));
            slidePart.AddPart(layoutPart);

            uint newId = 256U;
            var relId = presPart.GetIdOfPart(slidePart);
            presPart.Presentation.SlideIdList.Append(new SlideId { Id = newId, RelationshipId = relId });
        }

        presPart.Presentation.Save();
    }

    // 3️⃣ Upload as a new .pptx file
    templateStream.Flush();
    templateStream.Position = 0;
    // var safeName = SanitizeFileName(fileName);

    var uploaded = await graphClient.Upload(
        $"{fileName}.pptx",
        await BinaryData.FromStreamAsync(templateStream, cancellationToken),
        cancellationToken) ?? throw new FileNotFoundException($"No content found");

    return uploaded.ToCallToolResult();
}));


    [Description("Add a new slide to a PowerPoint presentation")]
    [McpServerTool(Name = "openxml_powerpoint_add_slide", ReadOnly = false, OpenWorld = false, Idempotent = true, Destructive = true)]
    public static async Task<CallToolResult?> OpenXMLPowerPoint_AddSlide(
        string url,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async graphClient =>
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var fileItems = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, url, cancellationToken);
        var fileItem = fileItems.FirstOrDefault();

        var newBinary = AddBlankSlide(fileItem?.Contents!);

        var uploaded = await graphClient.UploadBinaryDataAsync(url,
            newBinary, cancellationToken) ?? throw new FileNotFoundException($"No content found");

        return uploaded.ToResourceLinkBlock(uploaded?.Name!).ToCallToolResult();
    }));

    [Description("Create a new PowerPoint presentation")]
    [McpServerTool(ReadOnly = false, OpenWorld = false, Idempotent = false, Destructive = false)]
    public static async Task<CallToolResult?> OpenXMLPowerPoint_NewPresentation(
        [Description("Filename without .pptx extension")] string fileName,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async graphClient =>
    {
        using var stream = new MemoryStream();
        CreatePresentation(stream);

        var uploaded = await graphClient.Upload($"{fileName}.pptx",
            await BinaryData.FromStreamAsync(stream, cancellationToken), cancellationToken)
             ?? throw new FileNotFoundException($"No content found");

        return uploaded.ToCallToolResult();
    }));

    // 1) SLIDE VERWIJDEREN
    [Description("Remove a slide (by zero-based index) from a PowerPoint presentation")]
    [McpServerTool(Title = "Remove slide", Name = "openxml_powerpoint_remove_slide",
         ReadOnly = false, OpenWorld = false, Idempotent = false, Destructive = true)]
    public static async Task<CallToolResult?> OpenXMLPowerPoint_RemoveSlide(
        string url,
        int slideIndex,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async graphClient =>
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var fileItems = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, url, cancellationToken);
        var fileItem = fileItems.FirstOrDefault();
        if (fileItem == null) throw new FileNotFoundException($"No content found");

        var newBinary = RemoveSlideByIndex(fileItem.Contents, slideIndex);
        var uploaded = await graphClient.UploadBinaryDataAsync(url,
            newBinary, cancellationToken) ?? throw new FileNotFoundException($"No content found");

        return uploaded.ToResourceLinkBlock(uploaded?.Name!).ToCallToolResult(); ;
    }));

    // 2) SLIDE VERPLAATSEN (REORDER)
    [Description("Move a slide from one index to another (zero-based) in a PowerPoint presentation")]
    [McpServerTool(ReadOnly = false, OpenWorld = false, Idempotent = false, Destructive = true)]
    public static async Task<CallToolResult?> OpenXMLPowerPoint_MoveSlide(
        string url,
        int fromIndex,
        int toIndex,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async graphClient =>
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var fileItems = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, url, cancellationToken);
        var fileItem = fileItems.FirstOrDefault();
        if (fileItem == null) throw new FileNotFoundException($"No content found"); ;

        var newBinary = ReorderSlides(fileItem.Contents, fromIndex, toIndex);
        var uploaded = await graphClient.UploadBinaryDataAsync(url, newBinary, cancellationToken)
         ?? throw new FileNotFoundException($"No content found");

        return uploaded.ToResourceLinkBlock(uploaded?.Name!).ToCallToolResult(); ;
    }));

    private static BinaryData ReorderSlides(BinaryData pptx, int fromIndex, int toIndex)
    {
        using var inStream = new MemoryStream(pptx.ToArray());
        using var outStream = new MemoryStream();
        inStream.CopyTo(outStream);
        outStream.Position = 0;

        using (var presentation = PresentationDocument.Open(outStream, true))
        {
            var presentationPart = presentation.PresentationPart ?? throw new InvalidDataException("No presentation part found.");
            var slideIdList = presentationPart.Presentation?.SlideIdList ?? throw new InvalidDataException("No SlideIdList found.");

            var count = slideIdList.ChildElements.OfType<SlideId>().Count();
            if (count == 0) return new BinaryData(outStream.ToArray());

            if (fromIndex < 0 || fromIndex >= count) throw new ArgumentOutOfRangeException(nameof(fromIndex), $"Valid range: 0..{count - 1}");
            if (toIndex < 0 || toIndex >= count) throw new ArgumentOutOfRangeException(nameof(toIndex), $"Valid range: 0..{count - 1}");
            if (fromIndex == toIndex) return new BinaryData(outStream.ToArray());

            // Pak het te verplaatsen element
            var moving = (SlideId)slideIdList.ChildElements[fromIndex];

            // Verwijder eerst; indexen schuiven dan op
            slideIdList.RemoveChild(moving);

            // Als je naar een hogere index verplaatst, is doelindex nu -1
            if (toIndex > fromIndex) toIndex--;

            slideIdList.InsertAt(moving, toIndex);

            presentationPart.Presentation.Save();
        }

        return new BinaryData(outStream.ToArray());
    }

    private static BinaryData RemoveSlideByIndex(BinaryData pptx, int slideIndex)
    {
        using var inStream = new MemoryStream(pptx.ToArray());
        using var outStream = new MemoryStream();
        inStream.CopyTo(outStream);
        outStream.Position = 0;

        using (var presentation = PresentationDocument.Open(outStream, true))
        {
            var presentationPart = presentation.PresentationPart ?? throw new InvalidDataException("No presentation part found.");
            var slideIdList = presentationPart.Presentation.SlideIdList ?? throw new InvalidDataException("No SlideIdList found.");

            var slideIds = slideIdList.ChildElements.OfType<SlideId>().ToList();
            if (slideIndex < 0 || slideIndex >= slideIds.Count)
                throw new ArgumentOutOfRangeException(nameof(slideIndex), $"Valid range: 0..{slideIds.Count - 1}");

            var targetSlideId = slideIds[slideIndex];
            var relId = targetSlideId.RelationshipId!;
            var slidePart = (SlidePart)presentationPart.GetPartById(relId!);

            // 1) verwijder SlideId uit de lijst (volgorde bepaalt show-volgorde)
            slideIdList.RemoveChild(targetSlideId);

            // 2) delete de SlidePart (OpenXML zorgt voor subparts)
            presentationPart.DeletePart(slidePart);

            presentationPart.Presentation.Save();
        }

        return new BinaryData(outStream.ToArray());
    }


    private static BinaryData AddBlankSlide(BinaryData original)
    {
        var buffer = original.ToArray();
        using var ms = new MemoryStream();
        ms.Write(buffer, 0, buffer.Length);
        ms.Position = 0;

        using (var presentation = PresentationDocument.Open(ms, true))
        {
            var presPart = presentation.PresentationPart
                ?? throw new InvalidDataException("No presentation part found.");

            presPart.Presentation.SlideIdList ??= new SlideIdList();

            // ✅ Pick a slide layout that has placeholders
            var slideMasterPart = presPart.SlideMasterParts.FirstOrDefault()
                ?? throw new InvalidDataException("No SlideMasterPart found.");
            var slideLayoutPart = slideMasterPart.SlideLayoutParts
                .FirstOrDefault(l =>
                    l.SlideLayout?.CommonSlideData?.ShapeTree?
                    .Descendants<P.PlaceholderShape>().Any() == true)
                ?? slideMasterPart.SlideLayoutParts.First(); // fallback

            // ✅ Create new slide *from that layout*
            var newSlidePart = presPart.AddNewPart<SlidePart>();
            newSlidePart.AddPart(slideLayoutPart);

            newSlidePart.Slide = new P.Slide(
                new P.CommonSlideData(
                    new P.ShapeTree(
                        new P.NonVisualGroupShapeProperties(
                            new P.NonVisualDrawingProperties { Id = 1U, Name = "Title and Content" },
                            new P.NonVisualGroupShapeDrawingProperties(),
                            new P.ApplicationNonVisualDrawingProperties()),
                        new P.GroupShapeProperties(new A.TransformGroup())
                    )
                )
            );

            // ✅ Add the placeholders from the layout
            newSlidePart.Slide.CommonSlideData?.ShapeTree?.Append(
                new P.Shape(
                    new P.NonVisualShapeProperties(
                        new P.NonVisualDrawingProperties { Id = 2U, Name = "Title" },
                        new P.NonVisualShapeDrawingProperties(),
                        new P.ApplicationNonVisualDrawingProperties(
                            new P.PlaceholderShape { Type = PlaceholderValues.Title })),
                    new P.ShapeProperties(),
                    new P.TextBody(
                        new A.BodyProperties(),
                        new A.ListStyle(),
                        new A.Paragraph(new A.EndParagraphRunProperties())
                    )
                ),
                new P.Shape(
                    new P.NonVisualShapeProperties(
                        new P.NonVisualDrawingProperties { Id = 3U, Name = "Content Placeholder" },
                        new P.NonVisualShapeDrawingProperties(),
                        new P.ApplicationNonVisualDrawingProperties(
                            new P.PlaceholderShape { Type = PlaceholderValues.Body })),
                    new P.ShapeProperties(),
                    new P.TextBody(
                        new A.BodyProperties(),
                        new A.ListStyle(),
                        new A.Paragraph(new A.EndParagraphRunProperties())
                    )
                )
            );

            // ✅ Register the new slide in the presentation
            uint newId = presPart.Presentation.SlideIdList
                .Elements<SlideId>()
                .Select(s => s.Id?.Value ?? 255U)
                .DefaultIfEmpty(255U)
                .Max() + 1;

            var relId = presPart.GetIdOfPart(newSlidePart);
            presPart.Presentation.SlideIdList.Append(new SlideId { Id = newId, RelationshipId = relId });

            presPart.Presentation.Save();
        }

        return new BinaryData(ms.ToArray());
    }

    private static BinaryData AddBlankSlide222(BinaryData original)
    {
        // werk in één MemoryStream (geen aparte in/out streams nodig)
        var buffer = original.ToArray();
        using var ms = new MemoryStream();
        ms.Write(buffer, 0, buffer.Length);
        ms.Position = 0;

        using (var presentation = PresentationDocument.Open(ms, true))
        {
            var presentationPart = presentation.PresentationPart
                ?? throw new InvalidDataException("No presentation part found in PPTX.");

            // zorg dat SlideIdList bestaat
            presentationPart.Presentation.SlideIdList ??= new SlideIdList();

            // pak eerste layout als template
            var slideMasterPart = presentationPart.SlideMasterParts.FirstOrDefault()
                ?? throw new InvalidDataException("No SlideMasterPart found.");
            var slideLayoutPart = slideMasterPart.SlideLayoutParts.FirstOrDefault()
                ?? throw new InvalidDataException("No SlideLayoutPart found.");

            // maak nieuwe slidepart
            var newSlidePart = presentationPart.AddNewPart<SlidePart>();
            newSlidePart.Slide = new P.Slide(
                new P.CommonSlideData(
                    new P.ShapeTree(
                        new P.NonVisualGroupShapeProperties(
                            new P.NonVisualDrawingProperties { Id = 1U, Name = "Slide" },
                            new P.NonVisualGroupShapeDrawingProperties(),
                            new P.ApplicationNonVisualDrawingProperties()),
                        new P.GroupShapeProperties(new A.TransformGroup())
                    )
                )
            );

            // koppel layout aan nieuwe slide
            newSlidePart.AddPart(slideLayoutPart);

            // append nieuwe SlideId in de lijst
            var slideIdList = presentationPart.Presentation.SlideIdList!;
            uint maxSlideId = slideIdList.ChildElements
                .OfType<SlideId>()
                .Select(s => s.Id?.Value ?? 255U)
                .DefaultIfEmpty(255U)
                .Max();
            uint newSlideId = maxSlideId + 1U;
            var relId = presentationPart.GetIdOfPart(newSlidePart);
            slideIdList.Append(new SlideId { Id = newSlideId, RelationshipId = relId });

            // save
            presentationPart.Presentation.Save();
        }

        return new BinaryData(ms.ToArray());
    }

    public static void CreatePresentation(Stream stream)
    {
        using var presentationDoc =
            PresentationDocument.Create(stream, PresentationDocumentType.Presentation);
        // add the PresentationPart
        var presentationPart = presentationDoc.AddPresentationPart();
        presentationPart.Presentation = new Presentation();

        // add a SlideMasterPart and a SlideLayoutPart (very minimal)
        var slideMasterPart = presentationPart.AddNewPart<SlideMasterPart>();
        slideMasterPart.SlideMaster = new SlideMaster(
            new CommonSlideData(new ShapeTree()),
            new SlideLayoutIdList(),
            new TextStyles());
        slideMasterPart.SlideMaster.Save();

        var slideLayoutPart = slideMasterPart.AddNewPart<SlideLayoutPart>();
        slideLayoutPart.SlideLayout = new SlideLayout(
            new CommonSlideData(new ShapeTree()));
        slideLayoutPart.SlideLayout.Save();

        // add a SlidePart using the layout
        var slidePart = presentationPart.AddNewPart<SlidePart>();
        slidePart.Slide = new Slide(
            new CommonSlideData(new ShapeTree()));
        slidePart.AddPart(slideLayoutPart);
        slidePart.Slide.Save();

        // wire slide into presentation
        presentationPart.Presentation.SlideIdList = new SlideIdList();
        var id = presentationPart.GetIdOfPart(slidePart);
        presentationPart.Presentation.SlideIdList.Append(
            new SlideId() { Id = 256U, RelationshipId = id });
        presentationPart.Presentation.Save();
    }

}
