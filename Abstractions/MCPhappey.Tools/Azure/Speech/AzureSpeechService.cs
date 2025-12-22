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
    [McpServerTool(Title = "Azure Speech to Text", ReadOnly = true)]
    public static async Task<CallToolResult?> AzureSpeech_ToText(
     [Description("URL of the audio file (prefer .wav PCM).")]
    string audioUrl,
     [Description("Optional language code, e.g. en-US or nl-NL.")]
    string? language,
     IServiceProvider serviceProvider,
     RequestContext<CallToolRequestParams> requestContext,
     CancellationToken cancellationToken = default)
     => await requestContext.WithExceptionCheck(async () =>
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
     var result = await recognizer.RecognizeOnceAsync();

     if (result.Reason == ResultReason.RecognizedSpeech)
         return new { text = result.Text, durationSeconds = result.Duration.TotalSeconds };
     //  if (result.Reason == ResultReason.NoMatch)
     //     throw new Exception($"NoMatch: {result.NoMatch?.Reason}");
     if (result.Reason == ResultReason.Canceled)
     {
         var c = CancellationDetails.FromResult(result);
         throw new Exception($"Canceled: {c.Reason}; ErrorCode={c.ErrorCode}; Details={c.ErrorDetails}");
     }
     throw new Exception($"Recognition failed: {result.Reason}");
 }));
    /*

        [Description("Convert text into synthetic speech using Azure Text-to-Speech")]
        [McpServerTool(Title = "Azure Text to Speech", ReadOnly = true)]
        public static async Task<CallToolResult?> AzureSpeech_CreateAudio(
            [Description("Text to synthesize into speech.")]
        string text,
            [Description("Optional voice name (default: en-US-JennyNeural).")]
        string? voice,
            IServiceProvider serviceProvider,
            RequestContext<CallToolRequestParams> requestContext,
            CancellationToken cancellationToken = default)
            => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
        {
            var settings = serviceProvider.GetRequiredService<AzureAISettings>();
            var httpClient = serviceProvider.GetRequiredService<HttpClient>();

            var voiceName = string.IsNullOrWhiteSpace(voice)
                ? "en-US-JennyNeural"
                : voice;

            // --- Build SSML payload ---
            var ssml = $@"
                    <speak version='1.0' xml:lang='en-US'>
                    <voice name='{voiceName}'>{System.Security.SecurityElement.Escape(text)}</voice>
                    </speak>";

            using var request = new HttpRequestMessage(HttpMethod.Post, $"https://{settings.Endpoint}/cognitiveservices/v1");
            request.Headers.Add("Ocp-Apim-Subscription-Key", settings.ApiKey);
            request.Headers.Add("User-Agent", "AIhappey/1.0");
            request.Headers.Add("X-Microsoft-OutputFormat", "audio-16khz-128kbitrate-mono-mp3");
            request.Content = new StringContent(ssml, Encoding.UTF8, "application/ssml+xml");

            using var response = await httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new Exception($"TTS request failed: {(int)response.StatusCode} {response.ReasonPhrase}\n{body}");
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var base64 = Convert.ToBase64String(bytes);

            return new
            {
                audioBase64 = base64,
                contentType = "audio/mpeg",
                length = bytes.Length
            };
        }));*/
    /*
        [Description("Convert text into synthetic speech using Azure Text-to-Speech.")]
        [McpServerTool(Title = "Azure Text to Speech", ReadOnly = true)]
        public static async Task<CallToolResult?> AzureSpeech_ToAudio(
            [Description("Text to synthesize into speech.")]
            string text,
            [Description("Optional voice name (default: en-US-JennyNeural).")]
            string? voice,
            IServiceProvider serviceProvider,
            RequestContext<CallToolRequestParams> requestContext,
            CancellationToken cancellationToken = default)
            => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
        {
            var settings = serviceProvider.GetRequiredService<AzureAISettings>();
            var endpoint = new Uri($"https://{settings.Endpoint}/cognitiveservices/v1");

            var config = SpeechConfig.FromEndpoint(
                new Uri("https://westeurope.tts.speech.microsoft.com/cognitiveservices/v1"),
                settings.ApiKey // your fakton-azure-ai-beta key
            );

            //        var config = SpeechConfig.FromEndpoint(endpoint, settings.ApiKey);

            config.SpeechSynthesisVoiceName = voice ?? "en-US-JennyNeural";
            config.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Audio16Khz128KBitRateMonoMp3);

            using var synthesizer = new SpeechSynthesizer(config);
            var result = await synthesizer.SpeakTextAsync(text);

            if (result.Reason == ResultReason.SynthesizingAudioCompleted)
            {
                var bytes = result.AudioData;
                var base64 = Convert.ToBase64String(bytes);
                return new
                {
                    audioBase64 = base64,
                    contentType = "audio/mpeg",
                    length = bytes.Length
                };
            }
            else
            {
                var cancel = SpeechSynthesisCancellationDetails.FromResult(result);
                throw new Exception($"TTS canceled: Reason={cancel.Reason}; ErrorCode={cancel.ErrorCode}; Details={cancel.ErrorDetails}");
            }
        }));*/
}
