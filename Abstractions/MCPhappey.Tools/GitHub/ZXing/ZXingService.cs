using System.ComponentModel;
using System.Drawing;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ZXing;
using ZXing.Common;
using MCPhappey.Core.Extensions;
using MCPhappey.Common.Extensions;
using Microsoft.KernelMemory.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using MCPhappey.Core.Services;
using ZXing.Windows.Compatibility;

namespace MCPhappey.Tools.GitHub.ZXing;

public static class ZXingService
{
    [Description("Decode a barcode or QR code from an image stream or URL.")]
    [McpServerTool(Name = "zxing_decode_barcode", ReadOnly = true)]
    public static async Task<CallToolResult?> DecodeBarcodeAsync(
           RequestContext<CallToolRequestParams> requestContext,
           IServiceProvider serviceProvider,
           [Description("Image URL.")] string imageUrl)
           => await requestContext.WithExceptionCheck(async () =>
              await requestContext.WithStructuredContent(async () =>
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var images = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server,
            imageUrl);

        using var bitmap = (Bitmap)Image.FromStream(images.FirstOrDefault()?.Contents.ToStream()!);
        var reader = new BarcodeReader(); // ✅ correct reader
        var result = reader.Decode(bitmap);
        return result;
    }));

    [Description("Generate a barcode or QR code as an image stream.")]
    [McpServerTool(Name = "zxing_encode_barcode", ReadOnly = false)]
    public static async Task<CallToolResult?> EncodeBarcodeAsync(
            RequestContext<CallToolRequestParams> requestContext,
           [Description("Text to encode.")] string text,
           [Description("Barcode format (default QR_CODE).")] string format = "QR_CODE",
           [Description("Optional width.")] int width = 300,
           [Description("Optional height.")] int height = 300)
             => await requestContext.WithExceptionCheck(async () =>
    {

        var writer = new BarcodeWriterPixelData
        {
            Format = Enum.TryParse(format, true, out BarcodeFormat parsed) ? parsed : BarcodeFormat.QR_CODE,
            Options = new EncodingOptions { Width = width, Height = height, Margin = 0 }
        };

        var pixelData = writer.Write(text);
        using var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var bmpData = bitmap.LockBits(
            new Rectangle(0, 0, width, height),
            System.Drawing.Imaging.ImageLockMode.WriteOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            System.Runtime.InteropServices.Marshal.Copy(pixelData.Pixels, 0, bmpData.Scan0, pixelData.Pixels.Length);
        }
        finally
        {
            bitmap.UnlockBits(bmpData);
        }

        using var ms = new MemoryStream();
        bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        ms.Position = 0;

        return new ImageContentBlock()
        {
            Data = ms.ToArray(),
            MimeType = MimeTypes.ImagePng,
        }.ToCallToolResult();
    });
}
