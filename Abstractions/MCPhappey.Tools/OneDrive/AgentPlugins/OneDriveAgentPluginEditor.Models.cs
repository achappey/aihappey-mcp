using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MCPhappey.Common.Models;

namespace MCPhappey.Tools.OneDrive.AgentPlugins;

public static partial class OneDriveAgentPluginEditor
{
    [Description("Create or update Agent Plugin metadata. Empty optional values remove those fields.")]
    public sealed class PluginManifestInput
    {
        [JsonPropertyName("name")]
        [Required]
        [Description("Immutable Agent Plugin name and OneDrive folder name.")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("authorName")]
        public string? AuthorName { get; set; }

        [JsonPropertyName("authorEmail")]
        public string? AuthorEmail { get; set; }

        [JsonPropertyName("authorUrl")]
        public string? AuthorUrl { get; set; }

        [JsonPropertyName("homepage")]
        public string? Homepage { get; set; }

        [JsonPropertyName("repository")]
        public string? Repository { get; set; }

        [JsonPropertyName("license")]
        public string? License { get; set; }

        [JsonPropertyName("keywords")]
        [Description("Comma- or newline-delimited keywords.")]
        public string? Keywords { get; set; }

        [JsonPropertyName("extensionsJson")]
        [Description("Optional JSON object keyed by reverse-domain client extension namespace.")]
        public string? ExtensionsJson { get; set; }
    }

    [Description("Create or replace a UTF-8 text package file.")]
    public sealed class PluginTextFileInput
    {
        [JsonPropertyName("relativePath")]
        [Required]
        public string RelativePath { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        [Required]
        public string Content { get; set; } = string.Empty;
    }

    [Description("Import a binary or text package file from a URL.")]
    public sealed class PluginFileImportInput
    {
        [JsonPropertyName("sourceUrl")]
        [Required]
        public string SourceUrl { get; set; } = string.Empty;

        [JsonPropertyName("relativePath")]
        [Required]
        public string RelativePath { get; set; } = string.Empty;
    }

    [Description("Create or replace one Agent Plugin MCP server entry.")]
    public sealed class PluginMcpServerInput
    {
        [JsonPropertyName("serverName")]
        [Required]
        public string ServerName { get; set; } = string.Empty;

        [JsonPropertyName("serverJson")]
        [Required]
        [Description("A JSON object for one stdio, streamable-http, or sse server variant.")]
        public string ServerJson { get; set; } = string.Empty;
    }

    [Description("Import an Agent Skill snapshot from a shared OneDrive/SharePoint folder or ZIP URL.")]
    public sealed class PluginSkillImportInput
    {
        [JsonPropertyName("sourceUrl")]
        [Required]
        public string SourceUrl { get; set; } = string.Empty;
    }

    [Description("Please confirm deletion by entering: {0}")]
    public sealed class DeleteByNameConfirmation : IHasName
    {
        [JsonPropertyName("name")]
        [Required]
        public string Name { get; set; } = string.Empty;
    }
}
