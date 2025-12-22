using System.Text;
using MCPhappey.Tools.Extensions;
using OpenAI.Audio;
using static MCPhappey.Tools.OpenAI.Audio.OpenAIAudio;

namespace MCPhappey.Tools.OpenAI.Audio;

public static class OpenAIAudioExtensions
{
    public static async Task<string> CreateTranscriptionText(this AudioClient audioClient,
        BinaryData content, string filename,
        AudioTranscriptionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var splitted = content.Split(25);

        StringBuilder resultString = new();

        foreach (var fileSplit in splitted ?? [])
        {
            var item = await audioClient.CreateTranscription(fileSplit, filename, options,
                cancellationToken: cancellationToken);

            resultString.AppendLine(item?.Text);
        }

        return resultString.ToString();

    }

    public static GeneratedSpeechVoice ToGeneratedSpeechVoice(this Voice voice)
      => new(voice.ToString().ToLowerInvariant());


    public static async Task<AudioTranscription> CreateTranscription(this AudioClient audioClient,
       BinaryData content, string filename,
       AudioTranscriptionOptions? options = null,
       CancellationToken cancellationToken = default) =>
        await audioClient.TranscribeAudioAsync(content.ToStream(), filename,
            options: options,
            cancellationToken: cancellationToken);

}
