using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MCPhappey.Tools.Anthropic.MemoryStores;

public static partial class AnthropicMemoryVersions
{
    [Description("Please confirm the Anthropic memory version redact request.")]
    public sealed class AnthropicRedactMemoryVersionRequest
    {
        [JsonPropertyName("memoryStoreId")]
        [Required]
        [Description("Memory store ID.")]
        public string MemoryStoreId { get; set; } = string.Empty;

        [JsonPropertyName("memoryVersionId")]
        [Required]
        [Description("Memory version ID.")]
        public string MemoryVersionId { get; set; } = string.Empty;

        [JsonPropertyName("anthropicBetaCsv")]
        [Description("Optional extra anthropic-beta values as comma, semicolon, or newline separated strings.")]
        public string? AnthropicBetaCsv { get; set; }
    }
}

