using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MCPhappey.Common.Models;

namespace MCPhappey.Tools.Anthropic.MemoryStores;

public static partial class AnthropicMemoryStores
{
    [Description("Please confirm to delete: {0}")]
    public sealed class AnthropicDeleteMemoryStoreItem : IHasName
    {
        [JsonPropertyName("name")]
        [Description("Confirm deletion.")]
        public string Name { get; set; } = string.Empty;
    }

    public abstract class AnthropicMemoryStoreBetaRequestBase
    {
        [JsonPropertyName("anthropicBetaCsv")]
        [Description("Optional extra anthropic-beta values as comma, semicolon, or newline separated strings.")]
        public string? AnthropicBetaCsv { get; set; }
    }

    [Description("Please confirm the Anthropic memory store create request.")]
    public sealed class AnthropicCreateMemoryStoreRequest : AnthropicMemoryStoreBetaRequestBase
    {
        [JsonPropertyName("name")]
        [Required]
        [Description("Human-readable memory store name.")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        [Description("Optional memory store description.")]
        public string? Description { get; set; }
    }

    [Description("Please confirm the Anthropic memory store update request.")]
    public sealed class AnthropicUpdateMemoryStoreRequest : AnthropicMemoryStoreBetaRequestBase
    {
        [JsonPropertyName("memoryStoreId")]
        [Required]
        [Description("Memory store ID.")]
        public string MemoryStoreId { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        [Description("Optional updated memory store name. Omit to preserve.")]
        public string? Name { get; set; }

        [JsonPropertyName("description")]
        [Description("Optional updated memory store description. Provide an empty string to clear.")]
        public string? Description { get; set; }
    }

    [Description("Please confirm the Anthropic memory store archive request.")]
    public sealed class AnthropicArchiveMemoryStoreRequest : AnthropicMemoryStoreBetaRequestBase
    {
        [JsonPropertyName("memoryStoreId")]
        [Required]
        [Description("Memory store ID.")]
        public string MemoryStoreId { get; set; } = string.Empty;
    }

    [Description("Please confirm the Anthropic memory store owner request.")]
    public sealed class AnthropicMemoryStoreOwnerRequest : AnthropicMemoryStoreBetaRequestBase
    {
        [JsonPropertyName("memoryStoreId")]
        [Required]
        [Description("Memory store ID.")]
        public string MemoryStoreId { get; set; } = string.Empty;

        [JsonPropertyName("ownerId")]
        [Required]
        [Description("User ID of the owner to add.")]
        public string OwnerId { get; set; } = string.Empty;
    }
}

