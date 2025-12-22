using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.Runway;

public class RunwayClient
{
    private readonly HttpClient _client;

    public RunwayClient(HttpClient client, RunwaySettings settings)
    {
        _client = client;
        _client.BaseAddress ??= new Uri("https://api.dev.runwayml.com/");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private async Task<JsonNode?> PostAsync(string path, object body, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(body, JsonOpts);
        using var resp = await _client.PostAsync(path, new StringContent(json, Encoding.UTF8, MimeTypes.Json), ct);
        var text = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"{resp.StatusCode}: {text}");

        return JsonNode.Parse(text);
    }

    public Task<JsonNode?> TextToVideoAsync(object body, CancellationToken ct)
        => PostAsync("v1/text_to_video", body, ct);

    public Task<JsonNode?> ImageToVideoAsync(object body, CancellationToken ct)
        => PostAsync("v1/image_to_video", body, ct);

    public Task<JsonNode?> TextToImageAsync(object body, CancellationToken ct)
        => PostAsync("v1/text_to_image", body, ct);

    public Task<JsonNode?> TextToSpeechAsync(object body, CancellationToken ct)
        => PostAsync("v1/text_to_speech", body, ct);

    public Task<JsonNode?> VoiceDubbingAsync(object body, CancellationToken ct)
        => PostAsync("v1/voice_dubbing", body, ct);

    public Task<JsonNode?> VoiceIsolationAsync(object body, CancellationToken ct)
        => PostAsync("v1/voice_isolation", body, ct);

    // Add to RunwayClient
    public Task<JsonNode?> SpeechToSpeechAsync(object body, CancellationToken ct)
        => PostAsync("v1/speech_to_speech", body, ct);

    // Add to RunwayClient
    public Task<JsonNode?> SoundEffectAsync(object body, CancellationToken ct)
        => PostAsync("v1/sound_effect", body, ct);

    // Add to RunwayClient
    public Task<JsonNode?> CharacterPerformanceAsync(object body, CancellationToken ct)
        => PostAsync("v1/character_performance", body, ct);

    // Add to RunwayClient
    public Task<JsonNode?> VideoUpscaleAsync(object body, CancellationToken ct)
        => PostAsync("v1/video_upscale", body, ct);

    public Task<JsonNode?> VideoToVideoAsync(object body, CancellationToken ct)
        => PostAsync("v1/video_to_video", body, ct);

}

public class RunwaySettings
{
    public string ApiKey { get; set; } = default!;
}