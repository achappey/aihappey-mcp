using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Google.Image;

public static class GoogleImagen
{
    [Description("Create a image with Google Imagen image generator")]
    [McpServerTool(Title = "Generate image with Google Imagen", Destructive = false)]
    public static async Task<CallToolResult?> GoogleImagen_CreateImage(
        [Description("Image prompt (only English)")]
        string prompt,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("AI image model")]
        Model? imageModel = Model.imagen40generate001,
        [Description("The aspect ratio of the generated image.")]
        Mscc.GenerativeAI.ImageAspectRatio? aspectRatio = Mscc.GenerativeAI.ImageAspectRatio.Ratio1x1,
        [Description("The number of images to generate.")]
        [Range(1, 4)]
        int numberOfImages = 1,
        [Description("New image file name, without extension")]
        string? filename = null,
        [Description("Sample image size. 1K or 2K")]
        string? sampleImageSize = "1K",
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
    {
        ArgumentNullException.ThrowIfNullOrWhiteSpace(prompt);
        var googleAI = serviceProvider.GetRequiredService<Mscc.GenerativeAI.GoogleAI>();


        var (typed, notAccepted, result) = await requestContext.Server.TryElicit(
                       new GoogleImagenNewImage
                       {
                           Prompt = prompt,
                           Model = imageModel ?? Model.imagen40generate001,
                           NumberOfImages = numberOfImages,
                           SampleImageSize = sampleImageSize,
                           AspectRatio = aspectRatio ?? Mscc.GenerativeAI.ImageAspectRatio.Ratio1x1,
                           Filename = filename?.ToOutputFileName()
                            ?? requestContext.ToOutputFileName()
                       },
                       cancellationToken);

        if (notAccepted != null) return notAccepted;
        if (typed == null) return "Something went wrong".ToErrorCallToolResponse();
        var model = typed.Model;
        var modelString = model.GetEnumMemberValue();
        var imageClient = googleAI.ImageGenerationModel(modelString);

        var item = await imageClient.GenerateImages(new(typed.Prompt, typed.NumberOfImages)
        {
            Parameters = new()
            {
                SampleCount = typed.NumberOfImages,
                AspectRatio = typed.AspectRatio,
                SampleImageSize = typed.SampleImageSize,
                PersonGeneration = Mscc.GenerativeAI.PersonGeneration.AllowAdult,
                OutputOptions = new()
                {
                    MimeType = MediaTypeNames.Image.Png
                }
            }
        }, new Mscc.GenerativeAI.RequestOptions(), cancellationToken: cancellationToken);

        List<ResourceLinkBlock> resourceLinks = [];

        foreach (var imageItem in item.Predictions)
        {
            var graphItem = await requestContext.Server.Upload(serviceProvider,
                 $"{typed?.Filename}.png",
                BinaryData.FromBytes(Convert.FromBase64String(imageItem.BytesBase64Encoded!)), cancellationToken);

            if (graphItem != null) resourceLinks.Add(graphItem);
        }

        return resourceLinks?.ToResourceLinkCallToolResponse();
    });


    [Description("Please fill in the AI image request details.")]
    public class GoogleImagenNewImage
    {
        [JsonPropertyName("prompt")]
        [Required]
        [Description("The image prompt. English prompts only")]
        public string Prompt { get; set; } = default!;

        [JsonPropertyName("filename")]
        [Required]
        [Description("The new image file name.")]
        public string Filename { get; set; } = default!;

        [JsonPropertyName("model")]
        [Required]
        [Description("The AI image model.")]
        public Model Model { get; set; } = Model.imagen40generate001;

        [JsonPropertyName("aspectRatio")]
        [Required]
        [Description("The image aspect ratio.")]
        public Mscc.GenerativeAI.ImageAspectRatio AspectRatio { get; set; } = Mscc.GenerativeAI.ImageAspectRatio.Ratio1x1;

        [JsonPropertyName("numberOfImages")]
        [Required]
        [Range(1, 4)]
        [Description("The number of images to create.")]
        public int NumberOfImages { get; set; } = 1;

        [JsonPropertyName("sampleImageSize")]
        [Description("Specifies the generated image's output resolution. 1K or 2K")]
        public string? SampleImageSize { get; set; } = "1K";
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum Model
    {
        [EnumMember(Value = "imagen-4.0-generate-001")]
        imagen40generate001,
        [EnumMember(Value = "imagen-4.0-ultra-generate-001")]
        imagen40ultragenerate001,
        [EnumMember(Value = "imagen-4.0-fast-generate-001")]
        imagen40fastgenerate001,
    }


}

