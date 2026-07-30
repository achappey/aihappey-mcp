
using System.Text.Json.Serialization;
namespace MCPhappey.Common.Models;

public class MessageResults
{
    [JsonPropertyName("results")]
    public IEnumerable<ProviderMessageResult> Results { get; set; } = [];
}

public sealed class ProviderMessageResult
{
    [JsonPropertyName("provider")]
    public required string Provider { get; init; }

    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("content")]
    public required string Content { get; init; }

    [JsonPropertyName("duration")]
    public string? Duration { get; init; }

    [JsonPropertyName("metadata")]
    public object? Metadata { get; init; }
}
