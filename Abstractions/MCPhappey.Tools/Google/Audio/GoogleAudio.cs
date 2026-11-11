using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.Google.Interactions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Google.Audio;

public static partial class GoogleAudio
{
    [Description("Generate audio from the input prompt")]
    [McpServerTool(Title = "Generate speech from input prompt",
        ReadOnly = true)]
    public static async Task<CallToolResult?> GoogleAudio_CreateSpeech(
        [Description("The input prompt to generate the audio")]
        [MaxLength(1024)]
        string prompt,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Voice option")]
        TtsVoiceOption voice = TtsVoiceOption.Kore,
        CancellationToken cancellationToken = default)
         => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
            var interactions = serviceProvider.GetRequiredService<GoogleInteractionsClient>();

            string ttsModel = "gemini-3.1-flash-tts-preview";
            var item = await interactions.CreateInteractionAsync(new GoogleInteractionRequest
            {
                Model = ttsModel,
                Input = GoogleInteractionInput.Text(prompt),
                ResponseModalities = ["audio"],
                GenerationConfig = new System.Text.Json.Nodes.JsonObject
                {
                    ["speech_config"] = new System.Text.Json.Nodes.JsonArray(
                        new System.Text.Json.Nodes.JsonObject
                        {
                            ["voice"] = Enum.GetName(voice) ?? TtsVoiceOption.Kore.ToString()
                        })
                }
            }, cancellationToken, requestContext.Server);

            var audioPart = GoogleInteractionResponse.GetMedia(item, "audio").FirstOrDefault();
            if (audioPart is null)
                throw new InvalidOperationException("Google Audio did not return audio content.");

            using var pcmStream = new MemoryStream(audioPart.Data);
            using var mp3Stream = pcmStream.ConvertL16PcmStreamToMp3(24000, 1);
            var audioBytes = mp3Stream.ToArray();
            AudioContentBlock audio = new()
            {
                Data = audioBytes,
                MimeType = "audio/mp3"
            };

            return audio.ToCallToolResult();

        });

}

