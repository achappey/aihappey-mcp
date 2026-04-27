using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MCPhappey.Common.Models;

namespace MCPhappey.Tools.Anthropic.Credentials;

public static partial class AnthropicCredentials
{
    [Description("Please confirm to delete: {0}")]
    public sealed class AnthropicDeleteCredentialItem : IHasName
    {
        [JsonPropertyName("name")]
        [Description("Confirm deletion.")]
        public string Name { get; set; } = string.Empty;
    }

    public abstract class AnthropicCredentialBetaRequestBase
    {
        [JsonPropertyName("anthropicBetaCsv")]
        [Description("Optional extra anthropic-beta values as comma, semicolon, or newline separated strings.")]
        public string? AnthropicBetaCsv { get; set; }
    }

    [Description("Please confirm the Anthropic credential create request.")]
    public sealed class AnthropicCreateCredentialRequest : AnthropicCredentialBetaRequestBase
    {
        [JsonPropertyName("vaultId")]
        [Required]
        [Description("Vault ID.")]
        public string VaultId { get; set; } = string.Empty;

        [JsonPropertyName("authJson")]
        [Required]
        [Description("Credential auth JSON object. Use type static_bearer with token and mcp_server_url, or type mcp_oauth with access_token, mcp_server_url, optional expires_at, and optional refresh.")]
        public string AuthJson { get; set; } = string.Empty;

        [JsonPropertyName("displayName")]
        [Description("Optional human-readable credential display name.")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("metadataJson")]
        [Description("Optional metadata JSON object. Owner security is inherited from the parent vault.")]
        public string? MetadataJson { get; set; }
    }

    [Description("Please confirm the Anthropic credential update request.")]
    public sealed class AnthropicUpdateCredentialRequest : AnthropicCredentialBetaRequestBase
    {
        [JsonPropertyName("vaultId")]
        [Required]
        [Description("Vault ID.")]
        public string VaultId { get; set; } = string.Empty;

        [JsonPropertyName("credentialId")]
        [Required]
        [Description("Credential ID.")]
        public string CredentialId { get; set; } = string.Empty;

        [JsonPropertyName("authPatchJson")]
        [Description("Optional credential auth update JSON object. The mcp_server_url is immutable.")]
        public string? AuthPatchJson { get; set; }

        [JsonPropertyName("displayName")]
        [Description("Optional updated credential display name. Omit to preserve.")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("metadataPatchJson")]
        [Description("Optional metadata patch JSON object. Set keys to strings to upsert, or null to delete. Owner security is inherited from the parent vault.")]
        public string? MetadataPatchJson { get; set; }
    }

    [Description("Please confirm the Anthropic credential archive request.")]
    public sealed class AnthropicArchiveCredentialRequest : AnthropicCredentialBetaRequestBase
    {
        [JsonPropertyName("vaultId")]
        [Required]
        [Description("Vault ID.")]
        public string VaultId { get; set; } = string.Empty;

        [JsonPropertyName("credentialId")]
        [Required]
        [Description("Credential ID.")]
        public string CredentialId { get; set; } = string.Empty;
    }
}

