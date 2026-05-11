using System.Net.Mime;
using System.Runtime.InteropServices;
using System.Text.Json;
using MCPhappey.Common.Extensions;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NAudio.Lame;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MCPhappey.Tools.GitHub.NAudio;

internal static class NAudioShared
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Web)
    {
        WriteIndented = true
    };

    private static readonly HashSet<string> SupportedInputExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wav", ".wave", ".mp3", ".aiff", ".aif", ".wma", ".aac", ".m4a", ".mp4", ".flac", ".webm", ".mka"
    };

    internal static async Task<DownloadedAudio> DownloadAudioAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string fileUrl,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileUrl))
            throw new ArgumentException("A SharePoint, OneDrive or HTTPS audio file URL is required.", nameof(fileUrl));

        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
        var file = files.FirstOrDefault() ?? throw new InvalidOperationException("No audio content could be downloaded from fileUrl.");

        var extension = GuessExtension(file, fileUrl);
        var path = Path.Combine(Path.GetTempPath(), $"mcphappey-naudio-{Guid.NewGuid():N}{extension}");
        await File.WriteAllBytesAsync(path, file.Contents.ToArray(), cancellationToken);

        return new DownloadedAudio(path, file, extension, file.Contents.ToArray().LongLength);
    }

    internal static AudioFileReader OpenAudioFile(string path) => new(path);

    internal static AudioMetadata GetMetadata(AudioFileReader reader, DownloadedAudio input)
    {
        var format = reader.WaveFormat;
        return new AudioMetadata(
            input.File.Filename,
            input.File.Uri,
            input.File.MimeType,
            input.Extension.TrimStart('.'),
            input.SizeBytes,
            reader.TotalTime.TotalSeconds,
            reader.TotalTime.ToString("c"),
            format.SampleRate,
            format.Channels,
            format.BitsPerSample,
            format.Encoding.ToString(),
            format.BlockAlign,
            format.AverageBytesPerSecond);
    }

    internal static async Task<ResourceLinkBlock> UploadFileAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string? fileName,
        string extension,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        var outputName = BuildOutputName(requestContext, fileName, extension);
        return await requestContext.Server.Upload(serviceProvider, outputName, BinaryData.FromBytes(bytes), cancellationToken)
            ?? throw new InvalidOperationException("Upload returned no resource link.");
    }

    internal static async Task<CallToolResult> UploadResultAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string? fileName,
        string extension,
        byte[] bytes,
        object structuredContent,
        CancellationToken cancellationToken)
    {
        var uploaded = await UploadFileAsync(serviceProvider, requestContext, fileName, extension, bytes, cancellationToken);
        return new CallToolResult
        {
            StructuredContent = JsonSerializer.SerializeToElement(structuredContent, JsonOptions),
            Content = [uploaded]
        };
    }

    internal static async Task<CallToolResult> UploadJsonAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string? fileName,
        object payload,
        CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        return await UploadResultAsync(serviceProvider, requestContext, fileName, "json", bytes, payload, cancellationToken);
    }

    internal static string BuildOutputName(RequestContext<CallToolRequestParams> requestContext, string? fileName, string extension)
    {
        extension = NormalizeExtension(extension);
        if (string.IsNullOrWhiteSpace(fileName))
            return requestContext.ToOutputFileName(extension);

        var clean = Path.GetFileName(fileName).ToOutputFileName();
        return clean.EndsWith($".{extension}", StringComparison.OrdinalIgnoreCase) ? clean : $"{clean}.{extension}";
    }

    internal static string NormalizeExtension(string extension)
        => extension.Trim().TrimStart('.').ToLowerInvariant();

    internal static string NormalizeAudioFormat(string? format, string fallback = "wav")
    {
        var normalized = NormalizeExtension(string.IsNullOrWhiteSpace(format) ? fallback : format);
        return normalized switch
        {
            "wave" => "wav",
            "m4a" => "aac",
            "mpeg" => "mp3",
            "jpg" => "jpeg",
            _ => normalized
        };
    }

    internal static ISampleProvider BuildSampleProvider(
        AudioFileReader reader,
        int? sampleRate = null,
        int? channels = null,
        float volume = 1f,
        bool normalize = false,
        TimeSpan? trimStart = null,
        TimeSpan? duration = null,
        TimeSpan? fadeIn = null,
        TimeSpan? fadeOut = null)
    {
        reader.Position = 0;
        if (trimStart is { TotalSeconds: > 0 })
            reader.CurrentTime = trimStart.Value < reader.TotalTime ? trimStart.Value : reader.TotalTime;

        ISampleProvider provider = reader;

        if (duration is { TotalSeconds: > 0 })
            provider = provider.Take(duration.Value);

        if (channels is 1 && provider.WaveFormat.Channels == 2)
        {
            provider = new StereoToMonoSampleProvider(provider) { LeftVolume = 0.5f, RightVolume = 0.5f };
        }
        else if (channels is 2 && provider.WaveFormat.Channels == 1)
        {
            provider = new MonoToStereoSampleProvider(provider);
        }
        else if (channels is not null && provider.WaveFormat.Channels != channels.Value)
        {
            throw new NotSupportedException($"Channel conversion from {provider.WaveFormat.Channels} to {channels.Value} is not supported. Use 1 or 2 channels.");
        }

        if (sampleRate is > 0 && provider.WaveFormat.SampleRate != sampleRate.Value)
            provider = new WdlResamplingSampleProvider(provider, sampleRate.Value);

        if (normalize)
        {
            var peak = AnalyzePeaks(reader, provider.WaveFormat.SampleRate, provider.WaveFormat.Channels, trimStart, duration).PeakAbsolute;
            reader.Position = 0;
            if (trimStart is { TotalSeconds: > 0 })
                reader.CurrentTime = trimStart.Value < reader.TotalTime ? trimStart.Value : reader.TotalTime;
            provider = BuildSampleProvider(reader, sampleRate, channels, volume, false, trimStart, duration, null, null);
            if (peak > 0)
                provider = new VolumeSampleProvider(provider) { Volume = (float)Math.Min(10f, 0.98f / peak) };
        }

        if (Math.Abs(volume - 1f) > 0.0001f)
            provider = new VolumeSampleProvider(provider) { Volume = Math.Clamp(volume, 0f, 10f) };

        if ((fadeIn is { TotalSeconds: > 0 }) || (fadeOut is { TotalSeconds: > 0 }))
        {
            var fade = new FadeInOutSampleProvider(provider, true);
            if (fadeIn is { TotalMilliseconds: > 0 })
                fade.BeginFadeIn(fadeIn.Value.TotalMilliseconds);
            if (fadeOut is { TotalMilliseconds: > 0 } && duration is { TotalMilliseconds: > 0 })
            {
                var delayed = new OffsetSampleProvider(fade)
                {
                    Take = duration.Value
                };
                provider = new TailFadeSampleProvider(delayed, fadeOut.Value);
            }
            else
            {
                provider = fade;
            }
        }

        return provider;
    }

    internal static byte[] RenderAudio(AudioFileReader reader, RenderOptions options)
    {
        var provider = BuildSampleProvider(
            reader,
            options.SampleRate,
            options.Channels,
            options.Volume,
            options.Normalize,
            TimeSpan.FromSeconds(Math.Max(0, options.TrimStartSeconds)),
            options.DurationSeconds > 0 ? TimeSpan.FromSeconds(options.DurationSeconds) : null,
            options.FadeInSeconds > 0 ? TimeSpan.FromSeconds(options.FadeInSeconds) : null,
            options.FadeOutSeconds > 0 ? TimeSpan.FromSeconds(options.FadeOutSeconds) : null);

        var outputPath = Path.Combine(Path.GetTempPath(), $"mcphappey-naudio-out-{Guid.NewGuid():N}.{options.Format}");
        try
        {
            switch (options.Format)
            {
                case "wav":
                    WriteWave(outputPath, provider, options.BitDepth);
                    break;
                case "mp3":
                    WriteMp3(outputPath, provider, options.Bitrate);
                    break;
                case "aac":
                case "mp4":
                case "wma":
                case "flac":
                    throw new NotSupportedException($"Output format '{options.Format}' requires NAudio MediaFoundation/Wasapi components that are not compatible with this net10 backend. Use wav or mp3.");
                default:
                    throw new NotSupportedException($"Unsupported output format '{options.Format}'. Supported on this backend: wav, mp3.");
            }

            return File.ReadAllBytes(outputPath);
        }
        finally
        {
            TryDelete(outputPath);
        }
    }

    internal static void WriteWave(string outputPath, ISampleProvider provider, int bitDepth)
    {
        switch (bitDepth)
        {
            case 16:
                WaveFileWriter.CreateWaveFile16(outputPath, provider);
                break;
            case 32:
                WaveFileWriter.CreateWaveFile(outputPath, provider.ToWaveProvider());
                break;
            default:
                throw new NotSupportedException("WAV bitDepth supports 16 or 32 only.");
        }
    }

    internal static void WriteMp3(string outputPath, ISampleProvider provider, int bitrate)
    {
        var waveProvider = provider.ToWaveProvider16();
        using var writer = new LameMP3FileWriter(outputPath, waveProvider.WaveFormat, Math.Clamp(bitrate / 1000, 8, 320));
        var buffer = new byte[waveProvider.WaveFormat.AverageBytesPerSecond];
        int read;
        while ((read = waveProvider.Read(buffer)) > 0)
            writer.Write(buffer, 0, read);
    }

    internal static PeakAnalysis AnalyzePeaks(AudioFileReader reader, int? targetSampleRate = null, int? targetChannels = null, TimeSpan? start = null, TimeSpan? duration = null)
    {
        var provider = BuildSampleProvider(reader, targetSampleRate, targetChannels, 1f, false, start, duration);
        var channels = provider.WaveFormat.Channels;
        var peaks = new float[channels];
        var sums = new double[channels];
        var clipping = new long[channels];
        long frames = 0;
        var buffer = new float[provider.WaveFormat.SampleRate * channels];
        int read;

        while ((read = provider.Read(buffer)) > 0)
        {
            for (var n = 0; n < read; n++)
            {
                var channel = n % channels;
                var abs = Math.Abs(buffer[n]);
                if (abs > peaks[channel]) peaks[channel] = abs;
                sums[channel] += buffer[n] * buffer[n];
                if (abs >= 0.999f) clipping[channel]++;
            }

            frames += read / channels;
        }

        var rms = sums.Select(s => frames == 0 ? 0 : Math.Sqrt(s / frames)).ToArray();
        var peakAbsolute = peaks.Length == 0 ? 0 : peaks.Max();
        return new PeakAnalysis(
            peaks.Select(p => Math.Round(p, 6)).ToArray(),
            rms.Select(r => Math.Round(r, 6)).ToArray(),
            Math.Round(peakAbsolute, 6),
            Math.Round(ToDb(peakAbsolute), 2),
            clipping.Sum(),
            frames,
            channels,
            provider.WaveFormat.SampleRate);
    }

    internal static IReadOnlyList<SilenceRegion> DetectSilence(AudioFileReader reader, float thresholdDb, double minimumSilenceSeconds)
    {
        reader.Position = 0;
        var sampleRate = reader.WaveFormat.SampleRate;
        var channels = reader.WaveFormat.Channels;
        var threshold = DbToLinear(thresholdDb);
        var minFrames = (long)(minimumSilenceSeconds * sampleRate);
        var buffer = new float[Math.Max(sampleRate * channels / 10, channels * 1024)];
        var regions = new List<SilenceRegion>();
        long frame = 0;
        long? silenceStart = null;
        int read;

        while ((read = reader.Read(buffer)) > 0)
        {
            for (var i = 0; i < read; i += channels)
            {
                var max = 0f;
                for (var c = 0; c < channels && i + c < read; c++)
                    max = Math.Max(max, Math.Abs(buffer[i + c]));

                if (max <= threshold)
                {
                    silenceStart ??= frame;
                }
                else if (silenceStart.HasValue)
                {
                    AddSilenceRegion(regions, silenceStart.Value, frame, minFrames, sampleRate);
                    silenceStart = null;
                }

                frame++;
            }
        }

        if (silenceStart.HasValue)
            AddSilenceRegion(regions, silenceStart.Value, frame, minFrames, sampleRate);

        return regions;
    }

    internal static IReadOnlyList<WaveformPoint> BuildWaveform(AudioFileReader reader, int points, double startSeconds = 0, double? durationSeconds = null)
    {
        points = Math.Clamp(points, 64, 10000);
        var provider = BuildSampleProvider(reader, null, null, 1f, false, TimeSpan.FromSeconds(Math.Max(0, startSeconds)), durationSeconds is > 0 ? TimeSpan.FromSeconds(durationSeconds.Value) : null);
        var channels = provider.WaveFormat.Channels;
        var totalSeconds = durationSeconds is > 0 ? durationSeconds.Value : reader.TotalTime.TotalSeconds - startSeconds;
        var framesPerPoint = Math.Max(1, (int)Math.Ceiling(totalSeconds * provider.WaveFormat.SampleRate / points));
        var buffer = new float[framesPerPoint * channels];
        var result = new List<WaveformPoint>(points);
        var index = 0;
        int read;

        while ((read = provider.Read(buffer)) > 0 && result.Count < points)
        {
            var min = 0f;
            var max = 0f;
            double sumSquares = 0;
            for (var n = 0; n < read; n++)
            {
                min = Math.Min(min, buffer[n]);
                max = Math.Max(max, buffer[n]);
                sumSquares += buffer[n] * buffer[n];
            }

            result.Add(new WaveformPoint(
                index,
                Math.Round(startSeconds + (double)index * framesPerPoint / provider.WaveFormat.SampleRate, 4),
                Math.Round(min, 6),
                Math.Round(max, 6),
                Math.Round(Math.Sqrt(sumSquares / Math.Max(1, read)), 6)));
            index++;
        }

        return result;
    }

    internal static IReadOnlyList<SpectrumBin> BuildSpectrum(AudioFileReader reader, int fftSize, double startSeconds, double durationSeconds)
    {
        fftSize = Math.Clamp(NextPowerOfTwo(fftSize), 128, 8192);
        var provider = BuildSampleProvider(reader, null, 1, 1f, false, TimeSpan.FromSeconds(Math.Max(0, startSeconds)), TimeSpan.FromSeconds(Math.Max(0.05, durationSeconds)));
        var samples = new List<float>(fftSize * 16);
        var buffer = new float[fftSize];
        int read;
        while ((read = provider.Read(buffer)) > 0 && samples.Count < fftSize * 128)
        {
            samples.AddRange(buffer.Take(read));
        }

        if (samples.Count == 0) return [];

        var bins = fftSize / 2;
        var output = new List<SpectrumBin>(bins);
        var sampleRate = provider.WaveFormat.SampleRate;
        var window = samples.Take(fftSize).Concat(Enumerable.Repeat(0f, Math.Max(0, fftSize - samples.Count))).ToArray();

        for (var k = 0; k < bins; k++)
        {
            double real = 0;
            double imaginary = 0;
            for (var n = 0; n < fftSize; n++)
            {
                var hann = 0.5 * (1 - Math.Cos(2 * Math.PI * n / (fftSize - 1)));
                var angle = 2 * Math.PI * k * n / fftSize;
                real += window[n] * hann * Math.Cos(angle);
                imaginary -= window[n] * hann * Math.Sin(angle);
            }

            var magnitude = Math.Sqrt(real * real + imaginary * imaginary) / fftSize;
            output.Add(new SpectrumBin(k, Math.Round((double)k * sampleRate / fftSize, 2), Math.Round(magnitude, 8), Math.Round(ToDb(magnitude), 2)));
        }

        return output;
    }

    internal static List<SegmentSpec> SplitByDuration(AudioFileReader reader, double segmentSeconds, double overlapSeconds = 0)
    {
        segmentSeconds = Math.Clamp(segmentSeconds, 1, 60 * 60);
        overlapSeconds = Math.Clamp(overlapSeconds, 0, Math.Max(0, segmentSeconds - 0.1));
        var step = segmentSeconds - overlapSeconds;
        var result = new List<SegmentSpec>();
        var start = 0d;
        var index = 1;
        while (start < reader.TotalTime.TotalSeconds)
        {
            var duration = Math.Min(segmentSeconds, reader.TotalTime.TotalSeconds - start);
            result.Add(new SegmentSpec(index++, start, duration));
            start += step;
        }

        return result;
    }

    internal static List<SegmentSpec> SplitBySilence(AudioFileReader reader, float silenceThresholdDb, double minimumSilenceSeconds, double minimumSegmentSeconds, double maximumSegmentSeconds)
    {
        var silences = DetectSilence(reader, silenceThresholdDb, minimumSilenceSeconds);
        var result = new List<SegmentSpec>();
        var start = 0d;
        var index = 1;
        maximumSegmentSeconds = Math.Max(minimumSegmentSeconds, maximumSegmentSeconds);

        foreach (var silence in silences)
        {
            var cut = silence.StartSeconds + silence.DurationSeconds / 2;
            var candidateDuration = cut - start;
            if (candidateDuration >= minimumSegmentSeconds)
            {
                result.Add(new SegmentSpec(index++, start, candidateDuration));
                start = cut;
            }

            while (reader.TotalTime.TotalSeconds - start > maximumSegmentSeconds)
            {
                result.Add(new SegmentSpec(index++, start, maximumSegmentSeconds));
                start += maximumSegmentSeconds;
            }
        }

        if (reader.TotalTime.TotalSeconds - start > 0.05)
            result.Add(new SegmentSpec(index, start, reader.TotalTime.TotalSeconds - start));

        return result;
    }

    internal static byte[] RenderSegment(AudioFileReader reader, SegmentSpec segment, string format, int sampleRate, int channels, int bitrate, int bitDepth, bool normalize)
        => RenderAudio(reader, new RenderOptions(format, sampleRate, channels, bitrate, bitDepth, 1f, normalize, segment.StartSeconds, segment.DurationSeconds, 0, 0));

    internal static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // best effort temp cleanup
        }
    }

    private static string GuessExtension(FileItem file, string url)
    {
        var candidates = new[]
        {
            Path.GetExtension(file.Filename),
            Path.GetExtension(new Uri(url).AbsolutePath),
            MimeToExtension(file.MimeType)
        };

        foreach (var candidate in candidates.Where(c => !string.IsNullOrWhiteSpace(c)))
        {
            var normalized = candidate!.StartsWith('.') ? candidate : $".{candidate}";
            if (SupportedInputExtensions.Contains(normalized))
                return normalized.Equals(".wave", StringComparison.OrdinalIgnoreCase) ? ".wav" : normalized;
        }

        return ".tmp";
    }

    private static string MimeToExtension(string? mimeType)
        => mimeType?.ToLowerInvariant() switch
        {
            "audio/wav" or "audio/wave" or "audio/x-wav" => ".wav",
            "audio/mpeg" or "audio/mp3" => ".mp3",
            "audio/flac" or "audio/x-flac" => ".flac",
            "audio/aac" => ".aac",
            "audio/mp4" or "video/mp4" => ".mp4",
            "audio/x-ms-wma" => ".wma",
            "audio/aiff" or "audio/x-aiff" => ".aiff",
            "audio/webm" or "video/webm" => ".webm",
            _ => ".tmp"
        };

    private static void AddSilenceRegion(List<SilenceRegion> regions, long startFrame, long endFrame, long minFrames, int sampleRate)
    {
        var frames = endFrame - startFrame;
        if (frames < minFrames) return;
        regions.Add(new SilenceRegion(
            Math.Round((double)startFrame / sampleRate, 4),
            Math.Round((double)endFrame / sampleRate, 4),
            Math.Round((double)frames / sampleRate, 4)));
    }

    private static double ToDb(double value) => value <= 0 ? -120 : 20 * Math.Log10(value);

    private static float DbToLinear(float db) => (float)Math.Pow(10, db / 20);

    private static int NextPowerOfTwo(int value)
    {
        var power = 1;
        while (power < value) power <<= 1;
        return power;
    }
}

internal sealed record DownloadedAudio(string TempPath, FileItem File, string Extension, long SizeBytes) : IDisposable
{
    public void Dispose() => NAudioShared.TryDelete(TempPath);
}

internal sealed record AudioMetadata(
    string? FileName,
    string Uri,
    string? MimeType,
    string Extension,
    long SizeBytes,
    double DurationSeconds,
    string Duration,
    int SampleRate,
    int Channels,
    int BitsPerSample,
    string Encoding,
    int BlockAlign,
    int AverageBytesPerSecond);

internal sealed record PeakAnalysis(
    IReadOnlyList<double> ChannelPeaks,
    IReadOnlyList<double> ChannelRms,
    double PeakAbsolute,
    double PeakDb,
    long ClippedSamples,
    long FramesAnalyzed,
    int Channels,
    int SampleRate);

internal sealed record SilenceRegion(double StartSeconds, double EndSeconds, double DurationSeconds);

internal sealed record WaveformPoint(int Index, double TimeSeconds, double Min, double Max, double Rms);

internal sealed record SpectrumBin(int Index, double FrequencyHz, double Magnitude, double Decibels);

internal sealed record SegmentSpec(int Index, double StartSeconds, double DurationSeconds)
{
    public double EndSeconds => Math.Round(StartSeconds + DurationSeconds, 4);
}

internal sealed record RenderOptions(
    string Format,
    int? SampleRate,
    int? Channels,
    int Bitrate,
    int BitDepth,
    float Volume,
    bool Normalize,
    double TrimStartSeconds,
    double DurationSeconds,
    double FadeInSeconds,
    double FadeOutSeconds);

internal sealed class TailFadeSampleProvider(ISampleProvider source, TimeSpan fadeOut) : ISampleProvider
{
    private readonly ISampleProvider source = source;
    private readonly long fadeSamples = (long)(fadeOut.TotalSeconds * source.WaveFormat.SampleRate) * source.WaveFormat.Channels;
    private readonly List<float> allSamples = [];
    private int position;
    private bool loaded;

    public WaveFormat WaveFormat => source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        EnsureLoaded();
        var available = Math.Min(count, allSamples.Count - position);
        if (available <= 0) return 0;
        allSamples.CopyTo(position, buffer, offset, available);
        position += available;
        return available;
    }

    public int Read(Span<float> buffer)
    {
        EnsureLoaded();
        var available = Math.Min(buffer.Length, allSamples.Count - position);
        if (available <= 0) return 0;
        CollectionsMarshal.AsSpan(allSamples).Slice(position, available).CopyTo(buffer);
        position += available;
        return available;
    }

    private void EnsureLoaded()
    {
        if (loaded) return;
        var temp = new float[WaveFormat.SampleRate * WaveFormat.Channels];
        int read;
        while ((read = source.Read(temp)) > 0)
            allSamples.AddRange(temp.Take(read));

        if (fadeSamples > 0)
        {
            var start = Math.Max(0, allSamples.Count - (int)Math.Min(fadeSamples, int.MaxValue));
            var length = allSamples.Count - start;
            for (var i = 0; i < length; i++)
                allSamples[start + i] *= 1f - (float)i / Math.Max(1, length);
        }

        loaded = true;
    }
}
