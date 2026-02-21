using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Groq.Audio;

public static class GroqAudio
{
    private const string BASE_URL = "https://api.groq.com/openai/v1/audio/speech";

    [Description("Provides fast text-to-speech (TTS), enabling you to convert text to spoken audio in seconds with our available TTS models.")]
    [McpServerTool(
        Title = "Groq Audio Text-to-Speech",
        Name = "groq_audio_text_to_speech",
        Destructive = false)]
    public static async Task<CallToolResult?> GroqAudio_TextToSpeech(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Text to be spoken."), MaxLength(10000)] string input,
        [Description("Voice.")] GroqTtsVoice voice,
        [Description("Output filename (without extension).")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var settings = serviceProvider.GetRequiredService<GroqSettings>();
            var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

            // 1️⃣ Elicit or confirm model input
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new GroqAudioTextToSpeech
                {
                    Input = input,
                    Voice = voice,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            // 2️⃣ Prepare JSON payload
            var payload = new
            {
                model = "playai-tts",
                input = typed.Input,
                voice = typed.Voice.GetEnumMemberValue(),
            };

            using var client = clientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, BASE_URL);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/*"));
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, MimeTypes.Json);

            // 3️⃣ Execute request
            using var resp = await client.SendAsync(request, cancellationToken);
            var bytesOut = await resp.Content.ReadAsByteArrayAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
            {
                var errText = Encoding.UTF8.GetString(bytesOut);
                throw new Exception($"{resp.StatusCode}: {errText}");
            }

            // 4️⃣ Upload result to Graph (so it’s stored & viewable)
            var fileExt = "wav";

            var graphItem = await requestContext.Server.Upload(
                serviceProvider,
                $"{typed.Filename}.{fileExt}",
                BinaryData.FromBytes(bytesOut),
                cancellationToken) ?? throw new Exception("Audio upload failed");

            // 5️⃣ Return structured result (Graph link + playable audio block)
            return new CallToolResult
            {
                Content = [
                    graphItem,
                    new AudioContentBlock
                    {
                        Data = bytesOut,
                        MimeType =  "audio/wav"
                    }
                ]
            };
        });



    [Description("Provides fast Arabic text-to-speech (TTS), enabling you to convert text to spoken Arabic audio in seconds with our available TTS models.")]
    [McpServerTool(
           Title = "Groq Audio Text-to-Speech Arabic",
           Name = "groq_audio_text_to_speech_arabic",
           Destructive = false)]
    public static async Task<CallToolResult?> GroqAudio_TextToSpeechArabic(
           IServiceProvider serviceProvider,
           RequestContext<CallToolRequestParams> requestContext,
           [Description("Text to be spoken."), MaxLength(10000)] string input,
           [Description("Voice.")] GroqTtsArabicVoice voice,
           [Description("Output filename (without extension).")] string? filename = null,
           CancellationToken cancellationToken = default)
           => await requestContext.WithExceptionCheck(async () =>
           {
               ArgumentException.ThrowIfNullOrWhiteSpace(input);

               var settings = serviceProvider.GetRequiredService<GroqSettings>();
               var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

               // 1️⃣ Elicit or confirm model input
               var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                   new GroqAudioTextToSpeechArabic
                   {
                       Input = input,
                       Voice = voice,
                       Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                   },
                   cancellationToken);

               if (notAccepted != null) return notAccepted;
               if (typed == null) return "No input data provided".ToErrorCallToolResponse();

               // 2️⃣ Prepare JSON payload
               var payload = new
               {
                   model = "playai-tts-arabic",
                   input = typed.Input,
                   voice = typed.Voice.GetEnumMemberValue(),
               };

               using var client = clientFactory.CreateClient();
               using var request = new HttpRequestMessage(HttpMethod.Post, BASE_URL);
               request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
               request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/*"));
               request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, MimeTypes.Json);

               // 3️⃣ Execute request
               using var resp = await client.SendAsync(request, cancellationToken);
               var bytesOut = await resp.Content.ReadAsByteArrayAsync(cancellationToken);

               if (!resp.IsSuccessStatusCode)
               {
                   var errText = Encoding.UTF8.GetString(bytesOut);
                   throw new Exception($"{resp.StatusCode}: {errText}");
               }

               // 4️⃣ Upload result to Graph (so it’s stored & viewable)
               var fileExt = "wav";

               var graphItem = await requestContext.Server.Upload(
                   serviceProvider,
                   $"{typed.Filename}.{fileExt}",
                   BinaryData.FromBytes(bytesOut),
                   cancellationToken) ?? throw new Exception("Audio upload failed");

               // 5️⃣ Return structured result (Graph link + playable audio block)
               return new CallToolResult
               {
                   Content = [
                       graphItem,
                        new AudioContentBlock
                        {
                            Data = bytesOut,
                            MimeType = MimeTypes.AudioWaveform
                        }
                   ]
               };
           });

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum GroqTtsVoice
    {
        [EnumMember(Value = "Arista-PlayAI")]
        AristaPlayAI,

        [EnumMember(Value = "Atlas-PlayAI")]
        AtlasPlayAI,

        [EnumMember(Value = "Basil-PlayAI")]
        BasilPlayAI,

        [EnumMember(Value = "Briggs-PlayAI")]
        BriggsPlayAI,

        [EnumMember(Value = "Calum-PlayAI")]
        CalumPlayAI,

        [EnumMember(Value = "Celeste-PlayAI")]
        CelestePlayAI,

        [EnumMember(Value = "Cheyenne-PlayAI")]
        CheyennePlayAI,

        [EnumMember(Value = "Chip-PlayAI")]
        ChipPlayAI,

        [EnumMember(Value = "Cillian-PlayAI")]
        CillianPlayAI,

        [EnumMember(Value = "Deedee-PlayAI")]
        DeedeePlayAI,

        [EnumMember(Value = "Fritz-PlayAI")]
        FritzPlayAI,

        [EnumMember(Value = "Gail-PlayAI")]
        GailPlayAI,

        [EnumMember(Value = "Indigo-PlayAI")]
        IndigoPlayAI,

        [EnumMember(Value = "Mamaw-PlayAI")]
        MamawPlayAI,

        [EnumMember(Value = "Mason-PlayAI")]
        MasonPlayAI,

        [EnumMember(Value = "Mikail-PlayAI")]
        MikailPlayAI,

        [EnumMember(Value = "Mitch-PlayAI")]
        MitchPlayAI,

        [EnumMember(Value = "Quinn-PlayAI")]
        QuinnPlayAI,

        [EnumMember(Value = "Thunder-PlayAI")]
        ThunderPlayAI
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum GroqTtsArabicVoice
    {
        [EnumMember(Value = "Ahmad-PlayAI")]
        AhmadPlayAI,

        [EnumMember(Value = "Amira-PlayAI")]
        AmiraPlayAI,

        [EnumMember(Value = "Khalid-PlayAI")]
        KhalidPlayAI,

        [EnumMember(Value = "Nasser-PlayAI")]
        NasserPlayAI
    }

    [Description("Please fill in the Groq AI text-to-speech generation request.")]
    public class GroqAudioTextToSpeech
    {
        [Required]
        [JsonPropertyName("voice")]
        [Description("The Groq voice to use.")]
        public GroqTtsVoice Voice { get; set; } = GroqTtsVoice.AristaPlayAI;

        [Required]
        [JsonPropertyName("input")]
        [Description("The text input to synthesize into speech.")]
        public string Input { get; set; } = default!;

        [JsonPropertyName("filename")]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }


    [Description("Please fill in the Groq AI text-to-speech Arabic generation request.")]
    public class GroqAudioTextToSpeechArabic
    {
        [Required]
        [JsonPropertyName("voice")]
        [Description("The Groq voice to use.")]
        public GroqTtsArabicVoice Voice { get; set; } = GroqTtsArabicVoice.AhmadPlayAI;

        [Required]
        [JsonPropertyName("input")]
        [Description("The text input to synthesize into speech.")]
        public string Input { get; set; } = default!;

        [JsonPropertyName("filename")]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }

}



public class GroqSettings
{
    public string ApiKey { get; set; } = default!;
}
