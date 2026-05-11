using System.ComponentModel;
using System.Text.Json;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.GitHub.NAudio;

public static class NAudioInspect
{
    [Description("Inspect an audio file from a SharePoint, OneDrive, or HTTPS URL and return metadata, format details, levels, clipping and silence hints.")]
    [McpServerTool(Title = "Inspect audio file", Name = "naudio_inspect_audio_file", ReadOnly = true, Idempotent = true, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> InspectAudioFile(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("SharePoint, OneDrive, or HTTPS URL of the audio file. Raw bytes/base64 are intentionally not supported.")] string fileUrl,
        [Description("Silence threshold in dBFS for silence detection. Default: -45.")] float silenceThresholdDb = -45f,
        [Description("Minimum silence duration in seconds. Default: 0.75.")] double minimumSilenceSeconds = 0.75,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
           await requestContext.WithStructuredContent(async () =>
        {
            using var input = await NAudioShared.DownloadAudioAsync(serviceProvider, requestContext, fileUrl, cancellationToken);
            using var reader = NAudioShared.OpenAudioFile(input.TempPath);
            var metadata = NAudioShared.GetMetadata(reader, input);
            var peaks = NAudioShared.AnalyzePeaks(reader);
            var silences = NAudioShared.DetectSilence(reader, silenceThresholdDb, minimumSilenceSeconds);

            return new
            {
                metadata,
                waveFormat = new
                {
                    metadata.SampleRate,
                    metadata.Channels,
                    metadata.BitsPerSample,
                    metadata.Encoding,
                    metadata.BlockAlign,
                    metadata.AverageBytesPerSecond
                },
                levels = peaks,
                clipping = new
                {
                    detected = peaks.ClippedSamples > 0,
                    peaks.ClippedSamples,
                    recommendation = peaks.ClippedSamples > 0 ? "Normalize or reduce gain before transcription or publishing." : "No sample-level clipping detected."
                },
                silence = new
                {
                    thresholdDb = silenceThresholdDb,
                    minimumSilenceSeconds,
                    regions = silences,
                    totalSilenceSeconds = Math.Round(silences.Sum(s => s.DurationSeconds), 4),
                    regionCount = silences.Count
                },
                recommendations = BuildRecommendations(metadata, peaks, silences)
            };
        }));

    [Description("Return core audio metadata only: duration, file size, mime type, sample rate, channels, bit depth and encoding.")]
    [McpServerTool(Title = "Get audio metadata", Name = "naudio_inspect_get_audio_metadata", ReadOnly = true, Idempotent = true, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> GetAudioMetadata(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("SharePoint, OneDrive, or HTTPS URL of the audio file.")] string fileUrl,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
           await requestContext.WithStructuredContent(async () =>
        {
            using var input = await NAudioShared.DownloadAudioAsync(serviceProvider, requestContext, fileUrl, cancellationToken);
            using var reader = NAudioShared.OpenAudioFile(input.TempPath);
            return NAudioShared.GetMetadata(reader, input);
        }));

    [Description("Return only the NAudio wave format details for an audio file URL.")]
    [McpServerTool(Title = "Get wave format", Name = "naudio_inspect_get_wave_format", ReadOnly = true, Idempotent = true, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> GetWaveFormat(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("SharePoint, OneDrive, or HTTPS URL of the audio file.")] string fileUrl,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
           await requestContext.WithStructuredContent(async () =>
        {
            using var input = await NAudioShared.DownloadAudioAsync(serviceProvider, requestContext, fileUrl, cancellationToken);
            using var reader = NAudioShared.OpenAudioFile(input.TempPath);
            var format = reader.WaveFormat;
            return new
            {
                format.SampleRate,
                format.Channels,
                format.BitsPerSample,
                encoding = format.Encoding.ToString(),
                format.BlockAlign,
                format.AverageBytesPerSecond,
                durationSeconds = reader.TotalTime.TotalSeconds,
                duration = reader.TotalTime.ToString("c")
            };
        }));

    [Description("Get duration for an audio file URL.")]
    [McpServerTool(Title = "Get audio duration", Name = "naudio_inspect_get_duration", ReadOnly = true, Idempotent = true, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> GetDuration(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("SharePoint, OneDrive, or HTTPS URL of the audio file.")] string fileUrl,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
           await requestContext.WithStructuredContent(async () =>
        {
            using var input = await NAudioShared.DownloadAudioAsync(serviceProvider, requestContext, fileUrl, cancellationToken);
            using var reader = NAudioShared.OpenAudioFile(input.TempPath);
            return new
            {
                durationSeconds = Math.Round(reader.TotalTime.TotalSeconds, 4),
                duration = reader.TotalTime.ToString("c"),
                estimatedMinutes = Math.Round(reader.TotalTime.TotalMinutes, 4)
            };
        }));

    [Description("Detect container/codec-ish audio format from URL filename, MIME type, and NAudio decoded wave format.")]
    [McpServerTool(Title = "Detect audio format", Name = "naudio_inspect_detect_audio_format", ReadOnly = true, Idempotent = true, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> DetectAudioFormat(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("SharePoint, OneDrive, or HTTPS URL of the audio file.")] string fileUrl,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
           await requestContext.WithStructuredContent(async () =>
        {
            using var input = await NAudioShared.DownloadAudioAsync(serviceProvider, requestContext, fileUrl, cancellationToken);
            using var reader = NAudioShared.OpenAudioFile(input.TempPath);
            return new
            {
                containerExtension = input.Extension.TrimStart('.'),
                input.File.MimeType,
                input.File.Filename,
                decodedEncoding = reader.WaveFormat.Encoding.ToString(),
                decodedFormat = new
                {
                    reader.WaveFormat.SampleRate,
                    reader.WaveFormat.Channels,
                    reader.WaveFormat.BitsPerSample
                },
                mediaFoundationBacked = !input.Extension.Equals(".wav", StringComparison.OrdinalIgnoreCase)
            };
        }));

    [Description("Analyze peak and RMS levels by channel and report dBFS peak for normalization decisions.")]
    [McpServerTool(Title = "Get audio peak levels", Name = "naudio_inspect_get_peak_levels", ReadOnly = true, Idempotent = true, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> GetPeakLevels(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("SharePoint, OneDrive, or HTTPS URL of the audio file.")] string fileUrl,
        [Description("Optional start time in seconds for partial analysis.")] double startSeconds = 0,
        [Description("Optional duration in seconds for partial analysis. 0 means to the end.")] double durationSeconds = 0,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
           await requestContext.WithStructuredContent(async () =>
        {
            using var input = await NAudioShared.DownloadAudioAsync(serviceProvider, requestContext, fileUrl, cancellationToken);
            using var reader = NAudioShared.OpenAudioFile(input.TempPath);
            return NAudioShared.AnalyzePeaks(
                reader,
                start: TimeSpan.FromSeconds(Math.Max(0, startSeconds)),
                duration: durationSeconds > 0 ? TimeSpan.FromSeconds(durationSeconds) : null);
        }));

    [Description("Detect sample-level clipping in an audio file URL.")]
    [McpServerTool(Title = "Detect clipping", Name = "naudio_inspect_detect_clipping", ReadOnly = true, Idempotent = true, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> DetectClipping(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("SharePoint, OneDrive, or HTTPS URL of the audio file.")] string fileUrl,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
           await requestContext.WithStructuredContent(async () =>
        {
            using var input = await NAudioShared.DownloadAudioAsync(serviceProvider, requestContext, fileUrl, cancellationToken);
            using var reader = NAudioShared.OpenAudioFile(input.TempPath);
            var peaks = NAudioShared.AnalyzePeaks(reader);
            return new
            {
                detected = peaks.ClippedSamples > 0,
                peaks.ClippedSamples,
                peaks.PeakAbsolute,
                peaks.PeakDb,
                recommendation = peaks.ClippedSamples > 0 ? "Use naudio_transform_normalize_audio or convert with normalize=true." : "No clipping detected."
            };
        }));

    [Description("Detect silence regions useful for trimming, splitting and transcription chunking.")]
    [McpServerTool(Title = "Detect silence regions", Name = "naudio_inspect_detect_silence_regions", ReadOnly = true, Idempotent = true, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> DetectSilenceRegions(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("SharePoint, OneDrive, or HTTPS URL of the audio file.")] string fileUrl,
        [Description("Silence threshold in dBFS. Default: -45.")] float thresholdDb = -45f,
        [Description("Minimum silence duration in seconds. Default: 0.75.")] double minimumSilenceSeconds = 0.75,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
           await requestContext.WithStructuredContent(async () =>
        {
            using var input = await NAudioShared.DownloadAudioAsync(serviceProvider, requestContext, fileUrl, cancellationToken);
            using var reader = NAudioShared.OpenAudioFile(input.TempPath);
            var regions = NAudioShared.DetectSilence(reader, thresholdDb, minimumSilenceSeconds);
            return new
            {
                thresholdDb,
                minimumSilenceSeconds,
                regions,
                regionCount = regions.Count,
                totalSilenceSeconds = Math.Round(regions.Sum(r => r.DurationSeconds), 4),
                durationSeconds = Math.Round(reader.TotalTime.TotalSeconds, 4)
            };
        }));

    [Description("Create a JSON inspection report artifact for an audio file URL and upload it to SharePoint/OneDrive.")]
    [McpServerTool(Title = "Create audio inspection report", Name = "naudio_inspect_create_report", ReadOnly = true, Idempotent = true, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> CreateInspectionReport(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("SharePoint, OneDrive, or HTTPS URL of the audio file.")] string fileUrl,
        [Description("Optional output filename for the JSON report.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            using var input = await NAudioShared.DownloadAudioAsync(serviceProvider, requestContext, fileUrl, cancellationToken);
            using var reader = NAudioShared.OpenAudioFile(input.TempPath);
            var metadata = NAudioShared.GetMetadata(reader, input);
            var peaks = NAudioShared.AnalyzePeaks(reader);
            var silences = NAudioShared.DetectSilence(reader, -45f, 0.75);
            var report = new
            {
                metadata,
                peaks,
                silences,
                recommendations = BuildRecommendations(metadata, peaks, silences),
                createdUtc = DateTimeOffset.UtcNow
            };

            return await NAudioShared.UploadJsonAsync(serviceProvider, requestContext, filename, report, cancellationToken);
        });

    private static object BuildRecommendations(AudioMetadata metadata, PeakAnalysis peaks, IReadOnlyList<SilenceRegion> silences)
        => new
        {
            transcription = new
            {
                recommendedSampleRate = metadata.SampleRate >= 16000 ? 16000 : metadata.SampleRate,
                recommendedChannels = 1,
                chunkingRecommended = metadata.DurationSeconds > 900,
                suggestedChunkSeconds = metadata.DurationSeconds > 3600 ? 600 : 900,
                splitBySilenceUseful = silences.Count > 0,
                preprocess = metadata.Channels > 1 || peaks.PeakAbsolute < 0.2 || peaks.ClippedSamples > 0
            },
            transform = new[]
            {
                metadata.Channels > 1 ? "Convert to mono for speech/transcription pipelines." : null,
                metadata.SampleRate > 16000 ? "Resample to 16 kHz for speech-focused models if smaller files are preferred." : null,
                peaks.PeakAbsolute < 0.2 ? "Normalize audio; peak level is low." : null,
                peaks.ClippedSamples > 0 ? "Reduce gain or normalize to avoid clipped speech." : null
            }.Where(x => x is not null).ToArray()
        };
}
