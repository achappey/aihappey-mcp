using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Gradium;

public static class GradiumVoices
{
    private const string VoicesUrl = "https://eu.api.gradium.ai/voices/";

    [Description("Create a new custom Gradium voice with audio_file from fileUrl.")]
    [McpServerTool(
        Title = "Gradium Create Voice",
        Name = "gradium_voices_create",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Gradium_Voices_Create(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Audio file URL to upload for voice cloning.")] string fileUrl,
        [Description("Voice name.")] string name,
        [Description("Optional input format.")] string? input_format = null,
        [Description("Optional description.")] string? description = null,
        [Description("Optional language.")] string? language = null,
        [Description("Audio start offset in seconds. Default: 0.")] double start_s = 0,
        [Description("Timeout in seconds. Default: 10.")] double timeout_s = 10,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new GradiumCreateVoiceRequest
                {
                    FileUrl = fileUrl,
                    Name = name,
                    InputFormat = string.IsNullOrWhiteSpace(input_format) ? null : input_format.Trim(),
                    Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                    Language = string.IsNullOrWhiteSpace(language) ? null : language.Trim(),
                    StartS = start_s,
                    TimeoutS = timeout_s
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            var downloadService = serviceProvider.GetRequiredService<DownloadService>();
            var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, typed.FileUrl, cancellationToken);
            var sourceFile = files.FirstOrDefault() ?? throw new InvalidOperationException("No downloadable voice audio file found at fileUrl.");

            ValidateCreateVoice(typed, sourceFile.Filename);

            var settings = serviceProvider.GetRequiredService<GradiumSettings>();
            var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

            using var content = new MultipartFormDataContent();
            var audioContent = new ByteArrayContent(sourceFile.Contents.ToArray());
            audioContent.Headers.ContentType = MediaTypeHeaderValue.Parse(sourceFile.MimeType ?? "application/octet-stream");
            content.Add(audioContent, "audio_file", sourceFile.Filename ?? "voice-sample.bin");
            content.Add(new StringContent(typed.Name, Encoding.UTF8), "name");
            content.Add(new StringContent(typed.StartS.ToString(System.Globalization.CultureInfo.InvariantCulture), Encoding.UTF8), "start_s");
            content.Add(new StringContent(typed.TimeoutS.ToString(System.Globalization.CultureInfo.InvariantCulture), Encoding.UTF8), "timeout_s");

            if (!string.IsNullOrWhiteSpace(typed.InputFormat))
                content.Add(new StringContent(typed.InputFormat, Encoding.UTF8), "input_format");
            if (!string.IsNullOrWhiteSpace(typed.Description))
                content.Add(new StringContent(typed.Description, Encoding.UTF8), "description");
            if (!string.IsNullOrWhiteSpace(typed.Language))
                content.Add(new StringContent(typed.Language, Encoding.UTF8), "language");

            using var client = clientFactory.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Post, VoicesUrl);
            req.Headers.Add("x-api-key", settings.ApiKey);
            req.Content = content;

            using var resp = await client.SendAsync(req, cancellationToken);
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"Gradium create voice failed ({(int)resp.StatusCode}): {body}");

            return body.ToJsonContent(VoicesUrl).ToCallToolResult();
        });
   
    [Description("Update a Gradium voice by UID.")]
    [McpServerTool(
        Title = "Gradium Update Voice",
        Name = "gradium_voices_update",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Gradium_Voices_Update(
        [Description("Voice UID.")] string voice_uid,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional updated name.")] string? name = null,
        [Description("Optional updated description.")] string? description = null,
        [Description("Optional updated language.")] string? language = null,
        [Description("Optional updated start offset in seconds.")] double? start_s = null,
        [Description("Optional numeric rank.")] double? rank = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(voice_uid);

            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new GradiumUpdateVoiceRequest
                {
                    VoiceUid = voice_uid,
                    Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
                    Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                    Language = string.IsNullOrWhiteSpace(language) ? null : language.Trim(),
                    StartS = start_s,
                    Rank = rank
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            var settings = serviceProvider.GetRequiredService<GradiumSettings>();
            var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var url = $"{VoicesUrl}{typed.VoiceUid.Trim()}";

            var payload = new Dictionary<string, object?>();
            if (typed.Name != null) payload["name"] = typed.Name;
            if (typed.Description != null) payload["description"] = typed.Description;
            if (typed.Language != null) payload["language"] = typed.Language;
            if (typed.StartS.HasValue) payload["start_s"] = typed.StartS.Value;
            if (typed.Rank.HasValue) payload["rank"] = typed.Rank.Value;

            if (payload.Count == 0)
                throw new ValidationException("At least one field must be provided for update.");

            using var client = clientFactory.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Put, url);
            req.Headers.Add("x-api-key", settings.ApiKey);
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var resp = await client.SendAsync(req, cancellationToken);
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"Gradium update voice failed ({(int)resp.StatusCode}): {body}");

            return body.ToJsonContent(url).ToCallToolResult();
        });

    [Description("Delete a Gradium voice by UID using the default delete confirmation flow.")]
    [McpServerTool(
        Title = "Gradium Delete Voice",
        Name = "gradium_voices_delete",
        Destructive = true,
        OpenWorld = true,
        ReadOnly = false)]
    public static async Task<CallToolResult?> Gradium_Voices_Delete(
        [Description("Voice UID to delete.")] string voice_uid,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(voice_uid);

            var settings = serviceProvider.GetRequiredService<GradiumSettings>();
            var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var normalizedVoiceUid = voice_uid.Trim();

            return await requestContext.ConfirmAndDeleteAsync<ConfirmDeleteGradiumVoice>(
                normalizedVoiceUid,
                async ct =>
                {
                    using var client = clientFactory.CreateClient();
                    using var req = new HttpRequestMessage(HttpMethod.Delete, $"{VoicesUrl}{normalizedVoiceUid}");
                    req.Headers.Add("x-api-key", settings.ApiKey);

                    using var resp = await client.SendAsync(req, ct);
                    var body = await resp.Content.ReadAsStringAsync(ct);
                    if (!resp.IsSuccessStatusCode)
                        throw new InvalidOperationException($"Gradium delete voice failed ({(int)resp.StatusCode}): {body}");
                },
                $"Voice '{normalizedVoiceUid}' deleted successfully.",
                cancellationToken);
        });    


    private static void ValidateCreateVoice(GradiumCreateVoiceRequest request, string? sourceFileName)
    {
        if (string.IsNullOrWhiteSpace(request.FileUrl))
            throw new ValidationException("fileUrl is required.");

        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ValidationException("name is required.");

        if (string.IsNullOrWhiteSpace(sourceFileName))
            throw new ValidationException("audio_file is required.");
    }

    [Description("Please fill in the Gradium create voice request.")]
    public sealed class GradiumCreateVoiceRequest
    {
        [JsonPropertyName("fileUrl")]
        [Required]
        [Description("Audio file URL to upload for voice cloning.")]
        public string FileUrl { get; set; } = default!;

        [JsonPropertyName("name")]
        [Required]
        [Description("Voice name.")]
        public string Name { get; set; } = default!;

        [JsonPropertyName("input_format")]
        [Description("Optional input format.")]
        public string? InputFormat { get; set; }

        [JsonPropertyName("description")]
        [Description("Optional description.")]
        public string? Description { get; set; }

        [JsonPropertyName("language")]
        [Description("Optional language.")]
        public string? Language { get; set; }

        [JsonPropertyName("start_s")]
        [Description("Audio start offset in seconds.")]
        public double StartS { get; set; } = 0;

        [JsonPropertyName("timeout_s")]
        [Description("Timeout in seconds.")]
        public double TimeoutS { get; set; } = 10;
    }

    [Description("Please fill in the Gradium update voice request.")]
    public sealed class GradiumUpdateVoiceRequest
    {
        [JsonPropertyName("voice_uid")]
        [Required]
        [Description("Voice UID.")]
        public string VoiceUid { get; set; } = default!;

        [JsonPropertyName("name")]
        [Description("Updated name.")]
        public string? Name { get; set; }

        [JsonPropertyName("description")]
        [Description("Updated description.")]
        public string? Description { get; set; }

        [JsonPropertyName("language")]
        [Description("Updated language.")]
        public string? Language { get; set; }

        [JsonPropertyName("start_s")]
        [Description("Updated start offset.")]
        public double? StartS { get; set; }

        [JsonPropertyName("rank")]
        [Description("Updated rank.")]
        public double? Rank { get; set; }
    }

    [Description("Please confirm deletion of the voice UID: {0}")]
    public sealed class ConfirmDeleteGradiumVoice : IHasName
    {
        [Required]
        [JsonPropertyName("name")]
        [Description("Type the exact voice UID to confirm deletion.")]
        public string? Name { get; set; }
    }
}
