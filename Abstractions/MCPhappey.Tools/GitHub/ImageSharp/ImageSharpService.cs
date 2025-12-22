using System.ComponentModel;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MCPhappey.Tools.GitHub.ImageSharp;

public static class ImageSharpService
{
    [Description("Resize an image to a given width/height.")]
    [McpServerTool(
        Title = "Resize image",
        Name = "imagesharp_resize",
        Destructive = false,
        OpenWorld = false)]
    public static async Task<CallToolResult?> ImageSharp_Resize(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Url of the png image. SHarePoint and OneDrive links are supported")]
        string fileUrl,
        [Description("Target width in pixels (0 = auto).")]
        int width,
        [Description("Target height in pixels (0 = auto).")]
        int height,
        [Description("Keep aspect ratio (true = keep).")]
        bool keepAspect = true,
        CancellationToken cancellationToken = default)
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var mcpServer = requestContext.Server;
        var files = await downloadService.ScrapeContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
        var file = files.First() ?? throw new Exception("File missing");

        using var image = await Image.LoadAsync(file.Contents.ToStream(), cancellationToken);

        // determine target
        int targetWidth = width;
        int targetHeight = height;

        if (keepAspect)
        {
            // only width -> scale height
            if (width > 0 && height == 0)
            {
                targetHeight = image.Height * width / image.Width;
            }
            // only height -> scale width
            else if (height > 0 && width == 0)
            {
                targetWidth = image.Width * height / image.Height;
            }
            // none set -> just return original
            else if (width == 0 && height == 0)
            {
                targetWidth = image.Width;
                targetHeight = image.Height;
            }
        }
        else
        {
            if (width == 0) targetWidth = image.Width;
            if (height == 0) targetHeight = image.Height;
        }

        image.Mutate(x => x.Resize(targetWidth, targetHeight));

        using var outputStream = new MemoryStream();
        await image.SaveAsPngAsync(outputStream, cancellationToken);
        var resizedBytes = outputStream.ToArray();

        // Return via MCP upload if available
        var uploaded = await requestContext.Server.Upload(
            serviceProvider,
            requestContext.ToOutputFileName("png"),
            BinaryData.FromBytes(resizedBytes),
            cancellationToken);

        return new CallToolResult
        {
            Content =
            [
                uploaded!,
                    new
                    {
                        originalWidth = image.Width,
                        originalHeight = image.Height,
                        width = targetWidth,
                        height = targetHeight,
                        mimeType = "image/png"
                    }.ToJsonContentBlock("https://github.com/SixLabors/ImageSharp")
            ]
        };
    }

    [Description("Optimize and compress an image to reduce file size.")]
    [McpServerTool(
       Title = "Optimize image",
       Name = "imagesharp_optimize",
       Destructive = false,
       OpenWorld = false)]
    public static async Task<CallToolResult?> ImageSharp_Optimize(
       IServiceProvider serviceProvider,
       RequestContext<CallToolRequestParams> requestContext,
       [Description("Url of the image. SharePoint and OneDrive links are supported.")]
        string fileUrl,
       [Description("Output format (png, jpg, webp). Default: webp")]
        string format = "webp",
       [Description("Quality level (1-100). Default: 85")]
        int quality = 85,
       CancellationToken cancellationToken = default)
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var files = await downloadService.ScrapeContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
        var file = files.FirstOrDefault() ?? throw new Exception("File missing");

        using var image = await Image.LoadAsync(file.Contents.ToStream(), cancellationToken);
        using var output = new MemoryStream();

        format = format.ToLowerInvariant();
        quality = Math.Clamp(quality, 1, 100);

        switch (format)
        {
            case "jpg":
            case "jpeg":
                await image.SaveAsJpegAsync(output, new JpegEncoder { Quality = quality }, cancellationToken);
                break;
            case "png":
                await image.SaveAsPngAsync(output, new PngEncoder { CompressionLevel = PngCompressionLevel.BestCompression }, cancellationToken);
                break;
            case "webp":
            default:
                await image.SaveAsWebpAsync(output, new WebpEncoder { Quality = quality }, cancellationToken);
                format = "webp";
                break;
        }

        var optimizedBytes = output.ToArray();
        var uploaded = await requestContext.Server.Upload(
            serviceProvider,
            requestContext.ToOutputFileName(format),
            BinaryData.FromBytes(optimizedBytes),
            cancellationToken);

        return new CallToolResult
        {
            Content =
            [
                uploaded!,
                new
                {
                    originalFormat = file.MimeType,
                    optimizedFormat = format,
                    quality,
                    optimizedSizeKb = optimizedBytes.Length / 1024,
                    mimeType = $"image/{format}"
                }.ToJsonContentBlock("https://github.com/SixLabors/ImageSharp")
            ]
        };
    }

    [Description("Add a text or logo watermark to an image.")]
    [McpServerTool(
           Title = "Watermark image",
           Name = "imagesharp_watermark",
           Destructive = false,
           OpenWorld = false)]
    public static async Task<CallToolResult?> ImageSharp_Watermark(
           IServiceProvider serviceProvider,
           RequestContext<CallToolRequestParams> requestContext,
           [Description("Url of the image. SharePoint and OneDrive links are supported.")]
        string fileUrl,
           [Description("Optional logo url (if empty, text is used instead).")]
        string? logoUrl = null,
           [Description("Watermark text (used if logoUrl is empty).")]
        string watermarkText = "Fakton",
           [Description("Font size for text watermark.")]
        float fontSize = 42f,
           [Description("Opacity (0–1). Default = 0.5")]
        float opacity = 0.5f,
           [Description("Position: bottom-right, center, top-left, etc.")]
        string position = "bottom-right",
           CancellationToken cancellationToken = default)
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var files = await downloadService.ScrapeContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
        var file = files.FirstOrDefault() ?? throw new Exception("File missing");

        using var image = await Image.LoadAsync<Rgba32>(file.Contents.ToStream(), cancellationToken);

        if (!string.IsNullOrWhiteSpace(logoUrl))
        {
            // Load logo
            var logoFiles = await downloadService.ScrapeContentAsync(serviceProvider, requestContext.Server, logoUrl, cancellationToken);
            var logo = logoFiles.FirstOrDefault() ?? throw new Exception("Logo missing");

            using var logoImg = await Image.LoadAsync<Rgba32>(logo.Contents.ToStream(), cancellationToken);

            var targetX = position switch
            {
                "top-left" => 10,
                "top-right" => image.Width - logoImg.Width - 10,
                "bottom-left" => 10,
                "center" => (image.Width - logoImg.Width) / 2,
                _ => image.Width - logoImg.Width - 10 // bottom-right default
            };

            var targetY = position switch
            {
                "top-left" => 10,
                "top-right" => 10,
                "bottom-left" => image.Height - logoImg.Height - 10,
                "center" => (image.Height - logoImg.Height) / 2,
                _ => image.Height - logoImg.Height - 10
            };

            image.Mutate(x => x.DrawImage(logoImg, new Point(targetX, targetY), opacity));
        }
        else
        {
            // Text watermark
            var font = SystemFonts.CreateFont("Arial", fontSize);

            // set up text options
            var textOptions = new TextOptions(font)
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };

            // measure text
            var textSize = TextMeasurer.MeasureSize(watermarkText, textOptions);

            // pick position
            float x = 20;
            float y = 20;

            if (position.Contains("right"))
                x = image.Width - textSize.Width - 20;
            else if (position.Contains("center"))
                x = (image.Width - textSize.Width) / 2;

            if (position.Contains("bottom"))
                y = image.Height - textSize.Height - 20;
            else if (position.Contains("center"))
                y = (image.Height - textSize.Height) / 2;

            // draw it
            image.Mutate(ctx =>
                ctx.DrawText(
                    new DrawingOptions
                    {
                        GraphicsOptions = new GraphicsOptions
                        {
                            Antialias = true,
                            BlendPercentage = opacity
                        }
                    },
                    watermarkText,
                    font,
                    Color.White,
                    new PointF(x, y)
                )
            );
        }

        using var ms = new MemoryStream();
        await image.SaveAsPngAsync(ms, cancellationToken);
        var bytes = ms.ToArray();

        var uploaded = await requestContext.Server.Upload(
            serviceProvider,
            requestContext.ToOutputFileName("png"),
            BinaryData.FromBytes(bytes),
            cancellationToken);

        return new CallToolResult
        {
            Content =
            [
                uploaded!,
                new
                {
                    source = fileUrl,
                    watermark = logoUrl ?? watermarkText,
                    opacity,
                    position,
                    mimeType = "image/png"
                }.ToJsonContentBlock("https://github.com/SixLabors/ImageSharp")
            ]
        };
    }

    [Description("Add a text banner overlay to an image.")]
    [McpServerTool(
       Title = "Text overlay image",
       Name = "imagesharp_textoverlay",
       Destructive = false,
       OpenWorld = false)]
    public static async Task<CallToolResult?> ImageSharp_TextOverlay(
       IServiceProvider serviceProvider,
       RequestContext<CallToolRequestParams> requestContext,
       [Description("Url of the image. SharePoint and OneDrive links are supported.")]
        string fileUrl,
       [Description("Text to overlay on the banner.")]
        string overlayText,
       [Description("Banner position: top or bottom. Default: bottom.")]
        string position = "bottom",
       [Description("Font size for overlay text.")]
        float fontSize = 48f,
       [Description("Background color of the banner (hex or named). Default: black.")]
        string backgroundColor = "black",
       [Description("Text color (hex or named). Default: white.")]
        string textColor = "white",
       [Description("Banner opacity 0–1. Default: 0.6")]
        float opacity = 0.6f,
       CancellationToken cancellationToken = default)
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var files = await downloadService.ScrapeContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
        var file = files.FirstOrDefault() ?? throw new Exception("File missing");

        using var image = await Image.LoadAsync<Rgba32>(file.Contents.ToStream(), cancellationToken);
        var font = SystemFonts.CreateFont("Arial", fontSize);
        var textOptions = new TextOptions(font)
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        // measure text height
        var textSize = TextMeasurer.MeasureSize(overlayText, textOptions);
        int bannerHeight = (int)(textSize.Height * 2.2); // small padding

        // banner rectangle coordinates
        var rect = position.ToLower() == "top"
            ? new Rectangle(0, 0, image.Width, bannerHeight)
            : new Rectangle(0, image.Height - bannerHeight, image.Width, bannerHeight);

        // parse colors
        var bgColor = Color.TryParse(backgroundColor, out var bgParsed) ? bgParsed : Color.Black;
        var fgColor = Color.TryParse(textColor, out var fgParsed) ? fgParsed : Color.White;

        // make background semi-transparent
        var bgWithAlpha = bgColor.WithAlpha(opacity);

        image.Mutate(ctx =>
        {
            // draw semi-transparent rectangle
            ctx.Fill(bgWithAlpha, rect);

            // draw text centered on banner
            var textPosition = new PointF(image.Width / 2f, rect.Y + bannerHeight / 2f);
            ctx.DrawText(
                new DrawingOptions
                {
                    GraphicsOptions = new GraphicsOptions { Antialias = true }
                },
                overlayText,
                font,
                fgColor,
                textPosition
            );
        });


        using var ms = new MemoryStream();
        await image.SaveAsPngAsync(ms, cancellationToken);
        var bytes = ms.ToArray();

        var uploaded = await requestContext.Server.Upload(
            serviceProvider,
            requestContext.ToOutputFileName("png"),
            BinaryData.FromBytes(bytes),
            cancellationToken);

        return new CallToolResult
        {
            Content =
            [
                uploaded!,
                new
                {
                    overlayText,
                    position,
                    fontSize,
                    backgroundColor,
                    textColor,
                    opacity,
                    mimeType = "image/png"
                }.ToJsonContentBlock("https://github.com/SixLabors/ImageSharp")
            ]
        };
    }

    [Description("Crop an image by coordinates and dimensions.")]
    [McpServerTool(
            Title = "Crop image",
            Name = "imagesharp_crop",
            Destructive = false,
            OpenWorld = false)]
    public static async Task<CallToolResult?> ImageSharp_Crop(
            IServiceProvider serviceProvider,
            RequestContext<CallToolRequestParams> requestContext,
            [Description("Url of the image. SharePoint and OneDrive links are supported.")]
        string fileUrl,
            [Description("X coordinate of the top-left corner.")]
        int x,
            [Description("Y coordinate of the top-left corner.")]
        int y,
            [Description("Width of the crop area.")]
        int width,
            [Description("Height of the crop area.")]
        int height,
            CancellationToken cancellationToken = default)
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var files = await downloadService.ScrapeContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
        var file = files.FirstOrDefault() ?? throw new Exception("File missing");

        using var image = await Image.LoadAsync<Rgba32>(file.Contents.ToStream(), cancellationToken);

        // clamp crop region to image bounds
        x = Math.Max(0, x);
        y = Math.Max(0, y);
        width = Math.Min(width, image.Width - x);
        height = Math.Min(height, image.Height - y);

        var cropRect = new Rectangle(x, y, width, height);
        image.Mutate(ctx => ctx.Crop(cropRect));

        using var ms = new MemoryStream();
        await image.SaveAsPngAsync(ms, cancellationToken);
        var bytes = ms.ToArray();

        var uploaded = await requestContext.Server.Upload(
            serviceProvider,
            requestContext.ToOutputFileName("png"),
            BinaryData.FromBytes(bytes),
            cancellationToken);

        return new CallToolResult
        {
            Content =
            [
                uploaded!,
                new
                {
                    x,
                    y,
                    width,
                    height,
                    mimeType = "image/png"
                }.ToJsonContentBlock("https://github.com/SixLabors/ImageSharp")
            ]
        };
    }

    [Description("Generate a square thumbnail (center crop + resize).")]
    [McpServerTool(
        Title = "Thumbnail image",
        Name = "imagesharp_thumbnail",
        Destructive = false,
        OpenWorld = false)]
    public static async Task<CallToolResult?> ImageSharp_Thumbnail(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Url of the image. SharePoint and OneDrive links are supported.")]
        string fileUrl,
        [Description("Target thumbnail size in pixels (e.g. 256).")]
        int size = 256,
        [Description("Output format (png or jpg). Default: png.")]
        string format = "png",
        CancellationToken cancellationToken = default)
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var files = await downloadService.ScrapeContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
        var file = files.FirstOrDefault() ?? throw new Exception("File missing");

        using var image = await Image.LoadAsync<Rgba32>(file.Contents.ToStream(), cancellationToken);

        // determine square crop
        int minDimension = Math.Min(image.Width, image.Height);
        int x = (image.Width - minDimension) / 2;
        int y = (image.Height - minDimension) / 2;
        var cropRect = new Rectangle(x, y, minDimension, minDimension);

        image.Mutate(ctx =>
        {
            ctx.Crop(cropRect);
            ctx.Resize(new ResizeOptions
            {
                Size = new Size(size, size),
                Mode = ResizeMode.Crop
            });
        });

        using var ms = new MemoryStream();
        format = format.ToLowerInvariant();
        if (format == "jpg" || format == "jpeg")
            await image.SaveAsJpegAsync(ms, cancellationToken);
        else
            await image.SaveAsPngAsync(ms, cancellationToken);

        var bytes = ms.ToArray();

        var uploaded = await requestContext.Server.Upload(
            serviceProvider,
            requestContext.ToOutputFileName($"thumbnail.{format}"),
            BinaryData.FromBytes(bytes),
            cancellationToken);

        return new CallToolResult
        {
            Content =
            [
                uploaded!,
                new
                {
                    thumbnailSize = size,
                    cropX = x,
                    cropY = y,
                    mimeType = $"image/{format}"
                }.ToJsonContentBlock("https://github.com/SixLabors/ImageSharp")
            ]
        };
    }

    [Description("Apply Gaussian blur to an image or a region.")]
    [McpServerTool(
       Title = "Blur image",
       Name = "imagesharp_blur",
       Destructive = false,
       OpenWorld = false)]
    public static async Task<CallToolResult?> ImageSharp_Blur(
       IServiceProvider serviceProvider,
       RequestContext<CallToolRequestParams> requestContext,
       [Description("Url of the image. SharePoint and OneDrive links are supported.")]
        string fileUrl,
       [Description("Blur radius (e.g. 10 = strong blur). Default: 6")]
        float radius = 6f,
       [Description("Optional crop region to blur (x,y,width,height). Leave empty to blur full image.")]
        string? region = null,
       [Description("Output format (png or jpg). Default: png.")]
        string format = "png",
       CancellationToken cancellationToken = default)
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var files = await downloadService.ScrapeContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
        var file = files.FirstOrDefault() ?? throw new Exception("File missing");

        using var image = await Image.LoadAsync<Rgba32>(file.Contents.ToStream(), cancellationToken);

        image.Mutate(ctx =>
        {
            if (!string.IsNullOrWhiteSpace(region))
            {
                var parts = region.Split(',', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 4 &&
                    int.TryParse(parts[0], out var x) &&
                    int.TryParse(parts[1], out var y) &&
                    int.TryParse(parts[2], out var w) &&
                    int.TryParse(parts[3], out var h))
                {
                    var rect = new Rectangle(x, y, w, h);
                    ctx.GaussianBlur(radius, rect);
                }
                else
                {
                    throw new ArgumentException("Invalid region format. Use: x,y,width,height");
                }
            }
            else
            {
                ctx.GaussianBlur(radius);
            }
        });

        using var ms = new MemoryStream();
        format = format.ToLowerInvariant();
        if (format == "jpg" || format == "jpeg")
            await image.SaveAsJpegAsync(ms, cancellationToken);
        else
            await image.SaveAsPngAsync(ms, cancellationToken);

        var bytes = ms.ToArray();

        var uploaded = await requestContext.Server.Upload(
            serviceProvider,
            requestContext.ToOutputFileName(format),
            BinaryData.FromBytes(bytes),
            cancellationToken);

        return new CallToolResult
        {
            Content =
            [
                uploaded!,
                new
                {
                    radius,
                    region,
                    mimeType = $"image/{format}"
                }.ToJsonContentBlock("https://github.com/SixLabors/ImageSharp")
            ]
        };
    }

    [Description("Combine multiple image URLs into a single grid or collage.")]
    [McpServerTool(
     Title = "Image composition",
     Name = "imagesharp_composite_urls",
     Destructive = false,
     OpenWorld = false)]
    public static async Task<CallToolResult?> ImageSharp_Composite_Urls(
     IServiceProvider serviceProvider,
     RequestContext<CallToolRequestParams> requestContext,
     [Description("List of image URLs (SharePoint or OneDrive). Minimum 2, max 6.")]
        string[] imageUrls,
     [Description("Grid columns (1–3). Default: 2.")]
        int columns = 2,
     [Description("Cell size in pixels. Default: 512.")]
        int cellSize = 512,
     [Description("Padding between images. Default: 10.")]
        int padding = 10,
     [Description("Output format (png or jpg). Default: png.")]
        string format = "png",
     CancellationToken cancellationToken = default) =>
    await requestContext.WithExceptionCheck(async () =>
    await requestContext.WithOboGraphClient(async (client) =>
    {
        if (imageUrls == null || imageUrls.Length < 2)
            throw new ArgumentException("Please provide at least 2 image URLs.");

        var data = await BuildCompositeAsync(
            serviceProvider, requestContext, imageUrls, columns, cellSize, padding, format, cancellationToken);

        var uploaded = await requestContext.Server.Upload(
            serviceProvider,
            requestContext.ToOutputFileName($"composite.{format}"),
            data,
            cancellationToken);

        return new CallToolResult
        {
            Content =
            [
                uploaded!,
                new
                {
                    count = imageUrls.Length,
                    columns,
                    cellSize,
                    padding,
                    mimeType = $"image/{format}"
                }.ToJsonContentBlock("https://github.com/SixLabors/ImageSharp")
            ]
        };
    }));

    [Description("Combine all images from a SharePoint folder into a collage.")]
    [McpServerTool(
       Title = "SharePoint folder composition",
       Name = "imagesharp_composite_sharepoint_folder",
       Destructive = false,
       OpenWorld = false)]
    public static async Task<CallToolResult?> ImageSharp_Composite_Folder(
       IServiceProvider serviceProvider,
       RequestContext<CallToolRequestParams> requestContext,
       [Description("SharePoint folder URL containing images.")]
        string sharepointFolderUrl,
       [Description("Grid columns (1–3). Default: 2.")]
        int columns = 2,
       [Description("Cell size in pixels. Default: 512.")]
        int cellSize = 512,
       [Description("Padding between images. Default: 10.")]
        int padding = 10,
       [Description("Output format (png or jpg). Default: png.")]
        string format = "png",
       CancellationToken cancellationToken = default) =>
    await requestContext.WithExceptionCheck(async () =>
    await requestContext.WithOboGraphClient(async (client) =>
    {
        var fileUrls = await client.GetFileUrlsFromFolderAsync(sharepointFolderUrl, cancellationToken);

        if (fileUrls == null || fileUrls.Count < 2)
            throw new Exception("No images found in folder or less than 2.");

        var data = await BuildCompositeAsync(
            serviceProvider, requestContext, fileUrls.ToArray(), columns, cellSize, padding, format, cancellationToken);

        var uploaded = await requestContext.Server.Upload(
            serviceProvider,
            requestContext.ToOutputFileName($"composite.{format}"),
            data,
            cancellationToken);

        return new CallToolResult
        {
            Content =
            [
                uploaded!,
                new
                {
                    folder = sharepointFolderUrl,
                    count = fileUrls.Count,
                    columns,
                    cellSize,
                    padding,
                    mimeType = $"image/{format}"
                }.ToJsonContentBlock("https://github.com/SixLabors/ImageSharp")
            ]
        };
    }));

    private static async Task<BinaryData> BuildCompositeAsync(
          IServiceProvider serviceProvider,
          RequestContext<CallToolRequestParams> requestContext,
          string[] imageUrls,
          int columns,
          int cellSize,
          int padding,
          string format,
          CancellationToken cancellationToken)
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();

        var loadedImages = new List<Image<Rgba32>>();
        foreach (var url in imageUrls)
        {
            var files = await downloadService.ScrapeContentAsync(serviceProvider, requestContext.Server, url, cancellationToken);
            var file = files.FirstOrDefault() ?? throw new Exception($"Missing file for {url}");
            var img = await Image.LoadAsync<Rgba32>(file.Contents.ToStream(), cancellationToken);
            img.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(cellSize, cellSize),
                Mode = ResizeMode.Crop
            }));
            loadedImages.Add(img);
        }

        int rows = (int)Math.Ceiling((double)loadedImages.Count / columns);
        int canvasWidth = columns * cellSize + (columns + 1) * padding;
        int canvasHeight = rows * cellSize + (rows + 1) * padding;

        using var canvas = new Image<Rgba32>(canvasWidth, canvasHeight, Color.LightGray);

        int index = 0;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                if (index >= loadedImages.Count) break;
                int x = padding + c * (cellSize + padding);
                int y = padding + r * (cellSize + padding);
                canvas.Mutate(ctx => ctx.DrawImage(loadedImages[index], new Point(x, y), 1f));
                index++;
            }
        }

        using var ms = new MemoryStream();
        format = format.ToLowerInvariant();
        if (format == "jpg" || format == "jpeg")
            await canvas.SaveAsJpegAsync(ms, cancellationToken);
        else
            await canvas.SaveAsPngAsync(ms, cancellationToken);

        var bytes = ms.ToArray();

        foreach (var img in loadedImages) img.Dispose();
        return BinaryData.FromBytes(bytes);
    }

}
