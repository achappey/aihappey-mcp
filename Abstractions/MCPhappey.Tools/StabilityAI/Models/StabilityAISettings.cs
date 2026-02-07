using System.ComponentModel;

namespace MCPhappey.Tools.StabilityAI.Models;

public class StabilityAISettings
{
    [Description("Your Stability AI API key.")]
    public string ApiKey { get; set; } = default!;
}
