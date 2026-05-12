using System.ComponentModel;
using System.Text.Json;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.GitHub.NAudio;

public static class NAudioRender
{
   

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
}
