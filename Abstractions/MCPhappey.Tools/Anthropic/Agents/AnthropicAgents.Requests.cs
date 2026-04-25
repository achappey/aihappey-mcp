using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MCPhappey.Tools.Anthropic.Agents;

public static partial class AnthropicAgents
{
    [Description("Please confirm the Anthropic create agent request.")]
    public sealed class AnthropicCreateAgentRequest
    {
        [JsonPropertyName("name")]
        [Required]
        [MinLength(1)]
        [MaxLength(256)]
        [Description("Human-readable name for the agent.")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("modelId")]
        [Required]
        [Description("Model identifier such as claude-sonnet-4-6.")]
        public string ModelId { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        [MaxLength(2048)]
        [Description("Optional description.")]
        public string? Description { get; set; }

        [JsonPropertyName("modelSpeed")]
        [Description("Optional model speed: standard or fast.")]
        public string? ModelSpeed { get; set; }

        [JsonPropertyName("system")]
        [MaxLength(100000)]
        [Description("Optional system prompt.")]
        public string? System { get; set; }

        [JsonPropertyName("anthropicBetaCsv")]
        [Description("Optional extra anthropic-beta values as comma, semicolon, or newline separated strings.")]
        public string? AnthropicBetaCsv { get; set; }
    }

    [Description("Please confirm the Anthropic update agent request.")]
    public sealed class AnthropicUpdateAgentRequest
    {
        [JsonPropertyName("agentId")]
        [Required]
        [Description("Agent ID to update.")]
        public string AgentId { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        [Required]
        [Range(1, int.MaxValue)]
        [Description("Current agent version used for optimistic concurrency.")]
        public int Version { get; set; }

        [JsonPropertyName("name")]
        [MinLength(1)]
        [MaxLength(256)]
        [Description("Optional updated name.")]
        public string? Name { get; set; }

        [JsonPropertyName("description")]
        [MaxLength(2048)]
        [Description("Optional updated description. Provide an empty string to clear.")]
        public string? Description { get; set; }

        [JsonPropertyName("modelId")]
        [Description("Optional updated model identifier.")]
        public string? ModelId { get; set; }

        [JsonPropertyName("modelSpeed")]
        [Description("Optional updated model speed: standard or fast.")]
        public string? ModelSpeed { get; set; }

        [JsonPropertyName("system")]
        [MaxLength(100000)]
        [Description("Optional updated system prompt. Provide an empty string to clear.")]
        public string? System { get; set; }

        [JsonPropertyName("anthropicBetaCsv")]
        [Description("Optional extra anthropic-beta values as comma, semicolon, or newline separated strings.")]
        public string? AnthropicBetaCsv { get; set; }
    }

    [Description("Please confirm the Anthropic archive agent request.")]
    public sealed class AnthropicArchiveAgentRequest
    {
        [JsonPropertyName("agentId")]
        [Required]
        [Description("Agent ID to archive.")]
        public string AgentId { get; set; } = string.Empty;

        [JsonPropertyName("anthropicBetaCsv")]
        [Description("Optional extra anthropic-beta values as comma, semicolon, or newline separated strings.")]
        public string? AnthropicBetaCsv { get; set; }
    }

    [Description("Please confirm the Anthropic agent skill mutation request.")]
    public sealed class AnthropicAgentSkillMutationRequest
    {
        [JsonPropertyName("agentId")]
        [Required]
        [Description("Agent ID.")]
        public string AgentId { get; set; } = string.Empty;

        [JsonPropertyName("skillId")]
        [Required]
        [Description("Skill ID.")]
        public string SkillId { get; set; } = string.Empty;

        [JsonPropertyName("skillType")]
        [Description("Skill type: anthropic or custom.")]
        public string SkillType { get; set; } = "anthropic";

        [JsonPropertyName("skillVersion")]
        [Description("Optional skill version.")]
        public string? SkillVersion { get; set; }

        [JsonPropertyName("anthropicBetaCsv")]
        [Description("Optional extra anthropic-beta values as comma, semicolon, or newline separated strings.")]
        public string? AnthropicBetaCsv { get; set; }
    }

    [Description("Please confirm the Anthropic agent MCP server mutation request.")]
    public sealed class AnthropicAgentMcpServerMutationRequest
    {
        [JsonPropertyName("agentId")]
        [Required]
        [Description("Agent ID.")]
        public string AgentId { get; set; } = string.Empty;

        [JsonPropertyName("serverName")]
        [Required]
        [MinLength(1)]
        [MaxLength(255)]
        [Description("Unique MCP server name.")]
        public string ServerName { get; set; } = string.Empty;

        [JsonPropertyName("serverUrl")]
        [Required]
        [Description("MCP server URL.")]
        public string ServerUrl { get; set; } = string.Empty;

        [JsonPropertyName("anthropicBetaCsv")]
        [Description("Optional extra anthropic-beta values as comma, semicolon, or newline separated strings.")]
        public string? AnthropicBetaCsv { get; set; }
    }

    [Description("Please confirm the Anthropic agent metadata mutation request.")]
    public sealed class AnthropicAgentMetadataMutationRequest
    {
        [JsonPropertyName("agentId")]
        [Required]
        [Description("Agent ID.")]
        public string AgentId { get; set; } = string.Empty;

        [JsonPropertyName("key")]
        [Required]
        [MinLength(1)]
        [MaxLength(64)]
        [Description("Metadata key.")]
        public string Key { get; set; } = string.Empty;

        [JsonPropertyName("value")]
        [Required]
        [MaxLength(512)]
        [Description("Metadata value.")]
        public string Value { get; set; } = string.Empty;

        [JsonPropertyName("anthropicBetaCsv")]
        [Description("Optional extra anthropic-beta values as comma, semicolon, or newline separated strings.")]
        public string? AnthropicBetaCsv { get; set; }
    }

    [Description("Please confirm the Anthropic agent built-in tool mutation request.")]
    public sealed class AnthropicAgentBuiltinToolMutationRequest
    {
        [JsonPropertyName("agentId")]
        [Required]
        [Description("Agent ID.")]
        public string AgentId { get; set; } = string.Empty;

        [JsonPropertyName("toolName")]
        [Required]
        [Description("Built-in tool name.")]
        public string ToolName { get; set; } = string.Empty;

        [JsonPropertyName("enabled")]
        [Description("Optional enabled flag override.")]
        public bool? Enabled { get; set; }

        [JsonPropertyName("permissionPolicy")]
        [Description("Optional permission policy: always_allow or always_ask.")]
        public string? PermissionPolicy { get; set; }

        [JsonPropertyName("anthropicBetaCsv")]
        [Description("Optional extra anthropic-beta values as comma, semicolon, or newline separated strings.")]
        public string? AnthropicBetaCsv { get; set; }
    }

    [Description("Please confirm the Anthropic built-in tool defaults request.")]
    public sealed class AnthropicAgentBuiltinToolsetDefaultsRequest
    {
        [JsonPropertyName("agentId")]
        [Required]
        [Description("Agent ID.")]
        public string AgentId { get; set; } = string.Empty;

        [JsonPropertyName("enabled")]
        [Description("Optional default enabled flag for built-in tools.")]
        public bool? Enabled { get; set; }

        [JsonPropertyName("permissionPolicy")]
        [Description("Optional default permission policy: always_allow or always_ask.")]
        public string? PermissionPolicy { get; set; }

        [JsonPropertyName("anthropicBetaCsv")]
        [Description("Optional extra anthropic-beta values as comma, semicolon, or newline separated strings.")]
        public string? AnthropicBetaCsv { get; set; }
    }

    [Description("Please confirm the Anthropic agent MCP tool mutation request.")]
    public sealed class AnthropicAgentMcpToolMutationRequest
    {
        [JsonPropertyName("agentId")]
        [Required]
        [Description("Agent ID.")]
        public string AgentId { get; set; } = string.Empty;

        [JsonPropertyName("mcpServerName")]
        [Required]
        [Description("MCP server name referenced by the toolset.")]
        public string McpServerName { get; set; } = string.Empty;

        [JsonPropertyName("toolName")]
        [Required]
        [Description("MCP tool name.")]
        public string ToolName { get; set; } = string.Empty;

        [JsonPropertyName("enabled")]
        [Description("Optional enabled flag override.")]
        public bool? Enabled { get; set; }

        [JsonPropertyName("permissionPolicy")]
        [Description("Optional permission policy: always_allow or always_ask.")]
        public string? PermissionPolicy { get; set; }

        [JsonPropertyName("anthropicBetaCsv")]
        [Description("Optional extra anthropic-beta values as comma, semicolon, or newline separated strings.")]
        public string? AnthropicBetaCsv { get; set; }
    }

    [Description("Please confirm the Anthropic MCP tool defaults request.")]
    public sealed class AnthropicAgentMcpToolsetDefaultsRequest
    {
        [JsonPropertyName("agentId")]
        [Required]
        [Description("Agent ID.")]
        public string AgentId { get; set; } = string.Empty;

        [JsonPropertyName("mcpServerName")]
        [Required]
        [Description("MCP server name referenced by the toolset.")]
        public string McpServerName { get; set; } = string.Empty;

        [JsonPropertyName("enabled")]
        [Description("Optional default enabled flag for tools from this MCP server.")]
        public bool? Enabled { get; set; }

        [JsonPropertyName("permissionPolicy")]
        [Description("Optional default permission policy: always_allow or always_ask.")]
        public string? PermissionPolicy { get; set; }

        [JsonPropertyName("anthropicBetaCsv")]
        [Description("Optional extra anthropic-beta values as comma, semicolon, or newline separated strings.")]
        public string? AnthropicBetaCsv { get; set; }
    }

    [Description("Please confirm the Anthropic agent custom tool mutation request.")]
    public sealed class AnthropicAgentCustomToolMutationRequest
    {
        [JsonPropertyName("agentId")]
        [Required]
        [Description("Agent ID.")]
        public string AgentId { get; set; } = string.Empty;

        [JsonPropertyName("toolName")]
        [Required]
        [MinLength(1)]
        [MaxLength(128)]
        [Description("Custom tool name.")]
        public string ToolName { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        [Required]
        [MinLength(1)]
        [MaxLength(1024)]
        [Description("Custom tool description.")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("fileUrl")]
        [Required]
        [Description("URL of the JSON Schema file for the tool input. SharePoint and OneDrive URLs are supported.")]
        public string FileUrl { get; set; } = string.Empty;

        [JsonPropertyName("anthropicBetaCsv")]
        [Description("Optional extra anthropic-beta values as comma, semicolon, or newline separated strings.")]
        public string? AnthropicBetaCsv { get; set; }
    }
}
