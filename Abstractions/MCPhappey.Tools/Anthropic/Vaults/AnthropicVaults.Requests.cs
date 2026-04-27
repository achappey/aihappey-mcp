using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MCPhappey.Common.Models;

namespace MCPhappey.Tools.Anthropic.Vaults;

public static partial class AnthropicVaults
{
    [Description("Please confirm to delete: {0}")]
    public sealed class AnthropicDeleteVaultItem : IHasName
    {
        [JsonPropertyName("name")]
        [Description("Confirm deletion.")]
        public string Name { get; set; } = string.Empty;
    }

    public abstract class AnthropicVaultBetaRequestBase
    {
        [JsonPropertyName("anthropicBetaCsv")]
        [Description("Optional extra anthropic-beta values as comma, semicolon, or newline separated strings.")]
        public string? AnthropicBetaCsv { get; set; }
    }

    [Description("Please confirm the Anthropic vault create request.")]
    public sealed class AnthropicCreateVaultRequest : AnthropicVaultBetaRequestBase
    {
        [JsonPropertyName("displayName")]
        [Required]
        [Description("Human-readable vault display name.")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("metadataJson")]
        [Description("Optional metadata JSON object. The Owners entry is controlled by this MCP server.")]
        public string? MetadataJson { get; set; }
    }

    [Description("Please confirm the Anthropic vault update request.")]
    public sealed class AnthropicUpdateVaultRequest : AnthropicVaultBetaRequestBase
    {
        [JsonPropertyName("vaultId")]
        [Required]
        [Description("Vault ID.")]
        public string VaultId { get; set; } = string.Empty;

        [JsonPropertyName("displayName")]
        [Description("Optional updated vault display name. Omit to preserve.")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("metadataPatchJson")]
        [Description("Optional metadata patch JSON object. Set keys to strings to upsert, or null to delete. The Owners entry is preserved.")]
        public string? MetadataPatchJson { get; set; }
    }

    [Description("Please confirm the Anthropic vault archive request.")]
    public sealed class AnthropicArchiveVaultRequest : AnthropicVaultBetaRequestBase
    {
        [JsonPropertyName("vaultId")]
        [Required]
        [Description("Vault ID.")]
        public string VaultId { get; set; } = string.Empty;
    }

    [Description("Please confirm the Anthropic vault owner request.")]
    public sealed class AnthropicVaultOwnerRequest : AnthropicVaultBetaRequestBase
    {
        [JsonPropertyName("vaultId")]
        [Required]
        [Description("Vault ID.")]
        public string VaultId { get; set; } = string.Empty;

        [JsonPropertyName("ownerId")]
        [Required]
        [Description("User ID of the owner to add.")]
        public string OwnerId { get; set; } = string.Empty;
    }
}

