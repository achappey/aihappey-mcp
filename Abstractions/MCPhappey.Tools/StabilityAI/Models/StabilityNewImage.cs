using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MCPhappey.Tools.StabilityAI.Enums;

namespace MCPhappey.Tools.StabilityAI.Models;

[Description("Please fill in the Stability AI image generation request.")]
public class StabilityNewImage
{
    [Required]
    [JsonPropertyName("prompt")]
    [Description("Prompt text for the image. English only.")]
    public string Prompt { get; set; } = default!;

    [JsonPropertyName("cfg_scale")]
    [Range(1, 10)]
    [Required]
    [Description("Prompt adherence strength (1–10).")]
    public int CfgScale { get; set; } = 4;

    [JsonPropertyName("model")]
    [Required]
    [Description("Stable Diffusion model version.")]
    [DefaultValue("sd3.5-large")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public StabilityAISD3ModelType Model { get; set; }

    [JsonPropertyName("filename")]
    [Description("Output file name, without extension.")]
    public string Filename { get; set; } = default!;

    [JsonPropertyName("aspect_ratio")]
    [Description("Aspect ratio, e.g. 1:1, 16:9, 3:2, etc.")]
    [DefaultValue("1:1")]
    public string? AspectRatio { get; set; }

    [JsonPropertyName("negative_prompt")]
    [Description("What NOT to see in the image.")]
    public string? NegativePrompt { get; set; }

    [JsonPropertyName("style_preset")]
    [Description("Optional style (e.g., anime, cinematic, fantasy-art, etc.).")]
    public StylePreset? StylePreset { get; set; }

    [JsonPropertyName("strength")]
    [Range(0, 1)]
    [Description("Image-to-image influence (0–1).")]
    public double? Strength { get; set; }

}
