using System.ComponentModel;
using System.Net.Http.Headers;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.StabilityAI.Enums;
using MCPhappey.Tools.StabilityAI.Models;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.StabilityAI;

public static class StabilityAIImageService
{
    private const string BASE_URL = "https://api.stability.ai/v2beta/stable-image/generate";

    [Description("Generate an image with Stability AI (Image Ultra)")]
    [McpServerTool(Title = "Generate image with Stability AI Image Ultra",
        Name = "stabilityai_image_generation_create_ultra",
        Destructive = false)]
    public static async Task<CallToolResult?> StabilityAI_ImageGeneration_Create_Ultra(
        [Description("Image prompt (English only)")] string prompt,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional image url for image edits. Supports protected links like SharePoint and OneDrive links")]
        string? fileUrl = null,
        [Description("Output filename, without extension")] string? filename = null,
        [Description("Controls the aspect ratio of the generated image.")] AspectRatio? aspectRatio = AspectRatio.Square,

        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
    {
        var downloader = serviceProvider.GetRequiredService<DownloadService>();
        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var items = !string.IsNullOrEmpty(fileUrl) ? await downloader.DownloadContentAsync(serviceProvider,
            requestContext.Server, fileUrl, cancellationToken) : null;
        var file = items?.FirstOrDefault();

        // 1) Get user input via elicitation
        var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
            new StabilityNewImageCore
            {
                Prompt = prompt,
                AspectRatio = aspectRatio ?? AspectRatio.Square,
                Filename = filename?.ToOutputFileName()
                           ?? requestContext.ToOutputFileName()
            },
            cancellationToken);

        // 2) Load API key
        var settings = serviceProvider.GetService<StabilityAISettings>()
            ?? throw new InvalidOperationException("No StabilityAISettings found in service provider");

        using var client = clientFactory.CreateClient();
        using var form = new MultipartFormDataContent();

        // NEVER use collection initializers here. Build every part via helpers above.
        var mode = string.IsNullOrEmpty(fileUrl) ? "text-to-image" : "image-to-image";

        form.Add("prompt".NamedField(typed.Prompt));
        form.Add("output_format".NamedField("png"));

        if (typed.StylePreset.HasValue) form.Add("style_preset".NamedField(typed.StylePreset.Value.GetEnumMemberValue()));

        if (mode == "text-to-image")
            form.Add("aspect_ratio".NamedField(typed.AspectRatio.GetEnumMemberValue() ?? "1:1"));

        if (!string.IsNullOrWhiteSpace(typed.NegativePrompt))
            form.Add("negative_prompt".NamedField(typed.NegativePrompt!));

        //form.Add(NamedField("cfg_scale", typed.CfgScale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));

        if (mode == "image-to-image" && file is not null)
        {
            form.Add("image".NamedFile(file.Contents.ToArray(), file.Filename ?? "input.png", file.MimeType));

            if (typed.Strength.HasValue)
                form.Add("strength".NamedField(typed.Strength.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));
        }

        // 🔎 sanity check before sending (keeps you out of debugger spelunking)
        foreach (var part in form)
        {
            var cd = part.Headers.ContentDisposition;
            if (cd?.Name is null)
                throw new InvalidOperationException($"Form part missing name. Headers: {part.Headers}");
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));

        using var resp = await client.PostAsync(BASE_URL + "/ultra", form, cancellationToken);
        var bytesOut = await resp.Content.ReadAsByteArrayAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"{resp.StatusCode}: {System.Text.Encoding.UTF8.GetString(bytesOut)}");


        var graphItem = await requestContext.Server.Upload(
            serviceProvider,
            $"{typed.Filename}.png",
            BinaryData.FromBytes(bytesOut),
            cancellationToken) ?? throw new Exception("Image upload failed");

        return graphItem.ToCallToolResult();
    });


    [Description("Generate an image with Stability AI (Image Core)")]
    [McpServerTool(Title = "Generate image with Stability AI Image Core",
           Name = "stabilityai_image_generation_create_core",
           Destructive = false)]
    public static async Task<CallToolResult?> StabilityAI_ImageGeneration_Create_Core(
           [Description("Image prompt (English only)")] string prompt,
           IServiceProvider serviceProvider,
           RequestContext<CallToolRequestParams> requestContext,
           [Description("Output filename, without extension")] string? filename = null,
           CancellationToken cancellationToken = default) =>
           await requestContext.WithExceptionCheck(async () =>
       {
           ArgumentNullException.ThrowIfNullOrWhiteSpace(prompt);

           var downloader = serviceProvider.GetRequiredService<DownloadService>();
           var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

           // 1) Get user input via elicitation
           var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
               new StabilityNewImageCore
               {
                   Prompt = prompt,
                   Filename = filename?.ToOutputFileName()
                              ?? requestContext.ToOutputFileName()
               },
               cancellationToken);

           // 2) Load API key
           var settings = serviceProvider.GetService<StabilityAISettings>()
               ?? throw new InvalidOperationException("No StabilityAISettings found in service provider");

           using var client = clientFactory.CreateClient();
           using var form = new MultipartFormDataContent();

           // NEVER use collection initializers here. Build every part via helpers above.

           if (typed.StylePreset.HasValue) form.Add("style_preset".NamedField(typed.StylePreset.Value.GetEnumMemberValue()));

           form.Add("prompt".NamedField(typed.Prompt));
           form.Add("output_format".NamedField("png"));
           form.Add("aspect_ratio".NamedField(typed.AspectRatio.GetEnumMemberValue() ?? "1:1"));

           if (!string.IsNullOrWhiteSpace(typed.NegativePrompt))
               form.Add("negative_prompt".NamedField(typed.NegativePrompt!));

           //form.Add(NamedField("cfg_scale", typed.CfgScale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));

           // 🔎 sanity check before sending (keeps you out of debugger spelunking)
           foreach (var part in form)
           {
               var cd = part.Headers.ContentDisposition;
               if (cd?.Name is null)
                   throw new InvalidOperationException($"Form part missing name. Headers: {part.Headers}");
           }

           client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
           client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));

           using var resp = await client.PostAsync(BASE_URL + "/core", form, cancellationToken);
           var bytesOut = await resp.Content.ReadAsByteArrayAsync(cancellationToken);

           if (!resp.IsSuccessStatusCode)
               throw new Exception($"{resp.StatusCode}: {System.Text.Encoding.UTF8.GetString(bytesOut)}");


           var graphItem = await requestContext.Server.Upload(
               serviceProvider,
               $"{typed.Filename}.png",
               BinaryData.FromBytes(bytesOut),
               cancellationToken) ?? throw new Exception("Image upload failed");
           return graphItem.ToCallToolResult();
       });


    [Description("Generate an image with Stability AI (Stable Diffusion 3.5)")]
    [McpServerTool(Title = "Generate image with Stability AI",
        Name = "stabilityai_image_generation_create_stable_diffusion",
        Destructive = false)]
    public static async Task<CallToolResult?> StabilityAI_ImageGeneration_Create_Stable_Diffusion(
        [Description("Image prompt (English only)")] string prompt,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Stable Diffusion 3.5 model to use")] StabilityAISD3ModelType model = StabilityAISD3ModelType.flash,
        [Description("Optional image url for image edits. Supports protected links like SharePoint and OneDrive links")]
        string? fileUrl = null,
        [Description("Output filename, without extension")] string? filename = null,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
    {
        var downloader = serviceProvider.GetRequiredService<DownloadService>();
        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var items = !string.IsNullOrEmpty(fileUrl) ? await downloader.DownloadContentAsync(serviceProvider,
            requestContext.Server, fileUrl, cancellationToken) : null;
        var file = items?.FirstOrDefault();

        // 1) Get user input via elicitation
        var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
            new StabilityNewImage
            {
                Prompt = prompt,
                Model = model,
                Filename = filename?.ToOutputFileName()
                           ?? requestContext.ToOutputFileName()
            },
            cancellationToken);

        var settings = serviceProvider.GetService<StabilityAISettings>()
            ?? throw new InvalidOperationException("No StabilityAISettings found in service provider");

        using var client = clientFactory.CreateClient();
        using var form = new MultipartFormDataContent();

        // NEVER use collection initializers here. Build every part via helpers above.
        var mode = string.IsNullOrEmpty(fileUrl) ? "text-to-image" : "image-to-image";

        form.Add("prompt".NamedField(typed.Prompt));
        form.Add("mode".NamedField(mode));
        form.Add("model".NamedField(typed.Model.GetEnumMemberValue() ?? "sd3.5-flash"));
        form.Add("output_format".NamedField("png"));
        if (typed.StylePreset.HasValue) form.Add("style_preset".NamedField(typed.StylePreset.Value.GetEnumMemberValue()));

        if (mode == "text-to-image")
            form.Add("aspect_ratio".NamedField(typed.AspectRatio ?? "1:1"));

        if (!string.IsNullOrWhiteSpace(typed.NegativePrompt))
            form.Add("negative_prompt".NamedField(typed.NegativePrompt!));

        form.Add("cfg_scale".NamedField(typed.CfgScale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));

        if (mode == "image-to-image" && file is not null)
        {
            form.Add("image".NamedFile(file.Contents.ToArray(), file.Filename ?? "input.png", file.MimeType));

            if (typed.Strength.HasValue)
                form.Add("strength".NamedField(typed.Strength.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));
        }

        // 🔎 sanity check before sending (keeps you out of debugger spelunking)
        foreach (var part in form)
        {
            var cd = part.Headers.ContentDisposition;
            if (cd?.Name is null)
                throw new InvalidOperationException($"Form part missing name. Headers: {part.Headers}");
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));

        using var resp = await client.PostAsync(BASE_URL + "/sd3", form, cancellationToken);
        var bytesOut = await resp.Content.ReadAsByteArrayAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"{resp.StatusCode}: {System.Text.Encoding.UTF8.GetString(bytesOut)}");

        var graphItem = await requestContext.Server.Upload(
            serviceProvider,
            $"{typed.Filename}.png",
            BinaryData.FromBytes(bytesOut),
            cancellationToken) ?? throw new Exception("Image upload failed");

        return graphItem?.ToCallToolResult();
    });

}
