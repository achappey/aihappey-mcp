using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MCPhappey.Tools.NationaleWoningbouwkaart;

internal sealed class NationaleWoningbouwkaartFeatureCollection
{
    [JsonPropertyName("features")]
    public List<NationaleWoningbouwkaartFeature>? Features { get; set; }
}

internal sealed class NationaleWoningbouwkaartFeature
{
    [JsonPropertyName("properties")]
    public JsonObject? Properties { get; set; }
}

internal sealed record NationaleWoningbouwkaartDatasetDefinition(
    string DatasetKey,
    string DatasetName,
    string SourceUrl);

internal sealed record NationaleWoningbouwkaartItem(
    string Name,
    string Description,
    string SearchTextNormalized,
    string NameNormalized,
    JsonObject ListJsonTemplate,
    JsonObject SearchJsonTemplate,
    string? AlternativeNameNormalized = null,
    string? MunicipalityNormalized = null,
    string? ProvinceNormalized = null,
    string? StatusNormalized = null,
    string? PeilmomentNormalized = null,
    string? CodeNormalized = null,
    string? RegionNormalized = null)
{
    public JsonObject ToListJson() => (JsonObject)ListJsonTemplate.DeepClone();

    public JsonObject ToSearchJson() => (JsonObject)SearchJsonTemplate.DeepClone();
}

internal sealed record NationaleWoningbouwkaartDataset(
    string DatasetKey,
    string DatasetName,
    IReadOnlyList<NationaleWoningbouwkaartItem> Items,
    DateTimeOffset CachedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string SourceUrl);
