using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MCPhappey.Tools.OpenAI.Agents;

public static partial class OpenAIAgents
{
    [Description("Confirm the OpenAI agent scalar configuration.")]
    public class AgentScalarRequest
    {
        [Required, JsonPropertyName("model"), Description("OpenAI model name.")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("name"), Description("Optional agent name.")]
        public string? Name { get; set; }

        [JsonPropertyName("instructions"), Description("Optional custom instructions.")]
        public string? Instructions { get; set; }

        [JsonPropertyName("multiAgentEnabled")] public bool? MultiAgentEnabled { get; set; }
        [Range(1, int.MaxValue), JsonPropertyName("maxConcurrentSubagents")] public int? MaxConcurrentSubagents { get; set; }
        [JsonPropertyName("reasoningEffort")] public string? ReasoningEffort { get; set; }
        [JsonPropertyName("reasoningSummary")] public string? ReasoningSummary { get; set; }
        [JsonPropertyName("serviceTier")] public string? ServiceTier { get; set; }
        [JsonPropertyName("textVerbosity")] public string? TextVerbosity { get; set; }
        [JsonPropertyName("textSchemaFileUrl")] public string? TextSchemaFileUrl { get; set; }
    }

    [Description("Confirm the OpenAI agent scalar update.")]
    public sealed class AgentUpdateScalarRequest : AgentScalarRequest
    {
        [Required, JsonPropertyName("agentId"), Description("Agent ID.")]
        public string AgentId { get; set; } = string.Empty;

        [JsonPropertyName("clearName"), Description("Clear the current name.")]
        public bool ClearName { get; set; }

        [JsonPropertyName("clearInstructions"), Description("Clear custom instructions.")]
        public bool ClearInstructions { get; set; }

        [JsonPropertyName("clearReasoning")] public bool ClearReasoning { get; set; }
        [JsonPropertyName("clearText")] public bool ClearText { get; set; }
        [JsonPropertyName("clearServiceTier")] public bool ClearServiceTier { get; set; }
        [JsonPropertyName("clearMetadata")] public bool ClearMetadata { get; set; }
        [JsonPropertyName("clearTools")] public bool ClearTools { get; set; }
    }

    [Description("Confirm an OpenAI agent metadata mutation.")]
    public sealed class AgentMetadataRequest
    {
        [Required, JsonPropertyName("agentId")] public string AgentId { get; set; } = string.Empty;
        [Required, MinLength(1), MaxLength(64), JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
        [MaxLength(512), JsonPropertyName("value")] public string? Value { get; set; }
    }

    [Description("Confirm OpenAI agent multi-agent configuration.")]
    public sealed class AgentMultiAgentRequest
    {
        [Required, JsonPropertyName("agentId")] public string AgentId { get; set; } = string.Empty;
        [JsonPropertyName("enabled")] public bool Enabled { get; set; }
        [Range(1, int.MaxValue), JsonPropertyName("maxConcurrentSubagents")] public int? MaxConcurrentSubagents { get; set; }
    }

    [Description("Confirm OpenAI agent reasoning configuration.")]
    public sealed class AgentReasoningRequest
    {
        [Required, JsonPropertyName("agentId")] public string AgentId { get; set; } = string.Empty;
        [JsonPropertyName("effort")] public string? Effort { get; set; }
        [JsonPropertyName("summary")] public string? Summary { get; set; }
    }

    [Description("Confirm OpenAI agent text configuration.")]
    public sealed class AgentTextRequest
    {
        [Required, JsonPropertyName("agentId")] public string AgentId { get; set; } = string.Empty;
        [JsonPropertyName("verbosity")] public string? Verbosity { get; set; }
        [JsonPropertyName("schemaFileUrl")] public string? SchemaFileUrl { get; set; }
    }

    [Description("Confirm an OpenAI agent function-tool mutation.")]
    public sealed class AgentFunctionToolRequest
    {
        [Required, JsonPropertyName("agentId")] public string AgentId { get; set; } = string.Empty;
        [Required, JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [Required, JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
        [Required, JsonPropertyName("schemaFileUrl")] public string SchemaFileUrl { get; set; } = string.Empty;
        [JsonPropertyName("deferLoading")] public bool DeferLoading { get; set; }
    }

    [Description("Confirm an OpenAI agent tool mutation.")]
    public sealed class AgentToolIdentityRequest
    {
        [Required, JsonPropertyName("agentId")] public string AgentId { get; set; } = string.Empty;
        [Required, JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    }

    [Description("Confirm OpenAI agent web-search configuration.")]
    public sealed class AgentWebSearchRequest
    {
        [Required, JsonPropertyName("agentId")] public string AgentId { get; set; } = string.Empty;
        [JsonPropertyName("contextSize")] public string? ContextSize { get; set; }
        [JsonPropertyName("mode")] public string? Mode { get; set; }
        [JsonPropertyName("city")] public string? City { get; set; }
        [JsonPropertyName("country")] public string? Country { get; set; }
        [JsonPropertyName("region")] public string? Region { get; set; }
        [JsonPropertyName("timezone")] public string? Timezone { get; set; }
    }

    [Description("Confirm OpenAI agent HTTP MCP configuration.")]
    public sealed class AgentHttpMcpRequest
    {
        [Required, JsonPropertyName("agentId")] public string AgentId { get; set; } = string.Empty;
        [Required, JsonPropertyName("serverLabel")] public string ServerLabel { get; set; } = string.Empty;
        [Required, JsonPropertyName("serverUrl")] public string ServerUrl { get; set; } = string.Empty;
        [JsonPropertyName("connectionOrigin")] public string? ConnectionOrigin { get; set; }
        [JsonPropertyName("credentialId")] public string? CredentialId { get; set; }
        [JsonPropertyName("required")] public bool Required { get; set; }
    }

    [Description("Confirm OpenAI agent stdio MCP configuration.")]
    public sealed class AgentStdioMcpRequest
    {
        [Required, JsonPropertyName("agentId")] public string AgentId { get; set; } = string.Empty;
        [Required, JsonPropertyName("serverLabel")] public string ServerLabel { get; set; } = string.Empty;
        [Required, JsonPropertyName("command")] public string Command { get; set; } = string.Empty;
        [Required, JsonPropertyName("cwd")] public string Cwd { get; set; } = string.Empty;
        [JsonPropertyName("required")] public bool Required { get; set; }
    }

    [Description("Confirm an OpenAI agent MCP map-entry mutation.")]
    public sealed class AgentMcpMapEntryRequest
    {
        [Required, JsonPropertyName("agentId")] public string AgentId { get; set; } = string.Empty;
        [Required, JsonPropertyName("serverLabel")] public string ServerLabel { get; set; } = string.Empty;
        [Required, JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
        [JsonPropertyName("value")] public string? Value { get; set; }
    }
}
