using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;

namespace MCPhappey.Common.Models;

public class PromptTemplates
{
    [JsonPropertyName("prompts")]
    public List<PromptTemplate> Prompts { get; set; } = [];
}

public class PromptTemplate
{
    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = null!;

    [JsonPropertyName("template")]
    public Prompt Template { get; set; } = null!;
}
