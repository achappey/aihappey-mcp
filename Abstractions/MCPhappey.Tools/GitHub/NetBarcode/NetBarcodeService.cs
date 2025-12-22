using System.ComponentModel;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using MCPhappey.Core.Extensions;
using MCPhappey.Common.Extensions;
using NetBarcode;

namespace MCPhappey.Tools.GitHub.NetBarcode;

public static class NetBarcodeService
{
    [Description("Generate multiple barcodes from a comma-separated list of texts.")]
    [McpServerTool(
    Title = "Generate barcode batch",
    Name = "netbarcode_batch",
    ReadOnly = true,
    OpenWorld = false)]
    public static async Task<CallToolResult?> GenerateBarcodeBatch(
    [Description("Comma-separated list of texts to encode.")]
        string texts,
    [Description("Barcode type for all (Code128, EAN13, Code39, etc.).")]
        [DefaultValue("Code128")]
        string barcodeType = "Code128",
    [Description("Show label text below barcodes.")]
        [DefaultValue(true)]
        bool showLabel = true,
    RequestContext<CallToolRequestParams>? requestContext = null,
    IServiceProvider? serviceProvider = null,
    CancellationToken cancellationToken = default)
    =>
    await requestContext!.WithExceptionCheck(async () =>
    {
        var type = ParseBarcodeType(barcodeType);
        var items = texts
            .Split(',')
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        var blocks = new List<ContentBlock>();

        foreach (var text in items)
        {
            var barcode = new Barcode();
            barcode.Configure(settings =>
                {
                    settings.Text = text;
                    settings.ShowLabel = showLabel;
                    settings.BarcodeType = Enum.Parse<BarcodeType>(barcodeType);
                }).SaveImageFile("12345456", "barcode.png");

            var base64 = barcode.GetBase64Image(text);

            blocks.Add(new ImageContentBlock()
            {
                Data = base64,
                MimeType = "image/png"
            });
        }

        return blocks.ToCallToolResponse();
    });


    [Description("Generate a barcode image from text.")]
    [McpServerTool(
          Title = "Generate barcode",
          Name = "netbarcode_generate",
          ReadOnly = true,
          OpenWorld = false)]
    public static async Task<CallToolResult?> GenerateBarcode(
          [Description("Text to encode in the barcode.")]
        string text,
          [Description("Barcode type (Code128, EAN13, Code39, etc.).")]
        [DefaultValue("Code128")]
        string barcodeType = "Code128",
          [Description("Show label text below barcode.")]
        [DefaultValue(true)]
        bool showLabel = true,
          RequestContext<CallToolRequestParams>? requestContext = null,
          IServiceProvider? serviceProvider = null,
          CancellationToken cancellationToken = default) =>
          await requestContext!.WithExceptionCheck(async () =>
          {
              // 🧩 Parse to enum (defaults to Code128)
              var type = ParseBarcodeType(barcodeType);

              var barcode = new Barcode();
              barcode.Configure(settings =>
                  {
                      settings.Text = text;
                      settings.ShowLabel = showLabel;
                      settings.BarcodeType = Enum.Parse<BarcodeType>(barcodeType);
                  });

              var base64 = barcode.GetBase64Image(text);

              return new ImageContentBlock()
              {
                  Data = base64,
                  MimeType = "image/png"
              }.ToCallToolResult();
          });

    private static BarcodeType ParseBarcodeType(string input)
    {
        return input.ToLowerInvariant() switch
        {
            "code128" or "code-128" => BarcodeType.Code128,
            "code128a" => BarcodeType.Code128A,
            "code128b" => BarcodeType.Code128B,
            "code128c" => BarcodeType.Code128C,
            "ean13" or "ean-13" => BarcodeType.EAN13,
            "ean8" or "ean-8" => BarcodeType.EAN8,
            "code39" => BarcodeType.Code39,
            "code39e" => BarcodeType.Code39E,
            "code93" => BarcodeType.Code93,
            "code11" => BarcodeType.Code11,
            "codabar" => BarcodeType.Codabar,
            _ => BarcodeType.Code128
        };
    }
}

