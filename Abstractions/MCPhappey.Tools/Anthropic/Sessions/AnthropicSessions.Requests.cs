using System.ComponentModel;
using System.Text.Json.Serialization;
using MCPhappey.Common.Models;

namespace MCPhappey.Tools.Anthropic.Sessions;

public static partial class AnthropicSessions
{
    [Description("Please confirm to delete: {0}")]
    public sealed class AnthropicDeleteSessionItem : IHasName
    {
        [JsonPropertyName("name")]
        [Description("Confirm deletion.")]
        public string Name { get; set; } = string.Empty;
    }
}
