using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Daglo;

public static class DagloSpeech
{
    private const string TtsPath = "tts/v1/sync/audios";

    [Description("Generate speech audio from raw text using Daglo and upload the result as a resource link.")]
    [McpServerTool(
        Title = "Daglo Text-to-Speech",
        Name = "daglo_speech_text_to_speech",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Daglo_Speech_TextToSpeech(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Text to synthesize into speech.")] string text,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new DagloSpeechTextToSpeechRequest
                {
                    Text = text,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            return await GenerateAndUploadSpeechAsync(
                serviceProvider,
                requestContext,
                typed.Text,
                typed.Filename,
                cancellationToken);
        });

    [Description("Generate speech audio from a fileUrl by scraping text first, then synthesizing with Daglo.")]
    [McpServerTool(
        Title = "Daglo File-to-Speech",
        Name = "daglo_speech_file_to_speech",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Daglo_Speech_FileToSpeech(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("File URL (SharePoint, OneDrive, HTTP) to extract text from.")] string fileUrl,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fileUrl);

            var downloadService = serviceProvider.GetRequiredService<DownloadService>();
            var files = await downloadService.ScrapeContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
            var sourceText = string.Join("\n\n", files.GetTextFiles().Select(f => f.Contents.ToString()));

            if (string.IsNullOrWhiteSpace(sourceText))
                throw new ValidationException("No readable text content found in fileUrl.");

            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new DagloSpeechFileToSpeechRequest
                {
                    FileUrl = fileUrl,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            return await GenerateAndUploadSpeechAsync(
                serviceProvider,
                requestContext,
                sourceText,
                typed.Filename,
                cancellationToken);
        });

    private static async Task<CallToolResult?> GenerateAndUploadSpeechAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string text,
        string filename,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ValidationException("text is required.");

        var daglo = serviceProvider.GetRequiredService<DagloClient>();

        var wavBytes = await daglo.PostForBytesAsync(
            TtsPath,
            new { text },
            acceptMimeType: "audio/wav",
            cancellationToken);

        var outputName = filename.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
            ? filename
            : $"{filename}.wav";

        var uploaded = await requestContext.Server.Upload(
            serviceProvider,
            outputName,
            BinaryData.FromBytes(wavBytes),
            cancellationToken);

        return uploaded?.ToResourceLinkCallToolResponse();
    }
}

[Description("Please fill in the Daglo text-to-speech request.")]
public sealed class DagloSpeechTextToSpeechRequest
{
    [JsonPropertyName("text")]
    [Required]
    [Description("Text to synthesize.")]
    public string Text { get; set; } = default!;

    [JsonPropertyName("filename")]
    [Required]
    [Description("Output filename without extension.")]
    public string Filename { get; set; } = default!;
}

[Description("Please fill in the Daglo file-to-speech request.")]
public sealed class DagloSpeechFileToSpeechRequest
{
    [JsonPropertyName("fileUrl")]
    [Required]
    [Description("Source file URL to scrape/extract text from.")]
    public string FileUrl { get; set; } = default!;

    [JsonPropertyName("filename")]
    [Required]
    [Description("Output filename without extension.")]
    public string Filename { get; set; } = default!;
}

