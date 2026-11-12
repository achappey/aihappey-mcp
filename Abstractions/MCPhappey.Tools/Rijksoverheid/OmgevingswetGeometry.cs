using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MCPhappey.Tools.Rijksoverheid;

public sealed class OmgevingswetGeometry
{
    [JsonPropertyName("type")]
    [Description("GeoJSON geometry type: Point, MultiPoint, LineString, MultiLineString, Polygon, MultiPolygon or GeometryCollection. Do not pass a Feature.")]
    public required string Type { get; set; }

    [JsonPropertyName("coordinates")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Description("GeoJSON coordinates with nesting appropriate to type. Point: [x,y]; Polygon: [[[x,y],...,[x,y]]]. Use the selected CRS; never silently convert coordinates.")]
    public JsonElement? Coordinates { get; set; }

    [JsonPropertyName("geometries")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Description("Only for GeometryCollection: the member geometries.")]
    public OmgevingswetGeometry[]? Geometries { get; set; }

    public void Validate()
    {
        if (Type == "GeometryCollection")
        {
            if (Coordinates is not null || Geometries is null)
                throw new ArgumentException("GeometryCollection requires geometries, not coordinates.");
            foreach (var geometry in Geometries) geometry.Validate();
            return;
        }
        if (Type is not ("Point" or "MultiPoint" or "LineString" or "MultiLineString" or "Polygon" or "MultiPolygon")
            || Geometries is not null || Coordinates is not { ValueKind: JsonValueKind.Array } coordinates
            || coordinates.GetArrayLength() == 0)
            throw new ArgumentException("Provide a GeoJSON geometry type and nonempty coordinates (not a Feature or FeatureCollection).");
    }
}
