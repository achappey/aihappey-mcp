using System.ComponentModel;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.Azure.DocumentIntelligence;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Azure.Speech;

public static class AzureSpeechService
{
    [Description("Convert spoken audio into text using Azure Speech-to-Text.")]
    [McpServerTool(Title = "Azure Speech to Text",
        UseStructuredContent = true,
        OutputSchemaType = typeof(SpeechRecognitionResult),
        ReadOnly = true)]
    public static async Task<CallToolResult?> AzureSpeech_ToText(
     [Description("URL of the audio file (prefer .wav PCM).")]
    string audioUrl,
     [Description("Optional language code, e.g. en-US or nl-NL.")]
    string? language,
     IServiceProvider serviceProvider,
     RequestContext<CallToolRequestParams> requestContext,
     CancellationToken cancellationToken = default)
     => await ModelContextToolExtensions.WithExceptionCheck(async () =>
     await requestContext.WithStructuredContent(async () =>
 {
     var settings = serviceProvider.GetRequiredService<AzureAISettings>();
     var downloadService = serviceProvider.GetRequiredService<DownloadService>();

     // 1) Download audio
     var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, audioUrl, cancellationToken);
     var file = files.FirstOrDefault() ?? throw new Exception("No audio file provided.");

     // 2) Maak push-stream en audio-config
     // Let op: voor niet-PCM/WAV moet je zelf naar WAV converteren of de Batch REST API gebruiken.
     var pushStream = AudioInputStream.CreatePushStream(); // optionally pass an AudioStreamFormat for raw PCM
     using var audioConfig = AudioConfig.FromStreamInput(pushStream);

     // 3) Config van Speech
     var config = SpeechConfig.FromHost(new Uri("https://" + settings.Endpoint), settings.ApiKey);

     if (!string.IsNullOrWhiteSpace(language))
         config.SpeechRecognitionLanguage = language;
     config.SetProfanity(ProfanityOption.Raw);

     // 4) Schrijf bytes in de push-stream vóór of tijdens de herkenning
     pushStream.Write(file.Contents.ToArray());
     pushStream.Close(); // EOF

     using var recognizer = new SpeechRecognizer(config, audioConfig);
     return await recognizer.RecognizeOnceAsync();

 }));

}
