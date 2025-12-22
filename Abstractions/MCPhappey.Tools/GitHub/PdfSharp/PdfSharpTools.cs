using System.ComponentModel;
using PdfSharpCore.Pdf;
using PdfSharpCore.Drawing;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using MCPhappey.Core.Extensions;
using MCPhappey.Common.Extensions;
using Microsoft.Extensions.DependencyInjection;
using MCPhappey.Core.Services;

namespace MCPhappey.Tools.GitHub.PdfSharp
{
    public static class PdfSharpTools
    {
        [Description("Generate a PDF (in memory) with title, body and optional image. Returns byte array.")]
        [McpServerTool(Name = "pdfsharp_create_pdf_stream", ReadOnly = false)]
        public static async Task<CallToolResult?> PdfSharp_CreatePdfStream(
            RequestContext<CallToolRequestParams> requestContext,
            IServiceProvider serviceProvider,
            [Description("Title text")] string title,
            [Description("Body text content")] string bodyText,
            [Description("Optional image URL")] string? imageUrl = null,
            CancellationToken cancellationToken = default)
              => await requestContext.WithExceptionCheck(async () =>
        {

            using var doc = new PdfDocument();
            var page = doc.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);

            var titleFont = new XFont("Arial", 20, XFontStyle.Bold);
            var bodyFont = new XFont("Arial", 12, XFontStyle.Regular);

            // Title
            gfx.DrawString(title, titleFont, XBrushes.Black,
                new XRect(40, 40, page.Width - 80, 40), XStringFormats.TopLeft);

            // Body
            gfx.DrawString(bodyText, bodyFont, XBrushes.Black,
                new XRect(40, 100, page.Width - 80, page.Height - 140), XStringFormats.TopLeft);

            // Optional image (from URL or base64)
            if (!string.IsNullOrWhiteSpace(imageUrl))
            {
                var downloadService = serviceProvider.GetRequiredService<DownloadService>();

                var images = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, imageUrl, cancellationToken);
                using var img = XImage.FromStream(() => images.FirstOrDefault()?.Contents.ToStream());
                gfx.DrawImage(img, 40, page.Height - 250, 200, 150);
            }

            // Save to memory
            using var ms = new MemoryStream();
            doc.Save(ms, false);
            var bytes = ms.ToArray();

            // Example upload (replace with your own Graph/Blob client)
            // await graphClient.Upload($"{Guid.NewGuid()}.pdf", BinaryData.FromBytes(bytes));
            var uploaded = await requestContext.Server.Upload(serviceProvider, requestContext.ToOutputFileName("pdf"), BinaryData.FromBytes(bytes));

            return uploaded!.ToCallToolResult();

        });
    }

}
