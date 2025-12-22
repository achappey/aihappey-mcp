using System.Text.Json.Serialization;

namespace MCPhappey.Common.Models;

public class SearchResults
{
    [JsonPropertyName("results")]
    public IEnumerable<SearchResult> Results { get; set; } = [];
}

public class SearchResult
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "search_result";

    [JsonPropertyName("title")]
    public required string Title { get; set; }

    [JsonPropertyName("source")]
    public required string Source { get; set; }

    [JsonPropertyName("content")]
    public IEnumerable<SearchResultContentBlock>? Content { get; set; }

    [JsonPropertyName("citations")]
    public CitationConfiguration? Citations { get; set; }

}

public class SearchResultContentBlock
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("text")]
    public required string Text { get; set; }
}


public class CitationConfiguration
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
}