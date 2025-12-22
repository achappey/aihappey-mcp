using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Services;

namespace MCPhappey.Tools.Runway.Audio;

public static class RunwayAudio
{
    private static readonly HashSet<string> AllowedIsoModels = ["eleven_voice_isolation"];
    private static readonly HashSet<string> AllowedTtsModels = ["eleven_multilingual_v2"];
    private static readonly HashSet<string> AllowedVoices =
    [
        "Maya","Arjun","Serene","Bernard","Billy","Mark","Clint","Mabel","Chad","Leslie","Eleanor","Elias","Elliot",
        "Grungle","Brodie","Sandra","Kirk","Kylie","Lara","Lisa","Malachi","Marlene","Martin","Miriam","Monster","Paula",
        "Pip","Rusty","Ragnar","Xylar","Maggie","Jack","Katie","Noah","James","Rina","Ella","Mariah","Frank","Claudia",
        "Niki","Vincent","Kendrick","Myrna","Tom","Wanda","Benjamin","Kiana","Rachel"
    ];

    private static readonly HashSet<string> AllowedDubModels = ["eleven_voice_dubbing"];
    private static readonly HashSet<string> AllowedLangs =
    [
        "en","hi","pt","zh","es","fr","de","ja","ar","ru","ko","id","it","nl","tr","pl","sv","fil","ms",
        "ro","uk","el","cs","da","fi","bg","hr","sk","ta"
    ];

    // ---------- TEXT → SPEECH ----------
    [Description("Generate synthetic speech from text using Runway's text-to-speech model.")]
    [McpServerTool(Title = "Create Runway Text-to-Speech", Name = "runway_text_to_speech", OpenWorld = true, ReadOnly = false, Destructive = false)]
    public static async Task<CallToolResult?> Runway_TextToSpeech(
        string promptText,
        string? voicePresetId,
        string? model,
        IServiceProvider sp,
        RequestContext<CallToolRequestParams> rc,
        CancellationToken ct = default)
        => await rc.WithExceptionCheck(async () =>
        await rc.WithStructuredContent(async () =>
    {
        var (typed, _, _) = await rc.Server.TryElicit(new RunwayNewTextToSpeech
        {
            PromptText = promptText,
            Model = string.IsNullOrWhiteSpace(model) ? "eleven_multilingual_v2" : model!,
            VoicePresetId = string.IsNullOrWhiteSpace(voicePresetId) ? "Maya" : voicePresetId!
        }, ct);

        ValidateTts(typed);

        var runway = sp.GetRequiredService<RunwayClient>();
        var payload = new
        {
            promptText = typed.PromptText,
            model = typed.Model,
            voice = new
            {
                type = "runway-preset",
                presetId = typed.VoicePresetId
            }
        };

        return await runway.TextToSpeechAsync(payload, ct);
    }));


    // ---------- VOICE DUBBING ----------
    [Description("Dub audio content into another language using Runway's voice dubbing model.")]
    [McpServerTool(Title = "Create Runway Voice Dubbing", Name = "runway_voice_dubbing", OpenWorld = true, ReadOnly = false, Destructive = false)]
    public static async Task<CallToolResult?> Runway_VoiceDubbing(
        string audioUri,
        string targetLang,
        bool? disableVoiceCloning,
        bool? dropBackgroundAudio,
        int? numSpeakers,
        string? model,
        IServiceProvider sp,
        RequestContext<CallToolRequestParams> rc,
        CancellationToken ct = default)
        => await rc.WithExceptionCheck(async () =>
        await rc.WithStructuredContent(async () =>
    {
        var (typed, _, _) = await rc.Server.TryElicit(new RunwayNewVoiceDubbing
        {
            TargetLang = targetLang,
            Model = string.IsNullOrWhiteSpace(model) ? "eleven_voice_dubbing" : model!,
            DisableVoiceCloning = disableVoiceCloning ?? false,
            DropBackgroundAudio = dropBackgroundAudio ?? false,
            NumSpeakers = numSpeakers
        }, ct);

        ValidateDub(typed);

        // Download and encode audio to base64 data URI
        var downloadService = sp.GetRequiredService<DownloadService>();
        var files = await downloadService.DownloadContentAsync(sp, rc.Server, audioUri, ct);
        var bytes = files.FirstOrDefault() ?? throw new Exception($"Failed to download audio: {audioUri}");
        string dataUri = bytes.ToDataUri();

        var runway = sp.GetRequiredService<RunwayClient>();
        var payload = new
        {
            model = typed.Model,
            audioUri = dataUri,
            targetLang = typed.TargetLang,
            disableVoiceCloning = typed.DisableVoiceCloning,
            dropBackgroundAudio = typed.DropBackgroundAudio,
            numSpeakers = typed.NumSpeakers
        };

        return await runway.VoiceDubbingAsync(payload, ct);
    }));

    [Description("Isolate speech from background audio using Runway's voice isolation model.")]
    [McpServerTool(Title = "Create Runway Voice Isolation", Name = "runway_voice_isolation", OpenWorld = true, ReadOnly = false, Destructive = false)]
    public static async Task<CallToolResult?> Runway_VoiceIsolation(
      string audioUri,
      string? model,
      IServiceProvider sp,
      RequestContext<CallToolRequestParams> rc,
      CancellationToken ct = default)
      => await rc.WithExceptionCheck(async () =>
      await rc.WithStructuredContent(async () =>
  {
      var (typed, _, _) = await rc.Server.TryElicit(new RunwayNewVoiceIsolation
      {
          Model = string.IsNullOrWhiteSpace(model) ? "eleven_voice_isolation" : model!
      }, ct);

      ValidateIso(typed);

      // Download and encode audio file to base64 data URI
      var downloadService = sp.GetRequiredService<DownloadService>();
      var files = await downloadService.DownloadContentAsync(sp, rc.Server, audioUri, ct);
      var bytes = files.FirstOrDefault() ?? throw new Exception($"Failed to download audio: {audioUri}");
      string dataUri = bytes.ToDataUri();

      var runway = sp.GetRequiredService<RunwayClient>();
      var payload = new
      {
          model = typed.Model,
          audioUri = dataUri
      };

      return await runway.VoiceIsolationAsync(payload, ct);
  }));


    private static void ValidateIso(RunwayNewVoiceIsolation input)
    {
        if (!AllowedIsoModels.Contains(input.Model))
            throw new ValidationException("Model must be 'eleven_voice_isolation'.");
    }

    [Description("Typed input for Runway voice isolation.")]
    public class RunwayNewVoiceIsolation
    {
        [JsonPropertyName("model")]
        [Required]
        [Description("Model name, must be 'eleven_voice_isolation'.")]
        public string Model { get; set; } = default!;
    }

    // ---------- VALIDATION ----------
    private static void ValidateTts(RunwayNewTextToSpeech input)
    {
        if (string.IsNullOrWhiteSpace(input.PromptText))
            throw new ValidationException("PromptText is required.");
        if (input.PromptText.Length > 1000)
            throw new ValidationException("PromptText must be at most 1000 characters.");
        if (!AllowedTtsModels.Contains(input.Model))
            throw new ValidationException("Model must be 'eleven_multilingual_v2'.");
        if (string.IsNullOrWhiteSpace(input.VoicePresetId) || !AllowedVoices.Contains(input.VoicePresetId))
            throw new ValidationException($"VoicePresetId must be one of [{string.Join(", ", AllowedVoices)}].");
    }

    private static void ValidateDub(RunwayNewVoiceDubbing input)
    {
        if (!AllowedDubModels.Contains(input.Model))
            throw new ValidationException("Model must be 'eleven_voice_dubbing'.");
        if (string.IsNullOrWhiteSpace(input.TargetLang) || !AllowedLangs.Contains(input.TargetLang))
            throw new ValidationException($"TargetLang must be one of [{string.Join(", ", AllowedLangs)}].");
        if (input.NumSpeakers < 0)
            throw new ValidationException("NumSpeakers must be non-negative if provided.");
    }

    // ---------- DTOs ----------
    [Description("Typed input for Runway text-to-speech generation.")]
    public class RunwayNewTextToSpeech
    {
        [JsonPropertyName("promptText")]
        [Required]
        [Description("Text to convert into speech (1–1000 UTF-16 characters).")]
        public string PromptText { get; set; } = default!;

        [JsonPropertyName("model")]
        [Required]
        [Description("Model name, must be 'eleven_multilingual_v2'.")]
        public string Model { get; set; } = default!;

        [JsonPropertyName("voicePresetId")]
        [Required]
        [Description("Voice preset to use, e.g., Maya, Arjun, Serene, Bernard, etc.")]
        public string VoicePresetId { get; set; } = default!;
    }

    [Description("Typed input for Runway voice dubbing.")]
    public class RunwayNewVoiceDubbing
    {
        [JsonPropertyName("model")]
        [Required]
        [Description("Model name, must be 'eleven_voice_dubbing'.")]
        public string Model { get; set; } = default!;

        [JsonPropertyName("targetLang")]
        [Required]
        [Description("Target language code (e.g., en, fr, es, de, ja).")]
        public string TargetLang { get; set; } = default!;

        [JsonPropertyName("disableVoiceCloning")]
        [Description("Disable voice cloning and use generic voice.")]
        public bool DisableVoiceCloning { get; set; }

        [JsonPropertyName("dropBackgroundAudio")]
        [Description("Remove background audio from the dubbed output.")]
        public bool DropBackgroundAudio { get; set; }

        [JsonPropertyName("numSpeakers")]
        [Description("Number of speakers in the source audio (auto-detected if omitted).")]
        public int? NumSpeakers { get; set; }
    }

    // ---------- SPEECH → SPEECH ----------
    [Description("Convert speech from one voice to another using Runway's speech-to-speech model.")]
    [McpServerTool(Title = "Create Runway Speech-to-Speech", Name = "runway_speech_to_speech", OpenWorld = true, ReadOnly = false, Destructive = false)]
    public static async Task<CallToolResult?> Runway_SpeechToSpeech(
        string mediaType,
        string mediaUri,
        string? voicePresetId,
        bool? removeBackgroundNoise,
        string? model,
        IServiceProvider sp,
        RequestContext<CallToolRequestParams> rc,
        CancellationToken ct = default)
        => await rc.WithExceptionCheck(async () =>
        await rc.WithStructuredContent(async () =>
    {
        var (typed, _, _) = await rc.Server.TryElicit(new RunwayNewSpeechToSpeech
        {
            MediaType = string.IsNullOrWhiteSpace(mediaType) ? "audio" : mediaType!,
            MediaUri = mediaUri,
            Model = string.IsNullOrWhiteSpace(model) ? "eleven_multilingual_sts_v2" : model!,
            VoicePresetId = string.IsNullOrWhiteSpace(voicePresetId) ? "Maya" : voicePresetId!,
            RemoveBackgroundNoise = removeBackgroundNoise ?? false
        }, ct);

        ValidateSts(typed);

        var runway = sp.GetRequiredService<RunwayClient>();
        var payload = new
        {
            model = typed.Model,
            removeBackgroundNoise = typed.RemoveBackgroundNoise,
            media = new
            {
                type = typed.MediaType,
                uri = typed.MediaUri
            },
            voice = new
            {
                type = "runway-preset",
                presetId = typed.VoicePresetId
            }
        };

        return await runway.SpeechToSpeechAsync(payload, ct);
    }));

    private static void ValidateSts(RunwayNewSpeechToSpeech input)
    {
        if (string.IsNullOrWhiteSpace(input.MediaType) || (input.MediaType != "audio" && input.MediaType != "video"))
            throw new ValidationException("MediaType must be 'audio' or 'video'.");
        if (string.IsNullOrWhiteSpace(input.MediaUri))
            throw new ValidationException("MediaUri is required.");
        if (!input.MediaUri.StartsWith("https://") && !input.MediaUri.StartsWith("data:audio/") && !input.MediaUri.StartsWith("data:video/"))
            throw new ValidationException("MediaUri must be a valid HTTPS URL or audio/video data URI.");
        if (string.IsNullOrWhiteSpace(input.Model) || input.Model != "eleven_multilingual_sts_v2")
            throw new ValidationException("Model must be 'eleven_multilingual_sts_v2'.");
        if (string.IsNullOrWhiteSpace(input.VoicePresetId) || !AllowedVoices.Contains(input.VoicePresetId))
            throw new ValidationException($"VoicePresetId must be one of [{string.Join(", ", AllowedVoices)}].");
    }

    [Description("Typed input for Runway speech-to-speech conversion.")]
    public class RunwayNewSpeechToSpeech
    {
        [JsonPropertyName("model")]
        [Required]
        [Description("Model name, must be 'eleven_multilingual_sts_v2'.")]
        public string Model { get; set; } = default!;

        [JsonPropertyName("mediaType")]
        [Required]
        [Description("Media type, must be 'audio' or 'video'.")]
        public string MediaType { get; set; } = default!;

        [JsonPropertyName("mediaUri")]
        [Required]
        [Description("HTTPS URL or data URI containing audio or video.")]
        public string MediaUri { get; set; } = default!;

        [JsonPropertyName("voicePresetId")]
        [Required]
        [Description("Voice preset to use for the generated speech.")]
        public string VoicePresetId { get; set; } = default!;

        [JsonPropertyName("removeBackgroundNoise")]
        [Description("Whether to remove background noise from the generated speech.")]
        public bool RemoveBackgroundNoise { get; set; }
    }

    // ---------- SOUND EFFECT ----------
    [Description("Generate sound effects from a text description using Runway's sound model.")]
    [McpServerTool(Title = "Create Runway Sound Effect", Name = "runway_sound_effect", OpenWorld = true, ReadOnly = false, Destructive = false)]
    public static async Task<CallToolResult?> Runway_SoundEffect(
        string promptText,
        double? duration,
        bool? loop,
        string? model,
        IServiceProvider sp,
        RequestContext<CallToolRequestParams> rc,
        CancellationToken ct = default)
        => await rc.WithExceptionCheck(async () =>
        await rc.WithStructuredContent(async () =>
    {
        var (typed, _, _) = await rc.Server.TryElicit(new RunwayNewSoundEffect
        {
            PromptText = promptText,
            Model = string.IsNullOrWhiteSpace(model) ? "eleven_text_to_sound_v2" : model!,
            Duration = duration,
            Loop = loop ?? false
        }, ct);

        ValidateSound(typed);

        var runway = sp.GetRequiredService<RunwayClient>();
        var payload = new
        {
            model = typed.Model,
            promptText = typed.PromptText,
            duration = typed.Duration,
            loop = typed.Loop
        };

        return await runway.SoundEffectAsync(payload, ct);
    }));

    private static void ValidateSound(RunwayNewSoundEffect input)
    {
        if (string.IsNullOrWhiteSpace(input.PromptText))
            throw new ValidationException("PromptText is required.");
        if (input.PromptText.Length > 3000)
            throw new ValidationException("PromptText must be at most 3000 characters.");
        if (string.IsNullOrWhiteSpace(input.Model) || input.Model != "eleven_text_to_sound_v2")
            throw new ValidationException("Model must be 'eleven_text_to_sound_v2'.");
        if (input.Duration.HasValue && (input.Duration < 0.5 || input.Duration > 30))
            throw new ValidationException("Duration must be between 0.5 and 30 seconds.");
    }

    [Description("Typed input for Runway sound effect generation.")]
    public class RunwayNewSoundEffect
    {
        [JsonPropertyName("model")]
        [Required]
        [Description("Model name, must be 'eleven_text_to_sound_v2'.")]
        public string Model { get; set; } = default!;

        [JsonPropertyName("promptText")]
        [Required]
        [Description("Text description of the sound effect to generate.")]
        public string PromptText { get; set; } = default!;

        [JsonPropertyName("duration")]
        [Description("Duration of the sound in seconds (0.5–30). Optional.")]
        public double? Duration { get; set; }

        [JsonPropertyName("loop")]
        [Description("Whether the output sound effect should loop seamlessly.")]
        public bool Loop { get; set; }
    }


}
