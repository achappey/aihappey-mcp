using System.ComponentModel;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.AzureMaps;

public static class AzureMapsService
{
    private static readonly string[] entityTypes =
     [
        "Neighbourhood",                 // wijk/buurt (if available)
        "MunicipalitySubdivision",       // stadsdeel / deelgemeente
        "MunicipalSubdivision",          // alt naming in some data
        "PostalCodeArea",                // postcode area (polygon)
        "Municipality",                  // gemeente
        "CountrySecondarySubdivision",   // province/state
        "CountrySubdivision",            // region
        "CountryRegion"                  // country
     ];

    public enum AzureMapsTileset
    {
        [EnumMember(Value = "microsoft.base.road")]
        BaseRoad,

        [EnumMember(Value = "microsoft.base.lightgrey")]
        BaseLightGrey,

        [EnumMember(Value = "microsoft.base.darkgrey")]
        BaseDarkGrey,

        [EnumMember(Value = "microsoft.base.night")]
        BaseNight,

        [EnumMember(Value = "microsoft.base.highcontrast.dark")]
        HighContrastDark,

        [EnumMember(Value = "microsoft.base.highcontrast.light")]
        HighContrastLight,

        [EnumMember(Value = "microsoft.base.blank")]
        Blank,

        [EnumMember(Value = "microsoft.imagery")]
        Imagery
    }


    [Description("Get a static PNG with a highlighted area (e.g., a neighbourhood, subdivision, municipality, province). Returns base64 PNG.")]
    [McpServerTool(
     Title = "Get area image",
     Name = "azure_maps_get_area_image",
     OpenWorld = false,
     ReadOnly = true,
     Destructive = false
 )]
    public static async Task<CallToolResult?> AzureMaps_GetAreaImage(
     IServiceProvider serviceProvider,
     RequestContext<CallToolRequestParams> requestContext,
     [Description("Place or area to highlight (e.g. 'Molenvliet, Woerden, Netherlands')")]
    string query,
     [Description("Preferred entity types (CSV, first match wins). Examples: 'Neighbourhood,MunicipalitySubdivision,Municipality'. Leave empty for smart defaults.")]
    string? entityTypesPreferred = null,
     [Description("Country filter (ISO-2, optional). Example: 'NL'")]
    string? countrySet = null,
     [Description("Tileset")]
    AzureMapsTileset tileset = AzureMapsTileset.BaseRoad,
     [Description("Fill color in HEX (without #, e.g. 2272B9)")]
    string fillColor = "2272B9",
     [Description("Fill opacity (0–1)")]
    double fillOpacity = 0.4,
     [Description("Border color in HEX (without #)")]
    string borderColor = "000000",
     [Description("Border width in px")]
    int borderWidth = 2,
     [Description("Image width")]
    int? width = 900,
     [Description("Image height")]
    int? height = 420,
     [Description("Map zoom (optional)")]
    int? zoom = null,
     CancellationToken cancellationToken = default)
     => await requestContext.WithExceptionCheck(async () =>
 {
     var maps = serviceProvider.GetRequiredService<AzureMapsClient>();


     // --- 1) Address search (no hard entityType filter; we choose from results) ---
     // Default preference order (fine → coarse)

     var prefs = string.IsNullOrWhiteSpace(entityTypesPreferred)
         ? entityTypes
         : entityTypesPreferred.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

     // Build address search URL
     var addrUrlSb = new StringBuilder("/search/address/json?api-version=1.0")
         .Append("&limit=10")
         .Append("&query=").Append(Uri.EscapeDataString(query));
     if (!string.IsNullOrWhiteSpace(countrySet))
         addrUrlSb.Append("&countrySet=").Append(Uri.EscapeDataString(countrySet!));

     var body = await maps.GetStringAsync(addrUrlSb.ToString(), cancellationToken);

     var addrJson = JsonSerializer.Deserialize<JsonElement>(body);
     if (!addrJson.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
         throw new Exception($"No search results for '{query}'.");

     // Pick first result matching preferred entity types and exposing geometry.id
     string? geometryId = null;
     string? chosenType = null;
     foreach (var wanted in prefs)
     {
         foreach (var r in results.EnumerateArray())
         {
             var type = r.TryGetProperty("entityType", out var t) && t.ValueKind == JsonValueKind.String
                 ? t.GetString()
                 : null;

             if (!string.Equals(type, wanted, StringComparison.OrdinalIgnoreCase))
                 continue;

             if (r.TryGetProperty("dataSources", out var ds) &&
                 ds.TryGetProperty("geometry", out var g) &&
                 g.TryGetProperty("id", out var idProp) &&
                 idProp.ValueKind == JsonValueKind.String)
             {
                 geometryId = idProp.GetString();
                 chosenType = type;
                 break;
             }
         }
         if (geometryId != null) break;
     }

     if (geometryId == null)
         throw new Exception($"No result for '{query}' matched the preferred types ({string.Join(", ", prefs)}), or no polygon geometry was available.");

     // --- 2) Get the polygon by geometry id ---
     var polyUrl = $"/search/polygon/json?api-version=1.0&geometries={Uri.EscapeDataString(geometryId)}";

     var polyBody = await maps.GetStringAsync(polyUrl, cancellationToken);

     var polyJson = JsonSerializer.Deserialize<JsonElement>(polyBody);
     if (!polyJson.TryGetProperty("additionalData", out var addData) || addData.GetArrayLength() == 0)
         throw new Exception("Polygon response missing additionalData.");

     var geomObj = addData[0].GetProperty("geometryData");

     // Extract first outer ring from Feature/FeatureCollection/Polygon/MultiPolygon
     JsonElement coords;
     string geomType;
     if (geomObj.TryGetProperty("type", out var typeProp) && typeProp.ValueKind == JsonValueKind.String)
     {
         var t = typeProp.GetString();
         if (t == "FeatureCollection")
         {
             var feat = geomObj.GetProperty("features")[0];
             geomType = feat.GetProperty("geometry").GetProperty("type").GetString()!;
             coords = feat.GetProperty("geometry").GetProperty("coordinates");
         }
         else if (t == "Feature")
         {
             geomType = geomObj.GetProperty("geometry").GetProperty("type").GetString()!;
             coords = geomObj.GetProperty("geometry").GetProperty("coordinates");
         }
         else
         {
             geomType = t!;
             coords = geomObj.GetProperty("coordinates");
         }
     }
     else
     {
         geomType = "Polygon";
         coords = geomObj.GetProperty("coordinates");
     }

     List<double[]> ringCoords;
     if (string.Equals(geomType, "MultiPolygon", StringComparison.OrdinalIgnoreCase))
     {
         // [ [ [outer], [hole]... ], [poly2]... ]
         var outer = coords[0][0];
         ringCoords = outer.EnumerateArray()
             .Select(pt => new[] { pt[0].GetDouble(), pt[1].GetDouble() }) // lon, lat
             .ToList();
     }
     else
     {
         // [ [outer], [hole]... ]
         var outer = coords[0];
         ringCoords = outer.EnumerateArray()
             .Select(pt => new[] { pt[0].GetDouble(), pt[1].GetDouble() })
             .ToList();
     }

     if (ringCoords.Count < 3)
         throw new Exception($"Polygon for '{query}' (type {chosenType}) has too few points.");

     // --- 3) Close ring, decimate to ≤100, re-close ---
     if (ringCoords[0][0] != ringCoords[^1][0] || ringCoords[0][1] != ringCoords[^1][1])
         ringCoords.Add([ringCoords[0][0], ringCoords[0][1]]);

     ringCoords = Decimate(ringCoords, 100);
     if (ringCoords[0][0] != ringCoords[^1][0] || ringCoords[0][1] != ringCoords[^1][1])
         ringCoords[^1] = [ringCoords[0][0], ringCoords[0][1]];

     // --- 4) Fit/center ---
     var (minLon, minLat, maxLon, maxLat) = GetBounds(ringCoords);
     var centerLon = (minLon + maxLon) / 2.0;
     var centerLat = (minLat + maxLat) / 2.0;
     var w = Math.Clamp(width ?? 900, 100, 1500);
     var h = Math.Clamp(height ?? 420, 100, 1500);
     var z = zoom ?? GuessZoom(minLon, minLat, maxLon, maxLat, w, h);

     // --- 5) Static image with filled path ---
     fillColor = fillColor.TrimStart('#');
     borderColor = borderColor.TrimStart('#');

     var coordList = string.Join("|", ringCoords.Select(c =>
         $"{c[0].ToString(System.Globalization.CultureInfo.InvariantCulture)} {c[1].ToString(System.Globalization.CultureInfo.InvariantCulture)}"));

     // fc=fill color, fa=fill alpha, lc=line color, lw=line width
     var pathValue = $"fc{fillColor}|fa{fillOpacity.ToString(System.Globalization.CultureInfo.InvariantCulture)}|lc{borderColor}|lw{borderWidth}||{coordList}";

     var qs = new StringBuilder("/map/static?api-version=2024-04-01")
        .Append("&tilesetId=").Append(Uri.EscapeDataString(tileset.GetEnumMemberValue()))
         //.Append("&style=").Append(Uri.EscapeDataString(style.GetEnumMemberValue() ?? "main"))
         .Append("&center=").Append(centerLon.ToString(System.Globalization.CultureInfo.InvariantCulture))
                            .Append(",").Append(centerLat.ToString(System.Globalization.CultureInfo.InvariantCulture))
         .Append("&zoom=").Append(z)
         .Append("&width=").Append(w)
         .Append("&height=").Append(h)
         .Append("&path=").Append(Uri.EscapeDataString(pathValue))
         .ToString();

     var bytes = await maps.GetBytesAsync(qs, cancellationToken);
     var base64 = Convert.ToBase64String(bytes);

     return new CallToolResult
     {
         Content = [new ImageContentBlock { MimeType = MimeTypes.ImagePng, Data = base64 }]
     };
 });



    public enum AzureMapsStyle
    {
        [EnumMember(Value = "main")]
        main,

        [EnumMember(Value = "grayscale_light")]
        grayscale_light,

        [EnumMember(Value = "grayscale_dark")]
        grayscale_dark,

        [EnumMember(Value = "road")]
        road,

        [EnumMember(Value = "night")]
        night,

        [EnumMember(Value = "satellite")]
        satellite,

        [EnumMember(Value = "satellite_with_roads")]
        satellite_with_roads,

        [EnumMember(Value = "high_contrast_dark")]
        high_contrast_dark,

        [EnumMember(Value = "high_contrast_light")]
        high_contrast_light,

        [EnumMember(Value = "blank")]
        blank
    }

    [Description("Get a static PNG with the route drawn (iframe-safe). Returns base64 PNG and echoes the route path.")]
    [McpServerTool(
        Title = "Get route image",
        Name = "azure_maps_get_route_image",
        OpenWorld = false,
        ReadOnly = true,
        Destructive = false
    )]
    public static async Task<CallToolResult?> AzureMaps_GetRouteImage(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Latitude of start")] string latitudeFrom,
        [Description("Longitude of start")] string longitudeFrom,
        [Description("Latitude of destination")] string latitudeTo,
        [Description("Longitude of destination")] string longitudeTo,
        [Description("Tile set")] AzureMapsTileset tileset = AzureMapsTileset.BaseRoad,
        [Description("Image width in pixels (80..1500, default 900)")] int? width = 900,
        [Description("Image height in pixels (80..1500, default 420)")] int? height = 420,
        [Description("Line color in HEX (without #, e.g. 2272B9 or 00AAFF)")]
        string lineColor = "2272B9",
        [Description("Line width in pixels (default 5)")]
        int lineWidth = 5,
        [Description("Map zoom (optional)")] int? zoom = null,
        [Description("Zoom tweak (e.g. -1 to zoom out one level)")] int? zoomAdjust = -1,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
    {
        var maps = serviceProvider.GetRequiredService<AzureMapsClient>();

        // 1) Directions (lat,lon : lat,lon)
        var dirUrl = $"/route/directions/json?api-version=1.0&query={latitudeFrom},{longitudeFrom}:{latitudeTo},{longitudeTo}";
        var body = await maps.GetStringAsync(dirUrl, cancellationToken);

        var json = JsonSerializer.Deserialize<JsonElement>(body);
        var points = json.GetProperty("routes")[0]
                         .GetProperty("legs")[0]
                         .GetProperty("points")
                         .EnumerateArray()
                         .Select(p => new[] {
                             p.GetProperty("longitude").GetDouble(), // lon
                             p.GetProperty("latitude").GetDouble()   // lat
                         })
                         .ToList();

        if (points.Count < 2)
            throw new Exception("No route points returned from Azure Maps.");

        // 2) Decimate to <=100 points (limit per path param)
        var simplified = Decimate(points, 100);

        // 3) Center/zoom heuristic if zoom not provided
        var (minLon, minLat, maxLon, maxLat) = GetBounds(simplified);
        var centerLon = (minLon + maxLon) / 2.0;
        var centerLat = (minLat + maxLat) / 2.0;
        var w = Math.Clamp(width ?? 900, 80, 1500);
        var h = Math.Clamp(height ?? 420, 80, 1500);
        var zRaw = zoom ?? GuessZoom(minLon, minLat, maxLon, maxLat, w, h);
        var z = Math.Clamp(zRaw + (zoomAdjust ?? 0), 3, 18);

        // 4) Build pins and path using the REQUIRED "style||locations" format
        //    IMPORTANT: use "lon lat" (space separated), and URL-encode the whole value.
        string PinsParam((double lon, double lat) p) => $"{p.lon.ToString(System.Globalization.CultureInfo.InvariantCulture)} {p.lat.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        var start = (lon: points.First()[0], lat: points.First()[1]);
        var end = (lon: points.Last()[0], lat: points.Last()[1]);

        var pinsValue = $"default||{PinsParam(start)}|{PinsParam(end)}";
        var pathCoords = string.Join("|", simplified.Select(c => $"{c[0].ToString(System.Globalization.CultureInfo.InvariantCulture)} {c[1].ToString(System.Globalization.CultureInfo.InvariantCulture)}"));
        // Style: line color + width (see docs). Example: magenta line width 5.
        lineColor = lineColor.TrimStart('#');
        var pathValue = $"lc{lineColor}|lw{lineWidth}||{pathCoords}";

        var qs = new StringBuilder("/map/static?api-version=2024-04-01")
            .Append("&tilesetId=").Append(Uri.EscapeDataString(tileset.GetEnumMemberValue()))
            //   .Append("&style=").Append(Uri.EscapeDataString(style.GetEnumMemberValue() ?? AzureMapsStyle.main.GetEnumMemberValue()))
            .Append("&center=").Append(centerLon.ToString(System.Globalization.CultureInfo.InvariantCulture))
                               .Append(',').Append(centerLat.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Append("&zoom=").Append(z)
            .Append("&width=").Append(w)
            .Append("&height=").Append(h)
            .Append("&pins=").Append(Uri.EscapeDataString(pinsValue))
            .Append("&path=").Append(Uri.EscapeDataString(pathValue))
            .ToString();

        var bytes = await maps.GetBytesAsync(qs, cancellationToken);
        var base64 = Convert.ToBase64String(bytes);
        return new CallToolResult()
        {
            Content = [new ImageContentBlock() {
                MimeType = MimeTypes.ImagePng,
                Data = base64
            }]
        };
    });

    private static (double minLon, double minLat, double maxLon, double maxLat) GetBounds(IList<double[]> pts)
    {
        double minLon = double.PositiveInfinity, minLat = double.PositiveInfinity;
        double maxLon = double.NegativeInfinity, maxLat = double.NegativeInfinity;
        foreach (var p in pts)
        {
            if (p[0] < minLon) minLon = p[0];
            if (p[0] > maxLon) maxLon = p[0];
            if (p[1] < minLat) minLat = p[1];
            if (p[1] > maxLat) maxLat = p[1];
        }
        return (minLon, minLat, maxLon, maxLat);
    }

    private static int GuessZoom(double minLon, double minLat, double maxLon, double maxLat, int w, int h)
    {
        const int tile = 256;
        // add ~10% safety so labels/pins don’t hug edges
        const double pad = 1.10;

        double lonSpan = Math.Max(1e-9, maxLon - minLon);

        // mercator helper
        static double LatToMercatorY(double latDeg)
        {
            double lat = Math.Clamp(latDeg, -85.05112878, 85.05112878) * Math.PI / 180.0;
            return Math.Log(Math.Tan(Math.PI / 4.0 + lat / 2.0));
        }

        double y1 = LatToMercatorY(minLat);
        double y2 = LatToMercatorY(maxLat);
        double mercSpan = Math.Abs(y2 - y1);

        // world pixels = tile * 2^z; fit spans into width/height
        // lon → pixels at equator; lat uses mercator span
        double zLon = Math.Log2(w * 2 * Math.PI / (tile * pad * (lonSpan * Math.PI / 180.0)));
        double zLat = Math.Log2(h * 2 / (tile * pad * mercSpan)); // 2π cancels in merc span

        var z = (int)Math.Floor(Math.Min(zLon, zLat));
        return Math.Clamp(z, 3, 18);
    }

    // Evenly sample to N points maximum (keeps endpoints).
    private static List<double[]> Decimate(List<double[]> pts, int maxPts)
    {
        if (pts.Count <= maxPts) return pts;
        var result = new List<double[]>(maxPts)
        {
            pts[0]
        };
        double step = (pts.Count - 1.0) / (maxPts - 1.0);
        for (int i = 1; i < maxPts - 1; i++)
        {
            int idx = (int)Math.Round(i * step);
            result.Add(pts[idx]);
        }
        result.Add(pts[^1]);
        return result;
    }
}