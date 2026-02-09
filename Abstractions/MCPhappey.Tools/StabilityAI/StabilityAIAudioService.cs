using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.StabilityAI.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.StabilityAI;

public static class StabilityAIAudioService
{
    private const string BASE_URL = "https://api.stability.ai/v2beta/audio/stable-audio-2";

    [Description("Generate high-quality music or sound effects from text using Stability AI’s Stable Audio.")]
    [McpServerTool(
           Title = "Text-to-Audio generation with Stability AI",
           Name = "stabilityai_audio_text_to_audio",
           Destructive = false)]
    public static async Task<CallToolResult?> StabilityAI_Audio_TextToAudio(
           IServiceProvider serviceProvider,
           RequestContext<CallToolRequestParams> requestContext,
           [Description("Text prompt describing the desired music or sound (genre, instruments, mood, etc.).")] string prompt,
           [Description("Output filename without extension.")] string? filename = null,
           CancellationToken cancellationToken = default) =>
           await requestContext.WithExceptionCheck(async () =>
           {
               var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

               // 1️⃣ Get user input via elicitation
               var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                   new StabilityAudioTextToAudio
                   {
                       Prompt = prompt,
                       Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                   },
                   cancellationToken);

               // 2️⃣ Load API key
               var settings = serviceProvider.GetService<StabilityAISettings>()
                   ?? throw new InvalidOperationException("No StabilityAISettings found in service provider");

               using var client = clientFactory.CreateClient();
               using var form = new MultipartFormDataContent
               {
                   // Required field
                   "prompt".NamedField(typed.Prompt),

                   // Optional fields
                   "duration".NamedField(typed.Duration.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)),
                   "steps".NamedField(typed.Steps.ToString()),
                   "cfg_scale".NamedField(typed.CfgScale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)),
                   "model".NamedField("stable-audio-2-5")
               };

               if (typed.Seed.HasValue && typed.Seed > 0)
                   form.Add("seed".NamedField(typed.Seed.ToString()!));

               form.Add("output_format".NamedField("mp3"));

               // Sanity check
               foreach (var part in form)
               {
                   var cd = part.Headers.ContentDisposition;
                   if (cd?.Name is null)
                       throw new InvalidOperationException($"Form part missing name. Headers: {part.Headers}");
               }

               // 3️⃣ Headers
               client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
               client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/*"));

               // 4️⃣ POST request
               using var resp = await client.PostAsync(BASE_URL + "/text-to-audio", form, cancellationToken);
               var bytesOut = await resp.Content.ReadAsByteArrayAsync(cancellationToken);

               if (!resp.IsSuccessStatusCode)
                   throw new Exception($"{resp.StatusCode}: {System.Text.Encoding.UTF8.GetString(bytesOut)}");

               // 5️⃣ Upload generated audio to graph
               var graphItem = await requestContext.Server.Upload(
                   serviceProvider,
                   $"{typed.Filename}.{"mp3"}",
                   BinaryData.FromBytes(bytesOut),
                   cancellationToken) ?? throw new Exception("Audio upload failed");

               // 6️⃣ Return as result
               return new CallToolResult
               {
                   Content = [
                       graphItem,
                        new AudioContentBlock
                        {
                            Data = Convert.ToBase64String(bytesOut),
                            MimeType = MimeTypes.AudioMP3
                        }
                   ]
               };
           });

    [Description("Transform existing audio into new compositions using Stability AI’s Audio-to-Audio model.")]
    [McpServerTool(
           Title = "Audio-to-Audio transformation with Stability AI",
           Name = "stabilityai_audio_to_audio",
           Destructive = false)]
    public static async Task<CallToolResult?> StabilityAI_Audio_ToAudio(
           IServiceProvider serviceProvider,
           RequestContext<CallToolRequestParams> requestContext,
           [Description("URL of the source audio file to transform.")] string audioUrl,
           [Description("Text instruction describing how to transform the source audio.")] string prompt,
           [Description("Output filename without extension.")] string? filename = null,
           CancellationToken cancellationToken = default) =>
           await requestContext.WithExceptionCheck(async () =>
           {
               var downloader = serviceProvider.GetRequiredService<DownloadService>();
               var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

               // 1️⃣ Download source audio
               var audioItems = await downloader.DownloadContentAsync(serviceProvider, requestContext.Server, audioUrl, cancellationToken);
               var audioFile = audioItems.FirstOrDefault() ?? throw new InvalidOperationException("Failed to download source audio");

               // 2️⃣ Elicit transformation settings
               var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                   new StabilityAudioToAudio
                   {
                       Prompt = prompt,
                       Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                   },
                   cancellationToken);

               // 3️⃣ Load API key
               var settings = serviceProvider.GetService<StabilityAISettings>()
                   ?? throw new InvalidOperationException("No StabilityAISettings found in service provider");

               using var client = clientFactory.CreateClient();
               using var form = new MultipartFormDataContent();

               // Required fields
               form.Add("audio".NamedFile(audioFile.Contents.ToArray(), audioFile.Filename ?? "input.mp3", audioFile.MimeType));
               form.Add("prompt".NamedField(typed.Prompt));

               // Optional parameters
               form.Add("duration".NamedField(typed.Duration.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));
               form.Add("steps".NamedField(typed.Steps.ToString()));
               form.Add("cfg_scale".NamedField(typed.CfgScale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));

               if (typed.Strength.HasValue)
                   form.Add("strength".NamedField(typed.Strength.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));

               form.Add("model".NamedField("stable-audio-2-5"));

               if (typed.Seed.HasValue && typed.Seed > 0)
                   form.Add("seed".NamedField(typed.Seed.ToString()!));

               form.Add("output_format".NamedField("mp3"));

               // Sanity check
               foreach (var part in form)
               {
                   var cd = part.Headers.ContentDisposition;
                   if (cd?.Name is null)
                       throw new InvalidOperationException($"Form part missing name. Headers: {part.Headers}");
               }

               // 4️⃣ Headers
               client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
               client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/*"));

               // 5️⃣ POST request
               using var resp = await client.PostAsync($"{BASE_URL}/audio-to-audio", form, cancellationToken);
               var bytesOut = await resp.Content.ReadAsByteArrayAsync(cancellationToken);

               if (!resp.IsSuccessStatusCode)
                   throw new Exception($"{resp.StatusCode}: {System.Text.Encoding.UTF8.GetString(bytesOut)}");

               // 6️⃣ Upload result to Graph
               var graphItem = await requestContext.Server.Upload(
                   serviceProvider,
                   $"{typed.Filename}.{"mp3"}",
                   BinaryData.FromBytes(bytesOut),
                   cancellationToken) ?? throw new Exception("Audio upload failed");

               // 7️⃣ Return to client
               return new CallToolResult
               {
                   Content = [
                       graphItem,
                        new AudioContentBlock
                        {
                            Data = Convert.ToBase64String(bytesOut),
                            MimeType = MimeTypes.AudioMP3
                        }
                   ]
               };
           });

    [Description("Inpaint or regenerate specific segments of existing audio using Stability AI’s audio model.")]
    [McpServerTool(
        Title = "Audio Inpainting with Stability AI",
        Name = "stabilityai_audio_inpaint",
        Destructive = false)]
    public static async Task<CallToolResult?> StabilityAI_Audio_Inpaint(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("URL of the source audio file to modify.")] string audioUrl,
        [Description("Prompt describing what should be generated in the masked region.")] string prompt,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(audioUrl);
            ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

            var downloader = serviceProvider.GetRequiredService<DownloadService>();
            var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

            // 1️⃣ Download source audio
            var audioItems = await downloader.DownloadContentAsync(serviceProvider, requestContext.Server, audioUrl, cancellationToken);
            var audioFile = audioItems.FirstOrDefault() ?? throw new InvalidOperationException("Failed to download source audio");

            // 2️⃣ Elicit audio inpaint parameters
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new StabilityAudioInpaint
                {
                    AudioUrl = audioUrl,
                    Prompt = prompt,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            // 3️⃣ Load API key
            var settings = serviceProvider.GetService<StabilityAISettings>()
                ?? throw new InvalidOperationException("No StabilityAISettings found in service provider");

            using var client = clientFactory.CreateClient();
            using var form = new MultipartFormDataContent();

            // Required fields
            form.Add("audio".NamedFile(audioFile.Contents.ToArray(), audioFile.Filename ?? "input.mp3", audioFile.MimeType));
            form.Add("prompt".NamedField(typed.Prompt));

            // Optional fields
            if (typed.Duration.HasValue)
                form.Add("duration".NamedField(typed.Duration.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));

            if (typed.MaskStart.HasValue)
                form.Add("mask_start".NamedField(typed.MaskStart.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));

            if (typed.MaskEnd.HasValue)
                form.Add("mask_end".NamedField(typed.MaskEnd.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));

            if (typed.Steps.HasValue)
                form.Add("steps".NamedField(typed.Steps.Value.ToString()));

            if (typed.Seed.HasValue && typed.Seed > 0)
                form.Add("seed".NamedField(typed.Seed.ToString()!));

            form.Add("output_format".NamedField("mp3"));

            // Sanity check
            foreach (var part in form)
            {
                var cd = part.Headers.ContentDisposition;
                if (cd?.Name is null)
                    throw new InvalidOperationException($"Form part missing name. Headers: {part.Headers}");
            }

            // 4️⃣ Headers
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/*"));

            // 5️⃣ POST request
            using var resp = await client.PostAsync($"{BASE_URL}/inpaint", form, cancellationToken);
            var bytesOut = await resp.Content.ReadAsByteArrayAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"{resp.StatusCode}: {System.Text.Encoding.UTF8.GetString(bytesOut)}");

            // 6️⃣ Upload result
            var graphItem = await requestContext.Server.Upload(
                serviceProvider,
                $"{typed.Filename}.{"mp3"}",
                BinaryData.FromBytes(bytesOut),
                cancellationToken) ?? throw new Exception("Audio upload failed");

            // 7️⃣ Return final result
            return new CallToolResult
            {
                Content = [
                    graphItem,
                            new AudioContentBlock
                            {
                                Data = Convert.ToBase64String(bytesOut),
                                MimeType = MimeTypes.AudioMP3
                            }
                ]
            };
        });

    [Description("Please fill in the Stability AI audio-to-audio transformation request.")]
    public class StabilityAudioToAudio
    {
        [Required]
        [JsonPropertyName("prompt")]
        [Description("Text instruction describing how the source audio should be transformed.")]
        public string Prompt { get; set; } = default!;

        [JsonPropertyName("duration")]
        [Range(1, 190)]
        [Required]
        [Description("Length of the generated audio in seconds (1–190). Default: 190.")]
        public int Duration { get; set; } = 190;

        [JsonPropertyName("steps")]
        [Required]
        [Range(4, 8)]
        [Description("Number of diffusion steps (30–100 for v2, 4–8 for v2.5). Default depends on model.")]
        public int Steps { get; set; } = 8;

        [JsonPropertyName("cfg_scale")]
        [Required]
        [Range(1, 25)]
        [Description("How strictly the model follows your prompt text. Higher = closer adherence.")]
        public int CfgScale { get; set; } = 1;

        [JsonPropertyName("strength")]
        [Range(0.01, 1)]
        [Required]
        [Description("Influence of the original audio (0 = identical to input, 1 = fully reimagined).")]
        public double? Strength { get; set; } = 1;

        [JsonPropertyName("seed")]
        [Range(0, uint.MaxValue - 1)]
        [Description("Randomness seed (0 = random).")]
        public uint? Seed { get; set; }

        [JsonPropertyName("filename")]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }

    [Description("Please fill in the Stability AI text-to-audio generation request.")]
    public class StabilityAudioTextToAudio
    {
        [Required]
        [JsonPropertyName("prompt")]
        [Description("Text description for the audio generation. Describe instruments, mood, tempo, and style.")]
        public string Prompt { get; set; } = default!;

        [JsonPropertyName("duration")]
        [Required]
        [Range(1, 190)]
        [Description("Length of the generated audio in seconds (1–190). Default: 190.")]
        public double Duration { get; set; } = 190;

        [JsonPropertyName("steps")]
        [Required]
        [Range(4, 8)]
        [Description("Number of diffusion steps (30–100 for v2, 4–8 for v2.5). Default depends on model.")]
        public int Steps { get; set; } = 8;

        [JsonPropertyName("cfg_scale")]
        [Required]
        [Range(1, 25)]
        [Description("How strictly the model follows your prompt text. Higher = closer adherence.")]
        public int CfgScale { get; set; } = 1;

        [JsonPropertyName("seed")]
        [Range(0, uint.MaxValue - 1)]
        [Description("Randomness seed (0 = random).")]
        public uint? Seed { get; set; }

        [JsonPropertyName("filename")]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }


    [Description("Please fill in the Stability AI audio inpainting request.")]
    public class StabilityAudioInpaint
    {
        [Required]
        [JsonPropertyName("audio_url")]
        [Description("URL of the input audio file (SharePoint, OneDrive, or public).")]
        public string AudioUrl { get; set; } = default!;

        [Required]
        [JsonPropertyName("prompt")]
        [Description("Describe the audio that should fill the missing or replaced section.")]
        public string Prompt { get; set; } = default!;

        [JsonPropertyName("duration")]
        [Range(1, 190)]
        [Description("Total duration of the generated audio (in seconds). Default: 190.")]
        public double? Duration { get; set; }

        [JsonPropertyName("mask_start")]
        [Range(0, 190)]
        [Description("Start time in seconds for the segment to be inpainted.")]
        public double? MaskStart { get; set; }

        [JsonPropertyName("mask_end")]
        [Range(0, 190)]
        [Description("End time in seconds for the segment to be inpainted.")]
        public double? MaskEnd { get; set; }

        [JsonPropertyName("steps")]
        [Range(4, 8)]
        [Description("Number of sampling steps (4–8). Default: 8.")]
        public int? Steps { get; set; }

        [JsonPropertyName("seed")]
        [Range(0, uint.MaxValue - 1)]
        [Description("Randomness seed (0 = random).")]
        public uint? Seed { get; set; }

        [JsonPropertyName("filename")]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }
}
