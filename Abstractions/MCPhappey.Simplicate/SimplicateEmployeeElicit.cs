using System.Text.Json.Serialization;

namespace MCPhappey.Simplicate;

public sealed class SimplicateElicitFieldOverride
{
    public required string PropertyName { get; init; }

    public string? Title { get; init; }

    public string? Description { get; init; }

    public string? DefaultValue { get; init; }

    public IReadOnlyCollection<string>? DefaultValues { get; init; }
}

internal sealed class SimplicateTeamEmployeeCollection
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("employees")]
    public List<SimplicateEmployeeLookupItem>? Employees { get; set; }
}

internal sealed class SimplicateEmployeeLookupItem
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("person_id")]
    public string? PersonId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
