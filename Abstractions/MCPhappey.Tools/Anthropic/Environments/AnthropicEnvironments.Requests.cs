using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MCPhappey.Tools.Anthropic.Environments;

public static partial class AnthropicEnvironments
{
    public abstract class AnthropicEnvironmentBetaRequestBase
    {
        [JsonPropertyName("anthropicBeta")]
        [MaxLength(128)]
        [RegularExpression(@"^[^,;\r\n]+$", ErrorMessage = "anthropicBeta must be a single value.")]
        [Description("Optional single extra anthropic-beta header value.")]
        public string? AnthropicBeta { get; set; }
    }

    public abstract class AnthropicEnvironmentMutationRequestBase : AnthropicEnvironmentBetaRequestBase
    {
        [JsonPropertyName("environmentId")]
        [Required]
        [Description("Environment ID.")]
        public string EnvironmentId { get; set; } = string.Empty;
    }

    [Description("Please confirm the Anthropic create environment request.")]
    public sealed class AnthropicCreateEnvironmentRequest : AnthropicEnvironmentBetaRequestBase
    {
        [JsonPropertyName("name")]
        [Required]
        [MinLength(1)]
        [MaxLength(256)]
        [Description("Human-readable name for the environment.")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        [MaxLength(2048)]
        [Description("Optional description.")]
        public string? Description { get; set; }
    }

    [Description("Please confirm the Anthropic update environment request.")]
    public sealed class AnthropicUpdateEnvironmentRequest : AnthropicEnvironmentMutationRequestBase
    {
        [JsonPropertyName("name")]
        [MinLength(1)]
        [MaxLength(256)]
        [Description("Optional updated name. Omit to preserve the current value.")]
        public string? Name { get; set; }

        [JsonPropertyName("description")]
        [MaxLength(2048)]
        [Description("Optional updated description. Provide an empty string to clear.")]
        public string? Description { get; set; }
    }

    [Description("Please confirm the Anthropic archive environment request.")]
    public sealed class AnthropicArchiveEnvironmentRequest : AnthropicEnvironmentMutationRequestBase
    {
    }

    [Description("Please confirm the Anthropic environment metadata mutation request.")]
    public sealed class AnthropicEnvironmentMetadataMutationRequest : AnthropicEnvironmentMutationRequestBase
    {
        [JsonPropertyName("key")]
        [Required]
        [MaxLength(64)]
        [Description("Metadata key.")]
        public string Key { get; set; } = string.Empty;

        [JsonPropertyName("value")]
        [Required]
        [MaxLength(512)]
        [Description("Metadata value.")]
        public string Value { get; set; } = string.Empty;
    }

    [Description("Please confirm the Anthropic environment limited networking request.")]
    public sealed class AnthropicEnvironmentLimitedNetworkingRequest : AnthropicEnvironmentMutationRequestBase
    {
        [JsonPropertyName("allowMcpServers")]
        [Description("Whether outbound access to configured MCP servers is allowed beyond the allowed host list.")]
        public bool AllowMcpServers { get; set; }

        [JsonPropertyName("allowPackageManagers")]
        [Description("Whether outbound access to public package registries is allowed beyond the allowed host list.")]
        public bool AllowPackageManagers { get; set; }
    }

    [Description("Please confirm the Anthropic environment allowed host mutation request.")]
    public sealed class AnthropicEnvironmentAllowedHostMutationRequest : AnthropicEnvironmentMutationRequestBase
    {
        [JsonPropertyName("host")]
        [Required]
        [MaxLength(253)]
        [Description("Hostname or IP address to allow.")]
        public string Host { get; set; } = string.Empty;
    }

    [Description("Please confirm the Anthropic environment package mutation request.")]
    public sealed class AnthropicEnvironmentPackageMutationRequest : AnthropicEnvironmentMutationRequestBase
    {
        [JsonPropertyName("packageManager")]
        [Required]
        [Description("Package manager: apt, cargo, gem, go, npm, or pip.")]
        public string PackageManager { get; set; } = string.Empty;

        [JsonPropertyName("package")]
        [Required]
        [MaxLength(256)]
        [Description("Single package entry, optionally including its version syntax for the selected package manager.")]
        public string Package { get; set; } = string.Empty;
    }
}
