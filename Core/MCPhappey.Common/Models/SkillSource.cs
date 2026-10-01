using System.Text.Json.Serialization;

namespace MCPhappey.Common.Models;

// Source configuration, never serialized as an MCP Skill entry.
public sealed class SkillSource
{
    public const string MediaType = "application/vnd.agent-skill";

    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    [JsonPropertyName("mimeType")]
    public required string MimeType { get; init; }
}

public sealed class SkillSources
{
    [JsonPropertyName("skills")]
    public List<SkillSource> Skills { get; init; } = [];
}
