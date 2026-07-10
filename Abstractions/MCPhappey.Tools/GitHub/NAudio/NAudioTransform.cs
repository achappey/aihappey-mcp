using System.ComponentModel;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.GitHub.NAudio;

public static class NAudioTransform
{
    [Description("Convert an audio file URL to WAV, MP3, AAC/MP4, WMA or FLAC and upload the converted file to SharePoint/OneDrive.")]
    [McpServerTool(Title = "Convert audio", Name = "naudio_transform_convert_audio", ReadOnly = false, Idempotent = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> ConvertAudio(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("SharePoint, OneDrive, or HTTPS URL of the source audio file.")] string fileUrl,
        [Description("Output format: wav, mp3, aac, mp4, wma, flac.")] string outputFormat = "wav",
        [Description("Optional output sample rate, e.g. 16000, 44100, 48000. 0 keeps source rate.")] int sampleRate = 0,
        [Description("Optional output channels: 1 mono or 2 stereo. 0 keeps source channels.")] int channels = 0,
        [Description("Output bitrate for lossy encoders in bits/sec. Default 192000.")] int bitrate = 192000,
        [Description("WAV bit depth: 16 or 32. Default 16.")] int bitDepth = 16,
        [Description("Apply peak normalization before writing output.")] bool normalize = false,
        [Description("Linear gain multiplier after normalization, between 0 and 10. Default 1.")] float volume = 1f,
        [Description("Optional output filename. Extension is added automatically.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await RenderAndUpload(serviceProvider, requestContext, fileUrl, outputFormat, sampleRate, channels, bitrate, bitDepth, normalize, volume, 0, 0, 0, 0, filename, cancellationToken);

    [Description("Convert an audio file URL to a 16-bit or 32-bit WAV file.")]
    [McpServerTool(Title = "Convert to WAV", Name = "naudio_transform_convert_to_wav", ReadOnly = false, Idempotent = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> ConvertToWav(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("SharePoint, OneDrive, or HTTPS URL of the source audio file.")] string fileUrl,
        [Description("Optional output sample rate. 0 keeps source rate.")] int sampleRate = 0,
        [Description("Optional output channels: 1 mono or 2 stereo. 0 keeps source channels.")] int channels = 0,
        [Description("WAV bit depth: 16 or 32. Default 16.")] int bitDepth = 16,
        [Description("Apply peak normalization.")] bool normalize = false,
        [Description("Optional output filename.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await RenderAndUpload(serviceProvider, requestContext, fileUrl, "wav", sampleRate, channels, 192000, bitDepth, normalize, 1f, 0, 0, 0, 0, filename, cancellationToken);

    [Description("Convert an audio file URL to MP3 using Windows Media Foundation.")]
    [McpServerTool(Title = "Convert to MP3", Name = "naudio_transform_convert_to_mp3", ReadOnly = false, Idempotent = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> ConvertToMp3(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("SharePoint, OneDrive, or HTTPS URL of the source audio file.")] string fileUrl,
        [Description("MP3 bitrate in bits/sec. Default 192000.")] int bitrate = 192000,
        [Description("Optional output sample rate. 0 keeps source rate.")] int sampleRate = 0,
        [Description("Optional output channels: 1 mono or 2 stereo. 0 keeps source channels.")] int channels = 0,
        [Description("Apply peak normalization.")] bool normalize = false,
        [Description("Optional output filename.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await RenderAndUpload(serviceProvider, requestContext, fileUrl, "mp3", sampleRate, channels, bitrate, 16, normalize, 1f, 0, 0, 0, 0, filename, cancellationToken);

    [Description("Convert an audio file URL to FLAC using Windows Media Foundation.")]
    [McpServerTool(Title = "Convert to FLAC", Name = "naudio_transform_convert_to_flac", ReadOnly = false, Idempotent = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> ConvertToFlac(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("SharePoint, OneDrive, or HTTPS URL of the source audio file.")] string fileUrl,
        [Description("Optional output sample rate. 0 keeps source rate.")] int sampleRate = 0,
        [Description("Optional output channels: 1 mono or 2 stereo. 0 keeps source channels.")] int channels = 0,
        [Description("Apply peak normalization.")] bool normalize = false,
        [Description("Optional output filename.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await RenderAndUpload(serviceProvider, requestContext, fileUrl, "flac", sampleRate, channels, 0, 16, normalize, 1f, 0, 0, 0, 0, filename, cancellationToken);

    [Description("Resample an audio file URL to a target sample rate, optionally changing channels and format.")]
    [McpServerTool(Title = "Resample audio", Name = "naudio_transform_resample_audio", ReadOnly = false, Idempotent = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> ResampleAudio(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("SharePoint, OneDrive, or HTTPS URL of the source audio file.")] string fileUrl,
        [Description("Target sample rate, e.g. 16000, 44100, 48000.")] int sampleRate,
        [Description("Output format: wav, mp3, aac, mp4, wma, flac.")] string outputFormat = "wav",
        [Description("Optional output channels: 1 mono or 2 stereo. 0 keeps source channels.")] int channels = 0,
        [Description("Optional output filename.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await RenderAndUpload(serviceProvider, requestContext, fileUrl, outputFormat, sampleRate, channels, 192000, 16, false, 1f, 0, 0, 0, 0, filename, cancellationToken);

    [Description("Change audio channel layout to mono or stereo and upload the transformed output.")]
    [McpServerTool(Title = "Change audio channels", Name = "naudio_transform_change_channels", ReadOnly = false, Idempotent = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> ChangeChannels(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("SharePoint, OneDrive, or HTTPS URL of the source audio file.")] string fileUrl,
        [Description("Target channels: 1 mono or 2 stereo.")] int channels,
        [Description("Output format: wav, mp3, aac, mp4, wma, flac.")] string outputFormat = "wav",
        [Description("Optional output filename.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await RenderAndUpload(serviceProvider, requestContext, fileUrl, outputFormat, 0, channels, 192000, 16, false, 1f, 0, 0, 0, 0, filename, cancellationToken);

    [Description("Change WAV bit depth to 16-bit PCM or 32-bit IEEE float and upload as WAV.")]
    [McpServerTool(Title = "Change bit depth", Name = "naudio_transform_change_bit_depth", ReadOnly = false, Idempotent = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> ChangeBitDepth(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("SharePoint, OneDrive, or HTTPS URL of the source audio file.")] string fileUrl,
        [Description("Target WAV bit depth: 16 or 32.")] int bitDepth = 16,
        [Description("Optional output filename.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await RenderAndUpload(serviceProvider, requestContext, fileUrl, "wav", 0, 0, 192000, bitDepth, false, 1f, 0, 0, 0, 0, filename, cancellationToken);

    [Description("Peak-normalize an audio file URL and upload the normalized output.")]
    [McpServerTool(Title = "Normalize audio", Name = "naudio_transform_normalize_audio", ReadOnly = false, Idempotent = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> NormalizeAudio(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("SharePoint, OneDrive, or HTTPS URL of the source audio file.")] string fileUrl,
        [Description("Output format: wav, mp3, aac, mp4, wma, flac.")] string outputFormat = "wav",
        [Description("Optional output filename.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await RenderAndUpload(serviceProvider, requestContext, fileUrl, outputFormat, 0, 0, 192000, 16, true, 1f, 0, 0, 0, 0, filename, cancellationToken);

    [Description("Extract a time segment from an audio file URL and upload it as a new audio file.")]
    [McpServerTool(Title = "Trim audio", Name = "naudio_transform_trim_audio", ReadOnly = false, Idempotent = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> TrimAudio(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("SharePoint, OneDrive, or HTTPS URL of the source audio file.")] string fileUrl,
        [Description("Start offset in seconds.")] double startSeconds,
        [Description("Duration in seconds. Use 0 for to-the-end.")] double durationSeconds,
        [Description("Output format: wav, mp3, aac, mp4, wma, flac.")] string outputFormat = "wav",
        [Description("Optional output filename.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await RenderAndUpload(serviceProvider, requestContext, fileUrl, outputFormat, 0, 0, 192000, 16, false, 1f, startSeconds, durationSeconds, 0, 0, filename, cancellationToken);

    [Description("Add a fade-in to an audio file URL and upload the transformed output.")]
    [McpServerTool(Title = "Add fade in", Name = "naudio_transform_add_fade_in", ReadOnly = false, Idempotent = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> AddFadeIn(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("SharePoint, OneDrive, or HTTPS URL of the source audio file.")] string fileUrl,
        [Description("Fade-in duration in seconds.")] double fadeInSeconds = 2,
        [Description("Output format: wav, mp3, aac, mp4, wma, flac.")] string outputFormat = "wav",
        [Description("Optional output filename.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await RenderAndUpload(serviceProvider, requestContext, fileUrl, outputFormat, 0, 0, 192000, 16, false, 1f, 0, 0, fadeInSeconds, 0, filename, cancellationToken);

    [Description("Add a fade-out to an audio file URL and upload the transformed output.")]
    [McpServerTool(Title = "Add fade out", Name = "naudio_transform_add_fade_out", ReadOnly = false, Idempotent = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> AddFadeOut(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("SharePoint, OneDrive, or HTTPS URL of the source audio file.")] string fileUrl,
        [Description("Fade-out duration in seconds.")] double fadeOutSeconds = 2,
        [Description("Output format: wav, mp3, aac, mp4, wma, flac.")] string outputFormat = "wav",
        [Description("Optional output filename.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await RenderAndUpload(serviceProvider, requestContext, fileUrl, outputFormat, 0, 0, 192000, 16, false, 1f, 0, 0, 0, fadeOutSeconds, filename, cancellationToken);

    [Description("Prepare audio for speech/transcription models: mono, 16 kHz, normalized WAV by default.")]
    [McpServerTool(Title = "Preprocess for transcription", Name = "naudio_transform_preprocess_for_transcription", ReadOnly = false, Idempotent = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> PreprocessForTranscription(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("SharePoint, OneDrive, or HTTPS URL of the source audio file.")] string fileUrl,
        [Description("Target speech sample rate. Default 16000.")] int sampleRate = 16000,
        [Description("Output format. Default wav; mp3 also useful for smaller uploads.")] string outputFormat = "wav",
        [Description("Optional output filename.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await RenderAndUpload(serviceProvider, requestContext, fileUrl, outputFormat, sampleRate, 1, 96000, 16, true, 1f, 0, 0, 0, 0, filename, cancellationToken);

    private static async Task<CallToolResult?> RenderAndUpload(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string fileUrl,
        string outputFormat,
        int sampleRate,
        int channels,
        int bitrate,
        int bitDepth,
        bool normalize,
        float volume,
        double trimStartSeconds,
        double durationSeconds,
        double fadeInSeconds,
        double fadeOutSeconds,
        string? filename,
        CancellationToken cancellationToken)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            var format = NAudioShared.NormalizeAudioFormat(outputFormat);
            if (channels is not 0 and not 1 and not 2)
                throw new ArgumentOutOfRangeException(nameof(channels), "channels must be 0, 1, or 2.");
            if (sampleRate < 0)
                throw new ArgumentOutOfRangeException(nameof(sampleRate), "sampleRate must be 0 or a positive sample rate.");

            using var input = await NAudioShared.DownloadAudioAsync(serviceProvider, requestContext, fileUrl, cancellationToken);
            using var reader = NAudioShared.OpenAudioFile(input.TempPath);
            var sourceMetadata = NAudioShared.GetMetadata(reader, input);
            var sourcePeaks = NAudioShared.AnalyzePeaks(reader);
            var bytes = NAudioShared.RenderAudio(reader, new RenderOptions(
                format,
                sampleRate > 0 ? sampleRate : null,
                channels > 0 ? channels : null,
                bitrate,
                bitDepth,
                volume,
                normalize,
                Math.Max(0, trimStartSeconds),
                Math.Max(0, durationSeconds),
                Math.Max(0, fadeInSeconds),
                Math.Max(0, fadeOutSeconds)));

            using var verifyPath = TempOutput(format, bytes);
            using var verifyReader = NAudioShared.OpenAudioFile(verifyPath.Path);
            var output = new
            {
                source = sourceMetadata,
                sourceLevels = sourcePeaks,
                output = new
                {
                    format,
                    sizeBytes = bytes.LongLength,
                    durationSeconds = Math.Round(verifyReader.TotalTime.TotalSeconds, 4),
                    sampleRate = verifyReader.WaveFormat.SampleRate,
                    channels = verifyReader.WaveFormat.Channels,
                    bitsPerSample = verifyReader.WaveFormat.BitsPerSample,
                    encoding = verifyReader.WaveFormat.Encoding.ToString(),
                    bitrate = format is "mp3" or "aac" or "mp4" or "wma" ? bitrate : 0,
                    bitDepth = format == "wav" ? bitDepth : 0,
                    normalize,
                    volume,
                    trimStartSeconds,
                    requestedDurationSeconds = durationSeconds,
                    fadeInSeconds,
                    fadeOutSeconds
                }
            };

            return await NAudioShared.UploadResultAsync(serviceProvider, requestContext, filename, format == "aac" ? "m4a" : format, bytes, output, cancellationToken);
        });

    private static TempFile TempOutput(string extension, byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"mcphappey-naudio-verify-{Guid.NewGuid():N}.{extension}");
        File.WriteAllBytes(path, bytes);
        return new TempFile(path);
    }

    private sealed record TempFile(string Path) : IDisposable
    {
        public void Dispose() => NAudioShared.TryDelete(Path);
    }
}
