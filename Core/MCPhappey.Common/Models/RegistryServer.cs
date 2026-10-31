using System.Text.Json.Serialization;

namespace MCPhappey.Common.Models;

public class RegistryServer
{
    [JsonPropertyName("$schema")]
    public string Schema { get; set; } = "https://static.modelcontextprotocol.io/schemas/2025-10-17/server.schema.json";

    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("websiteUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WebsiteUrl { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; } = null!;

    [JsonPropertyName("remotes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IEnumerable<ServerRemote>? Remotes { get; set; }

    [JsonPropertyName("repository")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Repository? Repository { get; set; }

    [JsonPropertyName("icons")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IEnumerable<ServerIcon>? Icons { get; set; }

    [JsonPropertyName("_meta")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, Dictionary<string, object>>? Meta { get; set; }
}

public class Repository
{
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; } = null!;

    [JsonPropertyName("subfolder")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Subfolder { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; } = null!;
}

public class ServerRemote
{
    [JsonPropertyName("headers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IEnumerable<ServerHeader>? Headers { get; set; } = null!;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "streamable-http";

    [JsonPropertyName("url")]
    public string Url { get; set; } = null!;
}

public class ServerInput
{
    [JsonPropertyName("choices")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IEnumerable<string>? Choices { get; set; }

    [JsonPropertyName("default")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Default { get; set; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonPropertyName("placeholder")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Placeholder { get; set; }

    [JsonPropertyName("value")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Value { get; set; }

    [JsonPropertyName("isSecret")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsSecret { get; set; }

    [JsonPropertyName("isRequired")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsRequired { get; set; }

    [JsonPropertyName("format")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Format { get; set; }
}

public class ServerHeader : ServerInput
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = default!;

    [JsonPropertyName("variables")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, ServerInput>? Variables { get; set; }

}


public class ServerIcon
{
    [JsonPropertyName("src")]
    public string Source { get; set; } = default!;

    [JsonPropertyName("mimeType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MimeType { get; set; }

    [JsonPropertyName("theme")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Theme { get; set; }

    [JsonPropertyName("sizes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IEnumerable<string>? Sizes { get; set; }
}
