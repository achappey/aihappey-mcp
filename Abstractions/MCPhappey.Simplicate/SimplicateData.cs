using System.Text.Json.Serialization;

namespace MCPhappey.Simplicate;

public class SimplicateData<T>
{
    [JsonPropertyName("data")]
    public IEnumerable<T> Data { get; set; } = default!;

    [JsonPropertyName("metadata")]
    public SimplicateMetadata? Metadata { get; set; }
}

public class SimplicateMetadata
{
    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("offset")]
    public int? Offset { get; set; }

    [JsonPropertyName("limit")]
    public int? Limit { get; set; }

}

public class SimplicateItemData<T>
{
    [JsonPropertyName("data")]
    public T? Data { get; set; }

    [JsonPropertyName("errors")]
    public IEnumerable<string>? Errors { get; set; }
}


public class SimplicateNewItemData
{
    [JsonPropertyName("data")]
    public SimplicateNewItem Data { get; set; } = default!;

    [JsonPropertyName("errors")]
    public IEnumerable<string>? Errors { get; set; }
}


public class SimplicateNewItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = default!;

}