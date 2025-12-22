using System.ComponentModel;
using ModelContextProtocol.Server;
using Raffinert.FuzzySharp;
using Raffinert.FuzzySharp.PreProcess;
using Raffinert.FuzzySharp.SimilarityRatio;
using Raffinert.FuzzySharp.SimilarityRatio.Scorer;
using Raffinert.FuzzySharp.SimilarityRatio.Scorer.StrategySensitive;
using Raffinert.FuzzySharp.SimilarityRatio.Scorer.Composite;
using ModelContextProtocol.Protocol;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using MCPhappey.Core.Services;
using MCPhappey.Common.Extensions;
using System.Text.Json;
using System.Globalization;
using CoordinateSharp;

namespace MCPhappey.Tools.GitHub.CoordinateSharp;

public static class CoordinateSharpService
{
    // ----------------------------- PARSE -----------------------------
    [Description("Parses coordinates (DMS, signed decimal, UTM/MGRS/GEOREF, etc.) and returns normalized forms + common conversions.")]
    [McpServerTool(
        Title = "Parse coordinates",
        Name = "github_coordinatesharp_parse",
        ReadOnly = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GitHubCoordinateSharp_Parse(
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Coordinate input (e.g. \"52.0907, 5.1214\" or \"N 47° 36' 22.32\\\" W 122° 19' 55.56\\\"\")")] string input,
        [Description("Optional ISO-8601 date/time for celestial calculations (defaults to now, UTC).")] string? dateTime = null)
    => await requestContext.WithExceptionCheck(async () =>
    {
        var dt = TryParseInstant(dateTime) ?? DateTime.UtcNow;

        var c = TryParseCoordinate(input, dt)
                ?? throw new Exception($"Unable to parse coordinate: '{input}'");

        var payload = new
        {
            request = new { input, dateTime = dt.ToString("o") },
            result = BuildFormats(c)
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        return await Task.FromResult(json.ToTextCallToolResponse());
    });

    // ------------------------- CONVERT FORMATS -----------------------
    [Description("Converts a coordinate to selected target formats (choose from: UTM, MGRS, GEOREF, WebMercator, ECEF, DMS, Decimal).")]
    [McpServerTool(
        Title = "Convert coordinate formats",
        Name = "github_coordinatesharp_convert_formats",
        ReadOnly = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GitHubCoordinateSharp_ConvertFormats(
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Coordinate input (string forms accepted).")] string input,
        [Description("Array of target formats, e.g. [\"UTM\",\"MGRS\",\"Decimal\"]")] string[] targets,
        [Description("Optional ISO-8601 date/time (only needed for celestial; safe to omit).")] string? dateTime = null)
    => await requestContext.WithExceptionCheck(async () =>
    {
        var dt = TryParseInstant(dateTime) ?? DateTime.UtcNow;
        var c = TryParseCoordinate(input, dt)
                ?? throw new Exception($"Unable to parse coordinate: '{input}'");

        var all = BuildFormats(c);
        var selection = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in targets ?? Array.Empty<string>())
        {
            if (all.TryGetValue(t, out var val)) selection[t] = val;
            else
            {
                // case-insensitive friendly
                var kv = all.FirstOrDefault(kv => kv.Key.Equals(t, StringComparison.OrdinalIgnoreCase));
                if (kv.Key != null) selection[kv.Key] = kv.Value;
            }
        }

        var payload = new
        {
            request = new { input, dateTime = dt.ToString("o"), targets },
            result = selection.Count > 0 ? selection : all
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        return await Task.FromResult(json.ToTextCallToolResponse());
    });

    // --------------------- DISTANCE & BEARING ------------------------
    [Description("Great-circle distance and bearings between two coordinates (inputs can be DMS/decimal/etc.).")]
    [McpServerTool(
        Title = "Distance & bearings",
        Name = "github_coordinatesharp_distance_bearing",
        ReadOnly = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GitHubCoordinateSharp_DistanceBearing(
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Origin coordinate")] string from,
        [Description("Destination coordinate")] string to,
        [Description("Distance unit: m|km|mi|nm (default km)")] string? unit = "km")
    => await requestContext.WithExceptionCheck(async () =>
    {
        var now = DateTime.UtcNow;
        var c1 = TryParseCoordinate(from, now) ?? throw new Exception($"Invalid 'from': {from}");
        var c2 = TryParseCoordinate(to, now) ?? throw new Exception($"Invalid 'to': {to}");

        var (lat1, lon1) = (ToDec(c1.Latitude), ToDec(c1.Longitude));
        var (lat2, lon2) = (ToDec(c2.Latitude), ToDec(c2.Longitude));

        var (distMeters, initialBearing, finalBearing) = HaversineWithBearings(lat1, lon1, lat2, lon2);

        var distance = unit?.ToLowerInvariant() switch
        {
            "m" => distMeters,
            "km" => distMeters / 1000.0,
            "mi" => distMeters / 1609.344,
            "nm" => distMeters / 1852.0,
            _ => distMeters / 1000.0
        };

        var payload = new
        {
            request = new { from, to, unit = unit ?? "km" },
            result = new
            {
                distance = Math.Round(distance, 6),
                unit = unit ?? "km",
                initialBearing = Math.Round(initialBearing, 6),
                finalBearing = Math.Round(finalBearing, 6)
            }
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        return await Task.FromResult(json.ToTextCallToolResponse());
    });

    // --------------------------- DESTINATION -------------------------
    [Description("Computes the destination point given a start coordinate, initial bearing (degrees), and distance.")]
    [McpServerTool(
        Title = "Destination point",
        Name = "github_coordinatesharp_destination",
        ReadOnly = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GitHubCoordinateSharp_Destination(
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Start coordinate (DMS/decimal/etc.)")] string start,
        [Description("Initial bearing in degrees (0..360, from North)")] double bearingDegrees,
        [Description("Distance with unit, e.g. '12.5 km', '4000 m', '3.2 nm'")] string distance)
    => await requestContext.WithExceptionCheck(async () =>
    {
        var now = DateTime.UtcNow;
        var cs = TryParseCoordinate(start, now) ?? throw new Exception($"Invalid 'start': {start}");

        var (lat, lon) = (ToDec(cs.Latitude), ToDec(cs.Longitude));
        var distMeters = ParseDistanceMeters(distance);

        var (dLat, dLon) = DestinationLatLon(lat, lon, bearingDegrees, distMeters);
        var c = new Coordinate(dLat, dLon, now);

        var payload = new
        {
            request = new { start, bearingDegrees, distance },
            result = BuildFormats(c)
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        return await Task.FromResult(json.ToTextCallToolResponse());
    });

    // ------------------------- CELESTIAL INFO ------------------------
    [Description("Returns sun & moon times/values for a coordinate at a given date/time (sunrise/set, twilights, moonrise/set, phase, altitude/azimuth).")]
    [McpServerTool(
        Title = "Celestial information",
        Name = "github_coordinatesharp_celestial_info",
        ReadOnly = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GitHubCoordinateSharp_CelestialInfo(
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Coordinate input")] string input,
        [Description("ISO-8601 date/time (defaults to now, UTC)")] string? dateTime = null)
    => await requestContext.WithExceptionCheck(async () =>
    {
        var dt = TryParseInstant(dateTime) ?? DateTime.UtcNow;
        var c = TryParseCoordinate(input, dt)
                ?? throw new Exception($"Unable to parse coordinate: '{input}'");

        var ci = c.CelestialInfo;

        var payload = new
        {
            request = new { input, dateTime = dt.ToString("o") },
            result = new
            {
                sun = new
                {
                    sunrise = ci.SunRise?.ToString("o"),
                    sunset = ci.SunSet?.ToString("o"),
                    // twilights live under AdditionalSolarTimes
                    civilDawn = ci.AdditionalSolarTimes?.CivilDawn?.ToString("o"),
                    civilDusk = ci.AdditionalSolarTimes?.CivilDusk?.ToString("o"),
                    nauticalDawn = ci.AdditionalSolarTimes?.NauticalDawn?.ToString("o"),
                    nauticalDusk = ci.AdditionalSolarTimes?.NauticalDusk?.ToString("o"),
                    astronomicalDawn = ci.AdditionalSolarTimes?.AstronomicalDawn?.ToString("o"),
                    astronomicalDusk = ci.AdditionalSolarTimes?.AstronomicalDusk?.ToString("o"),
                    altitude = ci.SunAltitude,
                    azimuth = ci.SunAzimuth
                },
                moon = new
                {
                    moonrise = ci.MoonRise?.ToString("o"),
                    moonset = ci.MoonSet?.ToString("o"),
                    phaseName = ci.MoonIllum?.PhaseName,
                    phaseFraction = ci.MoonIllum?.Fraction,
                    altitude = ci.MoonAltitude,
                    azimuth = ci.MoonAzimuth
                }
            }
        };


        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        return await Task.FromResult(json.ToTextCallToolResponse());
    });

    // ========================= Helpers =========================

    private static Dictionary<string, object?> BuildFormats(Coordinate c)
    {
        // Names chosen to be stable & UI-friendly.
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Decimal"] = new { latitude = c.Latitude.DecimalDegree, longitude = c.Longitude.DecimalDegree },
            ["DMS"] = c.ToString()
        };

        try { dict["UTM"] = c.UTM?.ToString(); } catch { }
        try { dict["MGRS"] = c.MGRS?.ToString(); } catch { }
        try { dict["GEOREF"] = c.GEOREF?.ToString(); } catch { }   // <-- casing fixed
        try
        {
            var wm = c.WebMercator;
            dict["WebMercator"] = wm == null ? null : new { easting = wm.Easting, northing = wm.Northing };
        }
        catch { }

        // ECEF is not under Cartesian — grab it directly
        try
        {
            var ecef = c.ECEF;
            dict["ECEF"] = ecef == null ? null : new { x = ecef.X, y = ecef.Y, z = ecef.Z };
        }
        catch { }


        return dict;
    }

    private static double ToDec(CoordinatePart p) => p.DecimalDegree;

    private static DateTime? TryParseInstant(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return null;
        if (DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
            return dto.UtcDateTime;
        if (DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
            return dt;
        return null;
    }

    private static Coordinate? TryParseCoordinate(string input, DateTime geoDateUtc)
    {
        // 1) fast path: "lat, lon"
        var parts = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
        {
            return new Coordinate(lat, lon, geoDateUtc);
        }

        // 2) library parse (DMS/MGRS/UTM/GEOREF etc.)
        if (Coordinate.TryParse(input, geoDateUtc, out var c)) return c;

        return null;
    }

    // Haversine + initial/final bearing (degrees)
    private static (double meters, double initialBearing, double finalBearing)
        HaversineWithBearings(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371008.8; // mean Earth radius (meters)
        double φ1 = Deg2Rad(lat1), φ2 = Deg2Rad(lat2);
        double Δφ = Deg2Rad(lat2 - lat1);
        double Δλ = Deg2Rad(lon2 - lon1);

        double a = Math.Sin(Δφ / 2) * Math.Sin(Δφ / 2) +
                   Math.Cos(φ1) * Math.Cos(φ2) *
                   Math.Sin(Δλ / 2) * Math.Sin(Δλ / 2);
        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        double d = R * c;

        // initial bearing
        double y = Math.Sin(Δλ) * Math.Cos(φ2);
        double x = Math.Cos(φ1) * Math.Sin(φ2) - Math.Sin(φ1) * Math.Cos(φ2) * Math.Cos(Δλ);
        double θ1 = (Rad2Deg(Math.Atan2(y, x)) + 360.0) % 360.0;

        // final bearing (reverse path)
        y = Math.Sin(-Δλ) * Math.Cos(φ1);
        x = Math.Cos(φ2) * Math.Sin(φ1) - Math.Sin(φ2) * Math.Cos(φ1) * Math.Cos(-Δλ);
        double θ2 = (Rad2Deg(Math.Atan2(y, x)) + 360.0) % 360.0;

        return (d, θ1, θ2);
    }

    private static (double lat, double lon) DestinationLatLon(double latDeg, double lonDeg, double bearingDeg, double distanceMeters)
    {
        const double R = 6371008.8;
        double δ = distanceMeters / R;
        double θ = Deg2Rad(bearingDeg);

        double φ1 = Deg2Rad(latDeg);
        double λ1 = Deg2Rad(lonDeg);

        double sinφ1 = Math.Sin(φ1), cosφ1 = Math.Cos(φ1);
        double sinδ = Math.Sin(δ), cosδ = Math.Cos(δ);
        double sinφ2 = sinφ1 * cosδ + cosφ1 * sinδ * Math.Cos(θ);
        double φ2 = Math.Asin(sinφ2);
        double y = Math.Sin(θ) * sinδ * cosφ1;
        double x = cosδ - sinφ1 * sinφ2;
        double λ2 = λ1 + Math.Atan2(y, x);

        return (Rad2Deg(φ2), NormalizeLon(Rad2Deg(λ2)));
    }

    private static double ParseDistanceMeters(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) throw new Exception("Distance is required.");
        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 1 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var justMeters))
            return justMeters; // assume meters if bare number

        if (parts.Length < 2) throw new Exception("Distance must include a unit, e.g. '12.5 km'.");

        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            throw new Exception("Invalid distance value.");

        var u = parts[1].ToLowerInvariant();
        return u switch
        {
            "m" or "meter" or "meters" => v,
            "km" or "kilometer" or "kilometers" => v * 1000.0,
            "mi" or "mile" or "miles" => v * 1609.344,
            "nm" or "nauticalmile" or "nauticalmiles" => v * 1852.0,
            _ => throw new Exception($"Unsupported distance unit: {parts[1]}")
        };
    }

    private static double Deg2Rad(double d) => d * Math.PI / 180.0;
    private static double Rad2Deg(double r) => r * 180.0 / Math.PI;
    private static double NormalizeLon(double lon)
    {
        // wrap to [-180, 180)
        lon = (lon + 540.0) % 360.0 - 180.0;
        return lon;
    }

}

