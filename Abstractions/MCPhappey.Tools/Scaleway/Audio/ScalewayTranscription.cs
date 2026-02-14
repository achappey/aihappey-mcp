using System.ComponentModel;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Scaleway.Audio;

public static class ScalewayTranscription
{
    private const string ScalewayAudioTranscriptionsPath = "v1/audio/transcriptions";

    [Description("Transcribe audio files using Scaleway Audio Transcriptions. Supports SharePoint, OneDrive, and HTTP file URLs.")]
    [McpServerTool(
        Title = "Scaleway Audio Transcription",
        Name = "scaleway_audio_transcribe_audio",
        Destructive = false,
        ReadOnly = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> ScalewayAudio_TranscribeAudio(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Audio file URL to transcribe (.wav, .mp3, .flac, .mpga, .oga, .ogg). SharePoint and OneDrive URLs are supported.")]
        string fileUrl,
        [Description("Model identifier to use for transcription, e.g. voxtral-small-24b-2507.")]
        string model,
        [Description("Optional language code (ISO-639-1), e.g. en or nl. Auto-detected when omitted.")]
        string? language = null,
        [Description("Optional guidance text to influence transcription.")]
        string? prompt = null,
        [Description("Sampling temperature between 0 and 2.")]
        double temperature = 0,
        [Description("Optional output filename without extension.")]
        string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(fileUrl);
                ArgumentException.ThrowIfNullOrWhiteSpace(model);

                if (temperature < 0 || temperature > 2)
                    throw new Exception("temperature must be between 0 and 2.");

                var settings = serviceProvider.GetRequiredService<ScalewaySettings>();
                var downloadService = serviceProvider.GetRequiredService<DownloadService>();
                var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

                var downloads = await downloadService.DownloadContentAsync(
                    serviceProvider,
                    requestContext.Server,
                    fileUrl,
                    cancellationToken);

                var audioFile = downloads.FirstOrDefault()
                    ?? throw new InvalidOperationException("Failed to download audio content.");

                var endpoint = $"https://api.scaleway.ai/{ScalewayAudioTranscriptionsPath}";

                using var form = new MultipartFormDataContent();

                var streamContent = new StreamContent(audioFile.Contents.ToStream());
                streamContent.Headers.ContentType = new MediaTypeHeaderValue(
                    string.IsNullOrWhiteSpace(audioFile.MimeType) ? "audio/mpeg" : audioFile.MimeType);

                form.Add(streamContent, "file", string.IsNullOrWhiteSpace(audioFile.Filename) ? "input.mp3" : audioFile.Filename);
                form.Add(new StringContent(model), "model");
                form.Add(new StringContent("json"), "response_format");
                form.Add(new StringContent("false"), "stream");
                form.Add(new StringContent(temperature.ToString(CultureInfo.InvariantCulture)), "temperature");

                if (!string.IsNullOrWhiteSpace(language))
                    form.Add(new StringContent(language), "language");

                if (!string.IsNullOrWhiteSpace(prompt))
                    form.Add(new StringContent(prompt), "prompt");

                using var client = clientFactory.CreateClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));

                using var response = await client.PostAsync(endpoint, form, cancellationToken);
                var json = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                    throw new Exception($"{response.StatusCode}: {json}");

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var text = root.TryGetProperty("text", out var textElement)
                    ? textElement.GetString()
                    : null;

                var usageType = root.TryGetProperty("usage", out var usageElement)
                                && usageElement.TryGetProperty("type", out var typeElement)
                    ? typeElement.GetString()
                    : null;

                var usageSeconds = root.TryGetProperty("usage", out var usageElement2)
                                   && usageElement2.TryGetProperty("seconds", out var secondsElement)
                                   && secondsElement.TryGetDouble(out var seconds)
                    ? seconds
                    : (double?)null;

                var safeName = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName();

                var uploadedTxt = await requestContext.Server.Upload(
                    serviceProvider,
                    $"{safeName}.txt",
                    BinaryData.FromString(text ?? string.Empty),
                    cancellationToken);

                var uploadedJson = await requestContext.Server.Upload(
                    serviceProvider,
                    $"{safeName}.json",
                    BinaryData.FromString(json),
                    cancellationToken);

                return new
                {
                    provider = "scaleway",
                    model,
                    fileUrl,
                    language,
                    prompt,
                    responseFormat = "json",
                    stream = false,
                    temperature,
                    text,
                    usage = new
                    {
                        type = usageType,
                        seconds = usageSeconds
                    },
                    output = new
                    {
                        transcriptFileUri = uploadedTxt?.Uri,
                        transcriptFileName = uploadedTxt?.Name,
                        transcriptMimeType = uploadedTxt?.MimeType,
                        rawResponseFileUri = uploadedJson?.Uri,
                        rawResponseFileName = uploadedJson?.Name,
                        rawResponseMimeType = uploadedJson?.MimeType
                    }
                };
            }));
}
