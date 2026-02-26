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

namespace MCPhappey.Tools.SambaNova;

public static class SambaNovaTranscriptions
{
    private const string TranscriptionsUrl = "https://api.sambanova.ai/v1/audio/transcriptions";

    [Description("Transcribe audio from fileUrl using SambaNova and return structured transcription output.")]
    [McpServerTool(
        Title = "SambaNova Audio Transcription",
        Name = "sambanova_transcriptions_create",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> SambaNova_Transcriptions_Create(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Audio file URL (.flac/.mp3/.mp4/.mpeg/.mpga/.m4a/.ogg/.wav/.webm) to transcribe. SharePoint and OneDrive are supported.")]
        string fileUrl,
        [Description("Model id. Default: Whisper-Large-v3.")]
        string model = "Whisper-Large-v3",
        [Description("Optional prompt to guide transcription.")]
        string? prompt = null,
        [Description("Optional language hint in ISO-639-1 (e.g. en, es).")]
        string? language = null,
        [Description("Sampling temperature between 0 and 1. Default: 0.")]
        double temperature = 0,
        [Description("Output filename without extension.")]
        string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(fileUrl);
                ArgumentException.ThrowIfNullOrWhiteSpace(model);

                if (temperature < 0 || temperature > 1)
                    throw new Exception("temperature must be between 0 and 1.");

                var settings = serviceProvider.GetRequiredService<SambaNovaSettings>();
                var downloadService = serviceProvider.GetRequiredService<DownloadService>();
                var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

                var downloads = await downloadService.DownloadContentAsync(
                    serviceProvider,
                    requestContext.Server,
                    fileUrl,
                    cancellationToken);

                var audioFile = downloads.FirstOrDefault()
                    ?? throw new InvalidOperationException("Failed to download audio content from fileUrl.");

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

                using var response = await client.PostAsync(TranscriptionsUrl, form, cancellationToken);
                var json = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                    throw new Exception($"{response.StatusCode}: {json}");

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var text = root.TryGetProperty("text", out var textElement)
                    ? textElement.GetString()
                    : null;

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
                    provider = "sambanova",
                    model,
                    fileUrl,
                    language,
                    prompt,
                    responseFormat = "json",
                    stream = false,
                    temperature,
                    text,
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
