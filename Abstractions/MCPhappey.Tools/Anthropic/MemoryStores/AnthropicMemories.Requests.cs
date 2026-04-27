using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MCPhappey.Common.Models;

namespace MCPhappey.Tools.Anthropic.MemoryStores;

public static partial class AnthropicMemories
{
    [Description("Please confirm to delete: {0}")]
    public sealed class AnthropicDeleteMemoryItem : IHasName
    {
        [JsonPropertyName("name")]
        [Description("Confirm deletion.")]
        public string Name { get; set; } = string.Empty;
    }

    [Description("Please confirm the Anthropic memory create request.")]
    public sealed class AnthropicCreateMemoryRequest
    {
        [JsonPropertyName("memoryStoreId")]
        [Required]
        [Description("Memory store ID.")]
        public string MemoryStoreId { get; set; } = string.Empty;

        [JsonPropertyName("path")]
        [Required]
        [Description("Memory path.")]
        public string Path { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        [Required]
        [Description("Memory content.")]
        public string Content { get; set; } = string.Empty;

        [JsonPropertyName("view")]
        [Description("Optional response view. Allowed values: basic or full.")]
        public string? View { get; set; }

        [JsonPropertyName("anthropicBetaCsv")]
        [Description("Optional extra anthropic-beta values as comma, semicolon, or newline separated strings.")]
        public string? AnthropicBetaCsv { get; set; }
    }

    [Description("Please confirm the Anthropic memory update request.")]
    public sealed class AnthropicUpdateMemoryRequest
    {
        [JsonPropertyName("memoryStoreId")]
        [Required]
        [Description("Memory store ID.")]
        public string MemoryStoreId { get; set; } = string.Empty;

        [JsonPropertyName("memoryId")]
        [Required]
        [Description("Memory ID.")]
        public string MemoryId { get; set; } = string.Empty;

        [JsonPropertyName("path")]
        [Description("Optional updated memory path. Omit to preserve.")]
        public string? Path { get; set; }

        [JsonPropertyName("content")]
        [Description("Optional updated memory content. Omit to preserve.")]
        public string? Content { get; set; }

        [JsonPropertyName("expectedContentSha256")]
        [Description("Optional content SHA256 precondition.")]
        public string? ExpectedContentSha256 { get; set; }

        [JsonPropertyName("view")]
        [Description("Optional response view. Allowed values: basic or full.")]
        public string? View { get; set; }

        [JsonPropertyName("anthropicBetaCsv")]
        [Description("Optional extra anthropic-beta values as comma, semicolon, or newline separated strings.")]
        public string? AnthropicBetaCsv { get; set; }
    }
}

