using System.ComponentModel;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.GitHub.NAudio;

public static class NAudioSplit
{
    [Description("Split an audio file URL into fixed-duration chunks and upload all chunks plus a JSON manifest.")]
    [McpServerTool(Title = "Split by duration", Name = "naudio_split_by_duration", ReadOnly = false, Idempotent = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> SplitByDuration(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("SharePoint, OneDrive, or HTTPS URL of the source audio file.")] string fileUrl,
        [Description("Chunk duration in seconds. Default 900 (15 minutes).")]
        double segmentSeconds = 900,
        [Description("Overlap between chunks in seconds. Default 0.")] double overlapSeconds = 0,
        [Description("Output format for chunks: wav, mp3, aac, mp4, wma, flac.")] string outputFormat = "wav",
        [Description("Target sample rate for chunks. 0 keeps source rate.")] int sampleRate = 0,
        [Description("Target channel count. 0 keeps source channels, 1 mono, 2 stereo.")] int channels = 0,
        [Description("Normalize each chunk.")] bool normalize = false,
        [Description("Optional base filename for chunks and manifest.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            using var input = await NAudioShared.DownloadAudioAsync(serviceProvider, requestContext, fileUrl, cancellationToken);
            using var reader = NAudioShared.OpenAudioFile(input.TempPath);
            var segments = NAudioShared.SplitByDuration(reader, segmentSeconds, overlapSeconds);
            return await UploadSegments(serviceProvider, requestContext, input, segments, outputFormat, sampleRate, channels, normalize, filename, "duration", cancellationToken);
        });

    [Description("Split an audio file URL at detected silence regions and upload chunks plus a JSON manifest.")]
    [McpServerTool(Title = "Split by silence", Name = "naudio_split_by_silence", ReadOnly = false, Idempotent = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> SplitBySilence(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("SharePoint, OneDrive, or HTTPS URL of the source audio file.")] string fileUrl,
        [Description("Silence threshold in dBFS. Default -45.")] float silenceThresholdDb = -45f,
        [Description("Minimum silence duration in seconds. Default 0.75.")] double minimumSilenceSeconds = 0.75,
        [Description("Minimum chunk duration in seconds. Default 15.")] double minimumSegmentSeconds = 15,
        [Description("Maximum chunk duration in seconds. Default 900.")] double maximumSegmentSeconds = 900,
        [Description("Output format for chunks: wav, mp3, aac, mp4, wma, flac.")] string outputFormat = "wav",
        [Description("Target sample rate for chunks. 0 keeps source rate.")] int sampleRate = 0,
        [Description("Target channel count. 0 keeps source channels, 1 mono, 2 stereo.")] int channels = 0,
        [Description("Normalize each chunk.")] bool normalize = false,
        [Description("Optional base filename for chunks and manifest.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            using var input = await NAudioShared.DownloadAudioAsync(serviceProvider, requestContext, fileUrl, cancellationToken);
            using var reader = NAudioShared.OpenAudioFile(input.TempPath);
            var segments = NAudioShared.SplitBySilence(reader, silenceThresholdDb, minimumSilenceSeconds, minimumSegmentSeconds, maximumSegmentSeconds);
            return await UploadSegments(serviceProvider, requestContext, input, segments, outputFormat, sampleRate, channels, normalize, filename, "silence", cancellationToken);
        });

    [Description("Split audio into transcription-ready chunks: mono, normalized, 16 kHz by default, silence-aware where possible.")]
    [McpServerTool(Title = "Split for transcription", Name = "naudio_split_for_transcription", ReadOnly = false, Idempotent = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> SplitForTranscription(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("SharePoint, OneDrive, or HTTPS URL of the source audio file.")] string fileUrl,
        [Description("Preferred maximum chunk length in seconds. Default 900.")] double maximumSegmentSeconds = 900,
        [Description("Minimum chunk length in seconds when splitting on silence. Default 30.")] double minimumSegmentSeconds = 30,
        [Description("Silence threshold in dBFS. Default -45.")] float silenceThresholdDb = -45f,
        [Description("Minimum silence duration in seconds. Default 0.75.")] double minimumSilenceSeconds = 0.75,
        [Description("Target transcription sample rate. Default 16000.")] int sampleRate = 16000,
        [Description("Output format: wav or mp3 are recommended. Default wav.")] string outputFormat = "wav",
        [Description("Optional base filename for chunks and manifest.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            using var input = await NAudioShared.DownloadAudioAsync(serviceProvider, requestContext, fileUrl, cancellationToken);
            using var reader = NAudioShared.OpenAudioFile(input.TempPath);
            var segments = NAudioShared.SplitBySilence(reader, silenceThresholdDb, minimumSilenceSeconds, minimumSegmentSeconds, maximumSegmentSeconds);
            if (segments.Count <= 1 && reader.TotalTime.TotalSeconds > maximumSegmentSeconds)
                segments = NAudioShared.SplitByDuration(reader, maximumSegmentSeconds, 2);

            return await UploadSegments(serviceProvider, requestContext, input, segments, outputFormat, sampleRate, 1, true, filename, "transcription", cancellationToken);
        });

    [Description("Extract a single segment from an audio file URL and upload it as a new audio artifact.")]
    [McpServerTool(Title = "Extract segment", Name = "naudio_split_extract_segment", ReadOnly = false, Idempotent = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> ExtractSegment(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("SharePoint, OneDrive, or HTTPS URL of the source audio file.")] string fileUrl,
        [Description("Start offset in seconds.")] double startSeconds,
        [Description("Duration in seconds.")] double durationSeconds,
        [Description("Output format: wav, mp3, aac, mp4, wma, flac.")] string outputFormat = "wav",
        [Description("Optional output filename.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            using var input = await NAudioShared.DownloadAudioAsync(serviceProvider, requestContext, fileUrl, cancellationToken);
            using var reader = NAudioShared.OpenAudioFile(input.TempPath);
            var format = NAudioShared.NormalizeAudioFormat(outputFormat);
            var segment = new SegmentSpec(1, Math.Max(0, startSeconds), Math.Max(0.05, durationSeconds));
            var bytes = NAudioShared.RenderSegment(reader, segment, format, 0, 0, 192000, 16, false);
            var payload = new
            {
                source = NAudioShared.GetMetadata(reader, input),
                segment,
                output = new { format, sizeBytes = bytes.LongLength }
            };
            return await NAudioShared.UploadResultAsync(serviceProvider, requestContext, filename, format == "aac" ? "m4a" : format, bytes, payload, cancellationToken);
        });

    [Description("Merge multiple audio file URLs into one output file. Inputs are decoded and concatenated in the provided order.")]
    [McpServerTool(Title = "Merge segments", Name = "naudio_split_merge_segments", ReadOnly = false, Idempotent = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> MergeSegments(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Ordered SharePoint, OneDrive, or HTTPS URLs to audio segment files.")] List<string> fileUrls,
        [Description("Output format: wav, mp3, aac, mp4, wma, flac.")] string outputFormat = "wav",
        [Description("Target sample rate. Default 16000.")] int sampleRate = 16000,
        [Description("Target channels. Default 1 mono.")] int channels = 1,
        [Description("Optional output filename.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            if (fileUrls is null || fileUrls.Count == 0)
                throw new ArgumentException("Provide at least one audio segment URL.", nameof(fileUrls));

            var tempInputs = new List<DownloadedAudio>();
            var tempWavs = new List<string>();
            try
            {
                foreach (var url in fileUrls)
                {
                    var input = await NAudioShared.DownloadAudioAsync(serviceProvider, requestContext, url, cancellationToken);
                    tempInputs.Add(input);
                    using var reader = NAudioShared.OpenAudioFile(input.TempPath);
                    var wavBytes = NAudioShared.RenderAudio(reader, new RenderOptions("wav", sampleRate, channels, 192000, 16, 1f, false, 0, 0, 0, 0));
                    var wavPath = Path.Combine(Path.GetTempPath(), $"mcphappey-naudio-merge-{Guid.NewGuid():N}.wav");
                    await File.WriteAllBytesAsync(wavPath, wavBytes, cancellationToken);
                    tempWavs.Add(wavPath);
                }

                var mergedPath = Path.Combine(Path.GetTempPath(), $"mcphappey-naudio-merged-{Guid.NewGuid():N}.wav");
                ConcatenateWavs(tempWavs, mergedPath);
                using var mergedReader = NAudioShared.OpenAudioFile(mergedPath);
                var format = NAudioShared.NormalizeAudioFormat(outputFormat);
                var outputBytes = NAudioShared.RenderAudio(mergedReader, new RenderOptions(format, sampleRate, channels, 192000, 16, 1f, false, 0, 0, 0, 0));
                NAudioShared.TryDelete(mergedPath);

                var payload = new
                {
                    inputCount = fileUrls.Count,
                    inputUrls = fileUrls,
                    output = new
                    {
                        format,
                        sampleRate,
                        channels,
                        sizeBytes = outputBytes.LongLength
                    }
                };

                return await NAudioShared.UploadResultAsync(serviceProvider, requestContext, filename, format == "aac" ? "m4a" : format, outputBytes, payload, cancellationToken);
            }
            finally
            {
                foreach (var input in tempInputs) input.Dispose();
                foreach (var wav in tempWavs) NAudioShared.TryDelete(wav);
            }
        });

    [Description("Create and upload a chunk manifest JSON without uploading audio chunks. Useful for planning transcription jobs.")]
    [McpServerTool(Title = "Create chunk manifest", Name = "naudio_split_create_chunk_manifest", ReadOnly = true, Idempotent = true, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> CreateChunkManifest(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("SharePoint, OneDrive, or HTTPS URL of the source audio file.")] string fileUrl,
        [Description("Chunking strategy: duration, silence, transcription.")] string strategy = "transcription",
        [Description("Segment seconds for duration strategy or maximum seconds for silence/transcription. Default 900.")] double segmentSeconds = 900,
        [Description("Silence threshold in dBFS for silence/transcription strategies. Default -45.")] float silenceThresholdDb = -45f,
        [Description("Minimum silence duration in seconds. Default 0.75.")] double minimumSilenceSeconds = 0.75,
        [Description("Optional output filename for JSON manifest.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            using var input = await NAudioShared.DownloadAudioAsync(serviceProvider, requestContext, fileUrl, cancellationToken);
            using var reader = NAudioShared.OpenAudioFile(input.TempPath);
            var normalizedStrategy = strategy.Trim().ToLowerInvariant();
            var segments = normalizedStrategy switch
            {
                "duration" => NAudioShared.SplitByDuration(reader, segmentSeconds),
                "silence" => NAudioShared.SplitBySilence(reader, silenceThresholdDb, minimumSilenceSeconds, 15, segmentSeconds),
                _ => NAudioShared.SplitBySilence(reader, silenceThresholdDb, minimumSilenceSeconds, 30, segmentSeconds)
            };

            var manifest = new
            {
                source = NAudioShared.GetMetadata(reader, input),
                strategy = normalizedStrategy,
                segmentCount = segments.Count,
                totalSegmentSeconds = Math.Round(segments.Sum(s => s.DurationSeconds), 4),
                segments,
                createdUtc = DateTimeOffset.UtcNow
            };

            return await NAudioShared.UploadJsonAsync(serviceProvider, requestContext, filename, manifest, cancellationToken);
        });

    private static async Task<CallToolResult> UploadSegments(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        DownloadedAudio input,
        IReadOnlyList<SegmentSpec> segments,
        string outputFormat,
        int sampleRate,
        int channels,
        bool normalize,
        string? filename,
        string strategy,
        CancellationToken cancellationToken)
    {
        var format = NAudioShared.NormalizeAudioFormat(outputFormat);
        var links = new List<ResourceLinkBlock>();
        var chunkItems = new List<object>();
        var baseName = string.IsNullOrWhiteSpace(filename) ? "audio-chunk" : Path.GetFileNameWithoutExtension(filename);

        foreach (var segment in segments)
        {
            using var segmentReader = NAudioShared.OpenAudioFile(input.TempPath);
            var bytes = NAudioShared.RenderSegment(segmentReader, segment, format, sampleRate > 0 ? sampleRate : 0, channels > 0 ? channels : 0, 192000, 16, normalize);
            var chunkName = $"{baseName}-{segment.Index:000}";
            var uploaded = await NAudioShared.UploadFileAsync(serviceProvider, requestContext, chunkName, format == "aac" ? "m4a" : format, bytes, cancellationToken);
            links.Add(uploaded);
            chunkItems.Add(new
            {
                segment.Index,
                segment.StartSeconds,
                segment.EndSeconds,
                segment.DurationSeconds,
                file = uploaded.Uri,
                uploaded.Name,
                uploaded.MimeType,
                sizeBytes = bytes.LongLength
            });
        }

        using var metadataReader = NAudioShared.OpenAudioFile(input.TempPath);
        var manifest = new
        {
            source = NAudioShared.GetMetadata(metadataReader, input),
            strategy,
            output = new
            {
                format,
                sampleRate,
                channels,
                normalize
            },
            segmentCount = segments.Count,
            chunks = chunkItems,
            createdUtc = DateTimeOffset.UtcNow
        };

        var manifestBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(manifest, NAudioShared.JsonOptions);
        var manifestLink = await NAudioShared.UploadFileAsync(serviceProvider, requestContext, $"{baseName}-manifest", "json", manifestBytes, cancellationToken);
        links.Add(manifestLink);

        return new CallToolResult
        {
            StructuredContent = System.Text.Json.JsonSerializer.SerializeToElement(manifest, NAudioShared.JsonOptions),
            Content = [.. links]
        };
    }

    private static void ConcatenateWavs(IReadOnlyList<string> sourceFiles, string outputPath)
    {
        if (sourceFiles.Count == 0)
            throw new ArgumentException("No source files to concatenate.", nameof(sourceFiles));

        global::NAudio.Wave.WaveFileWriter? writer = null;
        try
        {
            foreach (var sourceFile in sourceFiles)
            {
                using var reader = new global::NAudio.Wave.WaveFileReader(sourceFile);
                writer ??= new global::NAudio.Wave.WaveFileWriter(outputPath, reader.WaveFormat);
                if (!reader.WaveFormat.Equals(writer.WaveFormat))
                    throw new InvalidOperationException("All WAV segments must have the same wave format after preprocessing.");
                reader.CopyTo(writer);
            }
        }
        finally
        {
            writer?.Dispose();
        }
    }
}
