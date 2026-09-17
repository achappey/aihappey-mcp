using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MCPhappey.Tools.OpenAI.Vaults;

public static partial class OpenAIVaults
{
    [Description("Confirm OpenAI vault creation.")]
    public sealed class CreateVaultRequest
    {
        [JsonPropertyName("name"), MaxLength(256)] public string? Name { get; set; }
        [JsonPropertyName("metadataFileUrl")] public string? MetadataFileUrl { get; set; }
    }

    [Description("Please confirm to delete: {0}")]
    public sealed class DeleteItem : MCPhappey.Common.Models.IHasName
    {
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    }

    [Description("Confirm static bearer credential creation. Secret fields are write-only.")]
    public sealed class CreateStaticBearerRequest
    {
        [Required, JsonPropertyName("vaultId")] public string VaultId { get; set; } = string.Empty;
        [Required, JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [Required, JsonPropertyName("mcpServerUrl")] public string McpServerUrl { get; set; } = string.Empty;
        [Required, JsonPropertyName("token"), DataType(DataType.Password)] public string Token { get; set; } = string.Empty;
    }

    [Description("Confirm MCP OAuth credential creation. Secret fields are write-only.")]
    public sealed class CreateOAuthRequest
    {
        [Required, JsonPropertyName("vaultId")] public string VaultId { get; set; } = string.Empty;
        [Required, JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [Required, JsonPropertyName("mcpServerUrl")] public string McpServerUrl { get; set; } = string.Empty;
        [Required, JsonPropertyName("accessToken"), DataType(DataType.Password)] public string AccessToken { get; set; } = string.Empty;
        [JsonPropertyName("expiresAt")] public string? ExpiresAt { get; set; }
        [JsonPropertyName("refreshToken"), DataType(DataType.Password)] public string? RefreshToken { get; set; }
        [JsonPropertyName("clientId")] public string? ClientId { get; set; }
        [JsonPropertyName("tokenEndpoint")] public string? TokenEndpoint { get; set; }
        [JsonPropertyName("tokenEndpointAuth")] public string? TokenEndpointAuth { get; set; }
        [JsonPropertyName("clientSecret"), DataType(DataType.Password)] public string? ClientSecret { get; set; }
        [JsonPropertyName("resource")] public string? Resource { get; set; }
        [JsonPropertyName("scope")] public string? Scope { get; set; }
    }

    [Description("Confirm static bearer credential rotation. The token is write-only.")]
    public sealed class RotateStaticBearerRequest
    {
        [Required, JsonPropertyName("vaultId")] public string VaultId { get; set; } = string.Empty;
        [Required, JsonPropertyName("credentialId")] public string CredentialId { get; set; } = string.Empty;
        [Required, JsonPropertyName("token"), DataType(DataType.Password)] public string Token { get; set; } = string.Empty;
    }

    [Description("Confirm OAuth credential rotation. Secret fields are write-only.")]
    public sealed class RotateOAuthRequest
    {
        [Required, JsonPropertyName("vaultId")] public string VaultId { get; set; } = string.Empty;
        [Required, JsonPropertyName("credentialId")] public string CredentialId { get; set; } = string.Empty;
        [JsonPropertyName("accessToken"), DataType(DataType.Password)] public string? AccessToken { get; set; }
        [JsonPropertyName("expiresAt")] public string? ExpiresAt { get; set; }
        [JsonPropertyName("clearExpiresAt")] public bool ClearExpiresAt { get; set; }
        [JsonPropertyName("refreshToken"), DataType(DataType.Password)] public string? RefreshToken { get; set; }
        [JsonPropertyName("scope")] public string? Scope { get; set; }
        [JsonPropertyName("clearScope")] public bool ClearScope { get; set; }
        [JsonPropertyName("tokenEndpointAuth")] public string? TokenEndpointAuth { get; set; }
        [JsonPropertyName("clientSecret"), DataType(DataType.Password)] public string? ClientSecret { get; set; }
    }
}
