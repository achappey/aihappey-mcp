using System.ComponentModel;
using System.Text.Json;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MCPhappey.Tools.GitHub.NAudio;

public static class NAudioRender
{
    [Description("Render a waveform PNG preview from an audio file URL and upload it to SharePoint/OneDrive.")]
    [McpServerTool(Title = "Render waveform PNG", Name = "naudio_render_waveform_png", ReadOnly = false, Idempotent = true, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> RenderWaveformPng(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("SharePoint, OneDrive, or HTTPS URL of the source audio file.")] string fileUrl,
        [Description("Image width in pixels. Default 1200.")] int width = 1200,
        [Description("Image height in pixels. Default 300.")] int height = 300,
        [Description("Waveform foreground color hex, e.g. #2B8CFF.")] string waveColor = "#2B8CFF",
        [Description("Background color hex, e.g. #101820.")] string backgroundColor = "#101820",
        [Description("Optional start time in seconds.")] double startSeconds = 0,
        [Description("Optional duration in seconds. 0 renders to end.")] double durationSeconds = 0,
        [Description("Optional output filename.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            width = Math.Clamp(width, 128, 4096);
            height = Math.Clamp(height, 64, 2048);
            using var input = await NAudioShared.DownloadAudioAsync(serviceProvider, requestContext, fileUrl, cancellationToken);
            using var reader = NAudioShared.OpenAudioFile(input.TempPath);
            var points = NAudioShared.BuildWaveform(reader, width, startSeconds, durationSeconds > 0 ? durationSeconds : null);
            var bytes = RenderWaveformImage(points, width, height, ParseColor(backgroundColor), ParseColor(waveColor));
            var payload = new
            {
                source = NAudioShared.GetMetadata(reader, input),
                width,
                height,
                startSeconds,
                durationSeconds = durationSeconds > 0 ? durationSeconds : reader.TotalTime.TotalSeconds - startSeconds,
                pointCount = points.Count
            };
            return await NAudioShared.UploadResultAsync(serviceProvider, requestContext, filename, "png", bytes, payload, cancellationToken);
        });

    [Description("Render waveform peaks as JSON for custom UI previews without creating an image.")]
    [McpServerTool(Title = "Render waveform JSON", Name = "naudio_render_waveform_json", ReadOnly = true, Idempotent = true, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> RenderWaveformJson(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("SharePoint, OneDrive, or HTTPS URL of the source audio file.")] string fileUrl,
        [Description("Number of waveform points. Default 1000.")] int points = 1000,
        [Description("Optional start time in seconds.")] double startSeconds = 0,
        [Description("Optional duration in seconds. 0 renders to end.")] double durationSeconds = 0,
        [Description("Upload the JSON as an artifact instead of returning only structured content.")] bool upload = false,
        [Description("Optional output filename when upload=true.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            using var input = await NAudioShared.DownloadAudioAsync(serviceProvider, requestContext, fileUrl, cancellationToken);
            using var reader = NAudioShared.OpenAudioFile(input.TempPath);
            var waveform = NAudioShared.BuildWaveform(reader, points, startSeconds, durationSeconds > 0 ? durationSeconds : null);
            var payload = new
            {
                source = NAudioShared.GetMetadata(reader, input),
                startSeconds,
                durationSeconds = durationSeconds > 0 ? durationSeconds : reader.TotalTime.TotalSeconds - startSeconds,
                points = waveform
            };

            return upload
                ? await NAudioShared.UploadJsonAsync(serviceProvider, requestContext, filename, payload, cancellationToken)
                : new CallToolResult { StructuredContent = JsonSerializer.SerializeToElement(payload, NAudioShared.JsonOptions) };
        });

    [Description("Render a simple frequency spectrum PNG preview for an audio file URL.")]
    [McpServerTool(Title = "Render spectrum PNG", Name = "naudio_render_spectrum_png", ReadOnly = false, Idempotent = true, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> RenderSpectrumPng(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("SharePoint, OneDrive, or HTTPS URL of the source audio file.")] string fileUrl,
        [Description("Image width in pixels. Default 1200.")] int width = 1200,
        [Description("Image height in pixels. Default 300.")] int height = 300,
        [Description("FFT size. Rounded to next power of 2 and clamped to 128..8192.")] int fftSize = 2048,
        [Description("Start time in seconds for spectrum window.")] double startSeconds = 0,
        [Description("Analysis duration in seconds. Default 5.")] double durationSeconds = 5,
        [Description("Optional output filename.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            width = Math.Clamp(width, 128, 4096);
            height = Math.Clamp(height, 64, 2048);
            using var input = await NAudioShared.DownloadAudioAsync(serviceProvider, requestContext, fileUrl, cancellationToken);
            using var reader = NAudioShared.OpenAudioFile(input.TempPath);
            var spectrum = NAudioShared.BuildSpectrum(reader, fftSize, startSeconds, durationSeconds);
            var bytes = RenderSpectrumImage(spectrum, width, height, Color.ParseHex("#101820"), Color.ParseHex("#F2AA4C"));
            var payload = new
            {
                source = NAudioShared.GetMetadata(reader, input),
                width,
                height,
                fftSize,
                startSeconds,
                durationSeconds,
                binCount = spectrum.Count
            };
            return await NAudioShared.UploadResultAsync(serviceProvider, requestContext, filename, "png", bytes, payload, cancellationToken);
        });

    [Description("Render frequency spectrum bins as JSON for audio analysis and custom visualizations.")]
    [McpServerTool(Title = "Render spectrum JSON", Name = "naudio_render_spectrum_json", ReadOnly = true, Idempotent = true, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> RenderSpectrumJson(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("SharePoint, OneDrive, or HTTPS URL of the source audio file.")] string fileUrl,
        [Description("FFT size. Rounded to next power of 2 and clamped to 128..8192.")] int fftSize = 2048,
        [Description("Start time in seconds for spectrum window.")] double startSeconds = 0,
        [Description("Analysis duration in seconds. Default 5.")] double durationSeconds = 5,
        [Description("Upload the JSON as an artifact instead of returning only structured content.")] bool upload = false,
        [Description("Optional output filename when upload=true.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            using var input = await NAudioShared.DownloadAudioAsync(serviceProvider, requestContext, fileUrl, cancellationToken);
            using var reader = NAudioShared.OpenAudioFile(input.TempPath);
            var bins = NAudioShared.BuildSpectrum(reader, fftSize, startSeconds, durationSeconds);
            var payload = new
            {
                source = NAudioShared.GetMetadata(reader, input),
                fftSize,
                startSeconds,
                durationSeconds,
                bins
            };

            return upload
                ? await NAudioShared.UploadJsonAsync(serviceProvider, requestContext, filename, payload, cancellationToken)
                : new CallToolResult { StructuredContent = JsonSerializer.SerializeToElement(payload, NAudioShared.JsonOptions) };
        });

    [Description("Generate a short normalized audio preview clip from a file URL and upload it.")]
    [McpServerTool(Title = "Generate audio preview", Name = "naudio_render_generate_audio_preview", ReadOnly = false, Idempotent = true, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> GenerateAudioPreview(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("SharePoint, OneDrive, or HTTPS URL of the source audio file.")] string fileUrl,
        [Description("Preview start time in seconds.")] double startSeconds = 0,
        [Description("Preview duration in seconds. Default 30.")] double durationSeconds = 30,
        [Description("Output format: mp3, wav, aac, mp4, wma, flac. Default mp3.")] string outputFormat = "mp3",
        [Description("Optional output filename.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var format = NAudioShared.NormalizeAudioFormat(outputFormat, "mp3");
            using var input = await NAudioShared.DownloadAudioAsync(serviceProvider, requestContext, fileUrl, cancellationToken);
            using var reader = NAudioShared.OpenAudioFile(input.TempPath);
            var bytes = NAudioShared.RenderAudio(reader, new RenderOptions(format, null, null, 128000, 16, 1f, true, Math.Max(0, startSeconds), Math.Clamp(durationSeconds, 1, 300), 0.25, 0.5));
            var payload = new
            {
                source = NAudioShared.GetMetadata(reader, input),
                preview = new
                {
                    startSeconds,
                    durationSeconds = Math.Clamp(durationSeconds, 1, 300),
                    format,
                    sizeBytes = bytes.LongLength,
                    normalized = true
                }
            };
            return await NAudioShared.UploadResultAsync(serviceProvider, requestContext, filename, format == "aac" ? "m4a" : format, bytes, payload, cancellationToken);
        });

    private static byte[] RenderWaveformImage(IReadOnlyList<WaveformPoint> points, int width, int height, Color background, Color foreground)
    {
        using var image = new Image<Rgba32>(width, height, background);
        var center = height / 2;
        var scale = height * 0.48f;
        image.ProcessPixelRows(accessor =>
        {
            for (var x = 0; x < width && x < points.Count; x++)
            {
                var p = points[x];
                var y1 = Math.Clamp(center - (int)(p.Max * scale), 0, height - 1);
                var y2 = Math.Clamp(center - (int)(p.Min * scale), 0, height - 1);
                if (y2 < y1) (y1, y2) = (y2, y1);
                for (var y = y1; y <= y2; y++)
                    accessor.GetRowSpan(y)[x] = foreground;
            }
        });

        using var stream = new MemoryStream();
        image.Save(stream, new PngEncoder());
        return stream.ToArray();
    }

    private static byte[] RenderSpectrumImage(IReadOnlyList<SpectrumBin> bins, int width, int height, Color background, Color foreground)
    {
        using var image = new Image<Rgba32>(width, height, background);
        var maxDb = bins.Count == 0 ? 0 : bins.Max(b => b.Decibels);
        var minDb = Math.Max(-120, bins.Count == 0 ? -120 : bins.Min(b => b.Decibels));
        image.ProcessPixelRows(accessor =>
        {
            for (var x = 0; x < width; x++)
            {
                var index = (int)((double)x / width * bins.Count);
                if (index < 0 || index >= bins.Count) continue;
                var normalized = (bins[index].Decibels - minDb) / Math.Max(1, maxDb - minDb);
                var barHeight = Math.Clamp((int)(normalized * (height - 1)), 0, height - 1);
                for (var y = height - 1; y >= height - 1 - barHeight; y--)
                    accessor.GetRowSpan(y)[x] = foreground;
            }
        });

        using var stream = new MemoryStream();
        image.Save(stream, new PngEncoder());
        return stream.ToArray();
    }

    private static Color ParseColor(string value)
    {
        try
        {
            return Color.ParseHex(value);
        }
        catch
        {
            return Color.Black;
        }
    }
}
