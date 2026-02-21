using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using ModelContextProtocol.Protocol;
using Mscc.GenerativeAI;
using NAudio.Lame;
using NAudio.Wave;

namespace MCPhappey.Tools.Google.Audio;

public static partial class GoogleAudio
{
    private static MemoryStream ConvertL16PcmStreamToMp3(this Stream pcmStream, int sampleRate = 24000, int channels = 1)
    {
        if (pcmStream.CanSeek) pcmStream.Position = 0;

        using var wavStream = new MemoryStream();
        var waveFormat = new WaveFormat(sampleRate, 16, channels);
        using (var rawSource = new RawSourceWaveStream(pcmStream, waveFormat))
        {
            WaveFileWriter.WriteWavFileToStream(wavStream, rawSource);
        }
        wavStream.Position = 0;

        var mp3Stream = new MemoryStream();
        using (var wavReader = new WaveFileReader(wavStream))
        using (var mp3Writer = new LameMP3FileWriter(mp3Stream, wavReader.WaveFormat, LAMEPreset.STANDARD))
        {
            wavReader.CopyTo(mp3Writer);
        }
        mp3Stream.Position = 0;
        return mp3Stream;
    }

    private static async Task<AudioContentBlock> GenerateMultiSpeakerAudioAsync(
        string prompt,
        string nameSpeakerOne,
        string nameSpeakerTwo,
        TtsVoiceOption voiceSpeakerOne,
        TtsVoiceOption voiceSpeakerTwo,
        string ttsModel,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        var googleAI = serviceProvider.GetRequiredService<GoogleAI>();
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
                    MultiSpeakerVoiceConfig = new()
                    {
                        SpeakerVoiceConfigs = [
                            new() {
                            Speaker = nameSpeakerOne,
                            VoiceConfig = new() {
                                PrebuiltVoiceConfig = new() {
                                    VoiceName = Enum.GetName(voiceSpeakerOne) ?? TtsVoiceOption.Kore.ToString()
                                }
                            }
                        },
                        new() {
                            Speaker = nameSpeakerTwo,
                            VoiceConfig = new() {
                                PrebuiltVoiceConfig = new() {
                                    VoiceName = Enum.GetName(voiceSpeakerTwo) ?? TtsVoiceOption.Sulafat.ToString()
                                }
                            }
                        }
                        ]
                    }
                }
            }
        }, cancellationToken: cancellationToken);

        var audioPart = item.Candidates?.FirstOrDefault()?.Content?.Parts.FirstOrDefault();
        var base64 = audioPart?.InlineData?.Data;
        if (string.IsNullOrWhiteSpace(base64))
            throw new Exception("No audio data returned.");

        byte[] pcmBytes = Convert.FromBase64String(base64);

        using var pcmStream = new MemoryStream(pcmBytes);
        using var mp3Stream = pcmStream.ConvertL16PcmStreamToMp3(24000, 1);

        return new AudioContentBlock
        {
            Data = mp3Stream.ToArray(),
            MimeType = MimeTypes.AudioMP3
        };
    }

    public enum TtsVoiceOption
    {
        [JsonStringEnumMemberName("Zephyr")]
        Zephyr,
        [JsonStringEnumMemberName("Puck")]
        Puck,
        [JsonStringEnumMemberName("Charon")]
        Charon,
        [JsonStringEnumMemberName("Kore")]
        Kore,
        [JsonStringEnumMemberName("Fenrir")]
        Fenrir,
        [JsonStringEnumMemberName("Leda")]
        Leda,
        [JsonStringEnumMemberName("Orus")]
        Orus,
        [JsonStringEnumMemberName("Aoede")]
        Aoede,
        [JsonStringEnumMemberName("Callirrhoe")]
        Callirrhoe,
        [JsonStringEnumMemberName("Autonoe")]
        Autonoe,
        [JsonStringEnumMemberName("Enceladus")]
        Enceladus,
        [JsonStringEnumMemberName("Iapetus")]
        Iapetus,
        [JsonStringEnumMemberName("Umbriel")]
        Umbriel,
        [JsonStringEnumMemberName("Algieba")]
        Algieba,
        [JsonStringEnumMemberName("Despina")]
        Despina,
        [JsonStringEnumMemberName("Erinome")]
        Erinome,
        [JsonStringEnumMemberName("Algenib")]
        Algenib,
        [JsonStringEnumMemberName("Rasalgethi")]
        Rasalgethi,
        [JsonStringEnumMemberName("Laomedeia")]
        Laomedeia,
        [JsonStringEnumMemberName("Achernar")]
        Achernar,
        [JsonStringEnumMemberName("Alnilam")]
        Alnilam,
        [JsonStringEnumMemberName("Schedar")]
        Schedar,
        [JsonStringEnumMemberName("Gacrux")]
        Gacrux,
        [JsonStringEnumMemberName("Pulcherrima")]
        Pulcherrima,
        [JsonStringEnumMemberName("Achird")]
        Achird,
        [JsonStringEnumMemberName("Zubenelgenubi")]
        Zubenelgenubi,
        [JsonStringEnumMemberName("Vindemiatrix")]
        Vindemiatrix,
        [JsonStringEnumMemberName("Sadachbia")]
        Sadachbia,
        [JsonStringEnumMemberName("Sadaltager")]
        Sadaltager,
        [JsonStringEnumMemberName("Sulafat")]
        Sulafat
    }


}

