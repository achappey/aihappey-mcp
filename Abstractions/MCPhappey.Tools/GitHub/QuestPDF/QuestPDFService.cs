using System.ComponentModel;
using ModelContextProtocol.Server;

using QUE = QuestPDF;
using QUEH = QuestPDF.Helpers;
using QUEI = QuestPDF.Infrastructure;

// IMPORTANT: bring extension methods into scope
using QuestPDF.Fluent;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using QuestPDF.Helpers;
using QuestPDF.Markdown;
using MCPhappey.Common.Extensions;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.GitHub.QuestPDF;

public static class QuestPDFService
{
    [Description("Generates a simple A4 PDF with a title, body text and page numbers.")]
    [McpServerTool(
        Title = "Render simple PDF",
        Name = "github_questpdf_render_simple",
        ReadOnly = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GitHubQuestPdf_RenderSimple(
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Title displayed at the top of the page(s).")] string title,
        [Description("Main body text (plain text).")] string content,
        [Description("Page margin in centimeters (0–5). Default: 2.0")] double marginCm = 2.0,
        [Description("Base font size in points (6–48). Default: 12")] float fontSize = 12f)
        => await requestContext.WithExceptionCheck(async () =>
        {
            if (string.IsNullOrWhiteSpace(title))
                throw new ArgumentException("Title is required.", nameof(title));
            if (string.IsNullOrWhiteSpace(content))
                throw new ArgumentException("Content is required.", nameof(content));
            if (marginCm < 0 || marginCm > 5)
                throw new ArgumentOutOfRangeException(nameof(marginCm), "Margin must be between 0 and 5 cm.");
            if (fontSize < 6 || fontSize > 48)
                throw new ArgumentOutOfRangeException(nameof(fontSize), "Font size must be between 6 and 48 pt.");

            // Community license is fine for < $1M revenue / FOSS / non-profit / evaluation
            QUE.Settings.License = QUEI.LicenseType.Community;

            byte[] pdfBytes =
                Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);

                        // Use (value, unit) overload — no multiplication with Unit
                        page.Margin((float)marginCm, QUEI.Unit.Centimetre);

                        page.DefaultTextStyle(t => t.FontSize(fontSize));

                        page.Header().Text(title)
                            .SemiBold()
                            .FontSize(fontSize + 6)
                            .FontColor(Colors.Blue.Medium);

                        page.Content()
                            .PaddingTop(10)
                            .Text(content);

                        page.Footer().AlignCenter().Text(txt =>
                        {
                            txt.CurrentPageNumber();
                            txt.Span(" / ");
                            txt.TotalPages();
                        });
                    });
                })
                .GeneratePdf();

            return pdfBytes.ToBlobContent("https://www.questpdf.com/", MimeTypes.Pdf)
                .ToCallToolResult();
        });

    [Description("Generates a simple A4 PDF document from Markdown text.")]
    [McpServerTool(
        Title = "Render Markdown PDF",
        Name = "github_questpdf_render_markdown",
        ReadOnly = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GitHubQuestPdf_RenderMarkdown(
   RequestContext<CallToolRequestParams> requestContext,
   [Description("Markdown content to render as the main body.")] string markdown,
   [Description("Page margin in centimeters (0–5). Default: 2.0")] double marginCm = 2.0,
   [Description("Base font size in points (6–48). Default: 12")] float fontSize = 12f)
   => await requestContext.WithExceptionCheck(async () =>
   {
       if (string.IsNullOrWhiteSpace(markdown))
           throw new ArgumentException("Markdown content is required.", nameof(markdown));
       if (marginCm < 0 || marginCm > 5)
           throw new ArgumentOutOfRangeException(nameof(marginCm), "Margin must be between 0 and 5 cm.");
       if (fontSize < 6 || fontSize > 48)
           throw new ArgumentOutOfRangeException(nameof(fontSize), "Font size must be between 6 and 48 pt.");

       // Use community license for open / internal use
       QUE.Settings.License = QUEI.LicenseType.Community;

       byte[] pdfBytes = Document.Create(container =>
       {
           container.Page(page =>
           {
               page.Size(PageSizes.A4);
               page.Margin((float)marginCm, QUEI.Unit.Centimetre);
               page.DefaultTextStyle(t => t.FontSize(fontSize));
               page.PageColor(Colors.White);
               page.Content().Markdown(markdown);

           });
       }).GeneratePdf();

       return pdfBytes.ToBlobContent("https://www.questpdf.com/", MimeTypes.Pdf)
                      .ToCallToolResult();
   });
}
