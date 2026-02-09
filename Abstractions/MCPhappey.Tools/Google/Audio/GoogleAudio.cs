using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graph.Beta;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Mscc.GenerativeAI;

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
         => await requestContext.WithExceptionCheck(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
            var googleAI = serviceProvider.GetRequiredService<GoogleAI>();

            string ttsModel = "gemini-2.5-flash-preview-tts";
            var modelClient = googleAI.GenerativeModel(ttsModel);
            var item = await modelClient.GenerateContent(new GenerateContentRequest()
            {
                Model = ttsModel,
                Contents = [new Content(prompt)],
                GenerationConfig = new()
                {
                    ResponseModalities = [ResponseModality.Audio],
                    SpeechConfig = new()
                    {
                        VoiceConfig = new()
                        {
                            PrebuiltVoiceConfig = new()
                            {
                                VoiceName = Enum.GetName(voice) ?? TtsVoiceOption.Kore.ToString()
                            }
                        }
                    }
                }
            }, cancellationToken: cancellationToken);

            var audioPart = item.Candidates?.FirstOrDefault()?.Content?.Parts.FirstOrDefault();
            var base64 = audioPart?.InlineData?.Data;
            byte[] pcmBytes = Convert.FromBase64String(base64!);

            using var pcmStream = new MemoryStream(pcmBytes);
            using var mp3Stream = pcmStream.ConvertL16PcmStreamToMp3(24000, 1);

            AudioContentBlock audio = new()
            {
                Data = Convert.ToBase64String(mp3Stream.ToArray()),
                MimeType = "audio/mp3"
            };

            return audio.ToCallToolResult();

        });

    [Description("Generate multi speaker audio from the input prompt")]
    [McpServerTool(Title = "Generate multi speaker audio from input prompt",
        Destructive = false)]
    public static async Task<CallToolResult?> GoogleAudio_CreateMultiSpeakerSpeech(
           [Description("The input prompt to generate the audio")]
           [MaxLength(1024)]
                string prompt,
           [Description("Name of speaker one")]
                string nameSpeakerOne,
           [Description("Name of speaker two")]
                string nameSpeakerTwo,
           [Description("File name of the resulting mp3 file without extension")]
                string filename,
           IServiceProvider serviceProvider,
             RequestContext<CallToolRequestParams> requestContext,
           [Description("Voice speaker one")]
                TtsVoiceOption voiceSpeakerOne = TtsVoiceOption.Kore,
           [Description("Voice speaker two")]
                TtsVoiceOption voiceSpeakerTwo = TtsVoiceOption.Sulafat,
           CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        var googleAI = serviceProvider.GetRequiredService<GoogleAI>();

        var audio = await GenerateMultiSpeakerAudioAsync(
            prompt,
            nameSpeakerOne,
            nameSpeakerTwo,
            voiceSpeakerOne,
            voiceSpeakerTwo,
            "gemini-2.5-flash-preview-tts",
            serviceProvider,
            cancellationToken
        );

        var outputName = $"{filename}.mp3";
        using var uploadStream = new MemoryStream(Convert.FromBase64String(audio.Data));

        var myDrive = await client.Me.Drive.GetAsync(cancellationToken: cancellationToken);
        var uploadedItem = await client.Drives[myDrive?.Id].Root.ItemWithPath($"/{outputName}")
            .Content.PutAsync(uploadStream, cancellationToken: cancellationToken);

        return uploadedItem?.WebUrl?.ToTextCallToolResponse();
    }));

}

