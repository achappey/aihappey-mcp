using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.AsyncAI;

public static class AsyncAIService
{
    [Description("Convert input text into synthetic speech using asyncAI's Text-to-Speech API.")]
    [McpServerTool(Title = "asyncAI Text to Speech", Destructive = false, OpenWorld = false)]
    public static async Task<CallToolResult?> AsyncAI_TextToSpeech(
        [Description("Text to convert into speech (up to 4096 characters).")]
        [MaxLength(4096)]
        string transcript,
        [Description("Voice UUID to use for speech generation.")]
        string voiceId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Model ID to use (default: asyncflow_multilingual_v1.0).")]
        string modelId = "asyncflow_multilingual_v1.0",
        [Description("Audio container format (mp3, wav, or raw). Defaults to mp3.")]
        string container = "mp3",
        [Description("Sample rate in Hz (e.g., 44100).")]
        int sampleRate = 44100,
        [Description("Optional bit rate in bps (only for mp3). Defaults to 192000.")]
        int? bitRate = 192000,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
    {
        var asyncAI = serviceProvider.GetRequiredService<AsyncAIClient>();

        var voice = new
        {
            mode = "id",
            id = voiceId
        };

        // ✅ encoding only for non-MP3
        object outputFormat = container switch
        {
            "mp3" => new { container, sample_rate = sampleRate, bit_rate = bitRate },
            "wav" => new { container, encoding = "pcm_s16le", sample_rate = sampleRate },
            "raw" => new { container, encoding = "pcm_s16le", sample_rate = sampleRate },
            _ => throw new ArgumentException("Invalid container format. Must be mp3, wav, or raw.")
        };

        var body = new
        {
            model_id = modelId,
            transcript,
            voice,
            output_format = outputFormat
        };

        var audioBytes = await asyncAI.TextToSpeechAsync(body, cancellationToken);

        // Upload or embed result
        var uploaded = await requestContext.Server.Upload(
            serviceProvider,
            requestContext.ToOutputFileName(container),
            BinaryData.FromBytes(audioBytes),
            cancellationToken);

        var mimeType = container == "wav" ? MimeTypes.AudioWaveform : MimeTypes.AudioMP3;

        return new CallToolResult
        {
            Content = [ new ResourceLinkBlock
                    {
                        Uri = uploaded!.Uri,
                        Name = uploaded.Name,
                        MimeType = mimeType,
                    }, new AudioContentBlock
                    {
                        MimeType = mimeType,
                        Data = Convert.ToBase64String(audioBytes)
                    }]
        };
    }));

    [Description("Retrieve available voices from asyncAI’s voice library with optional filters for language, gender, and model.")]
    [McpServerTool(Title = "List asyncAI voices", Destructive = false, OpenWorld = false, ReadOnly = true)]
    public static async Task<CallToolResult?> AsyncAI_ListVoices(
      IServiceProvider serviceProvider,
      RequestContext<CallToolRequestParams> requestContext,
      [Description("Number of voices per page (1–100). Default: 10")] int limit = 10,
      [Description("Filter by language (e.g., en, de, es, fr, it).")] string? language = null,
      [Description("Filter by model id (asyncflow_v2.0 or asyncflow_multilingual_v1.0).")] string? modelId = null,
      [Description("Filter by accent.")] string? accent = null,
      [Description("Filter by gender (Male, Female, Neutral, Unspecified).")] string? gender = null,
      [Description("Filter by style.")] string? style = null,
      [Description("Return only voices owned by the current user.")] bool? myVoice = null,
      [Description("Pagination cursor (voice id) for next page.")] string? after = null,
      CancellationToken cancellationToken = default) =>
      await requestContext.WithExceptionCheck(async () =>
      await requestContext.WithStructuredContent(async () =>
  {
      var asyncAI = serviceProvider.GetRequiredService<AsyncAIClient>();

      var body = new
      {
          limit,
          language,
          model_id = modelId,
          accent,
          gender,
          style,
          my_voice = myVoice,
          after
      };

      return await asyncAI.PostJsonAsync<AsyncAIVoiceListResponse>("voices", body, cancellationToken);
  }));

    [Description("Generate speech and aligned word timestamps from input text using asyncAI's Text-to-Speech API.")]
    [McpServerTool(Title = "asyncAI Text to Speech with timestamps", Destructive = false, OpenWorld = false)]
    public static async Task<CallToolResult?> AsyncAI_TextToSpeechWithTimestamps(
         [Description("Text to convert into speech (up to 4096 characters).")]
        [MaxLength(4096)]
        string transcript,
         [Description("Voice ID to use for speech generation.")]
        string voiceId,
         IServiceProvider serviceProvider,
         RequestContext<CallToolRequestParams> requestContext,
         [Description("Model ID to use (asyncflow_multilingual_v1.0 or asyncflow_v2.0).")]
        string modelId = "asyncflow_multilingual_v1.0",
         [Description("Audio container format (mp3, wav, or raw). Defaults to mp3.")]
        string container = "mp3",
         [Description("Sample rate in Hz (e.g., 44100).")]
        int sampleRate = 44100,
         [Description("Optional bit rate in bps (only for mp3). Defaults to 192000.")]
        int? bitRate = 192000,
         CancellationToken cancellationToken = default)
         => await requestContext.WithExceptionCheck(async () =>
         await requestContext.WithStructuredContent(async () =>
     {
         var asyncAI = serviceProvider.GetRequiredService<AsyncAIClient>();
         var body = new
         {
             model_id = modelId,
             transcript,
             voice = new
             {
                 mode = "id",
                 id = voiceId
             },
             output_format = new
             {
                 container,
                 encoding = container == "mp3" ? null : "pcm_s16le",
                 sample_rate = sampleRate,
                 bit_rate = bitRate
             }
         };

         var audioBytes = await asyncAI.TextToSpeechWithTimestampsAsync(body, cancellationToken);

         // Upload audio file
         var uploaded = await requestContext.Server.Upload(
             serviceProvider,
             requestContext.ToOutputFileName(container),
             BinaryData.FromBytes(audioBytes),
             cancellationToken);

         var mimeType = container == "wav" ? MimeTypes.AudioWaveform : MimeTypes.AudioMP3;

         return new CallToolResult
         {
             Content = [ new ResourceLinkBlock
                    {
                        Uri = uploaded!.Uri,
                        Name = uploaded.Name,
                        MimeType = mimeType,
                    }, new AudioContentBlock
                    {
                        MimeType = mimeType,
                        Data = Convert.ToBase64String(audioBytes)
                    }]
         };
     }));

    public class AsyncAIWithTimestampsResponse
    {
        [JsonPropertyName("audio")]
        public string Audio { get; set; } = null!;

        [JsonPropertyName("timestamps")]
        public IEnumerable<WordTimestamp> Timestamps { get; set; } = [];
    }

    public class WordTimestamp
    {
        [JsonPropertyName("word")]
        public string Word { get; set; } = null!;

        [JsonPropertyName("start")]
        public double Start { get; set; }

        [JsonPropertyName("end")]
        public double End { get; set; }
    }

    public class AsyncAIVoiceListResponse
    {
        [JsonPropertyName("voices")]
        public IEnumerable<AsyncAIVoice> Voices { get; set; } = [];
    }

    public class AsyncAIVoice
    {
        [JsonPropertyName("voice_id")]
        public string VoiceId { get; set; } = null!;

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("language")]
        public string? Language { get; set; }

        [JsonPropertyName("accent")]
        public string? Accent { get; set; }

        [JsonPropertyName("gender")]
        public string? Gender { get; set; }

        [JsonPropertyName("style")]
        public string? Style { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("model_id")]
        public string? ModelId { get; set; }
    }
}

