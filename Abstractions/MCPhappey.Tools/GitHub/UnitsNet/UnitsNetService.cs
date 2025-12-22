using System.ComponentModel;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using UnitsNet;

namespace MCPhappey.Tools.GitHub.UnitsNet;

public static class UnitsNetService
{
    [Description("Calculates the ratio between two quantities of the same type (e.g. '50 m' and '10 m' → 5).")]
    [McpServerTool(
        Title = "Compare quantities (ratio)",
        Name = "github_unitsnet_ratio",
        ReadOnly = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GitHubUnitsNet_Ratio(
    RequestContext<CallToolRequestParams> requestContext,
    [Description("First quantity (e.g. '50 meters')")] string first,
    [Description("Second quantity (e.g. '10 meters')")] string second)
    => await requestContext.WithExceptionCheck(async () =>
    {
        var q1 = TryParseAnyQuantity(first);
        var q2 = TryParseAnyQuantity(second);
        if (q1 is null || q2 is null)
            throw new Exception("Invalid input(s).");

        if (q1.QuantityInfo.Name != q2.QuantityInfo.Name)
            throw new Exception("Quantities must be of the same type (e.g. both Length, Mass, etc.).");

        var ratio = q1.Value / q2.ToUnit(q1.Unit).Value;
        return await Task.FromResult($"{ratio:F3}".ToTextCallToolResponse());
    });

    // ADD
    [Description("Adds two quantities of the same type (e.g. '5 m' + '2 m' → '7 m').")]
    [McpServerTool(
        Title = "Add quantities",
        Name = "github_unitsnet_add",
        ReadOnly = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GitHubUnitsNet_Add(
        RequestContext<CallToolRequestParams> requestContext,
        [Description("First quantity (e.g. '5 meters')")] string first,
        [Description("Second quantity (e.g. '2 meters')")] string second)
    => await requestContext.WithExceptionCheck(async () =>
    {
        var q1 = TryParseAnyQuantity(first);
        var q2 = TryParseAnyQuantity(second);
        if (q1 is null || q2 is null) throw new Exception("Invalid input(s).");
        if (q1.QuantityInfo.Name != q2.QuantityInfo.Name) throw new Exception("Quantities must be of the same type.");

        var sum = q1.Value + q2.ToUnit(q1.Unit).Value;
        return await Task.FromResult($"{sum:F3} {q1.Unit}".ToTextCallToolResponse());
    });

    // CONVERT BY NAME
    [Description("Converts a numeric value between two specific units by quantity name (e.g. 'Length', 'Centimeter', 'Meter').")]
    [McpServerTool(
        Title = "Convert by name",
        Name = "github_unitsnet_convert_by_name",
        ReadOnly = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GitHubUnitsNet_ConvertByName(
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Quantity type (e.g. 'Length', 'Mass', 'Temperature')")] string quantityName,
        [Description("From unit (e.g. 'Centimeter')")] string fromUnit,
        [Description("To unit (e.g. 'Meter')")] string toUnit,
        [Description("Value to convert")] double value)
    => await requestContext.WithExceptionCheck(async () =>
    {
        var result = UnitConverter.ConvertByName(value, quantityName, fromUnit, toUnit);
        return await Task.FromResult($"{result:F4}".ToTextCallToolResponse());
    });
    // AUTO-DETECT & CONVERT TEXT
    [Description("Parses a text like 'convert 10 km to miles' and performs the conversion automatically.")]
    [McpServerTool(
        Title = "Auto-detect and convert text",
        Name = "github_unitsnet_auto_convert_text",
        ReadOnly = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GitHubUnitsNet_AutoConvertText(
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Conversion request text (e.g. 'convert 5 liters to gallons')")] string text)
    => await requestContext.WithExceptionCheck(async () =>
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var toIndex = Array.IndexOf(parts, "to");
        if (parts.Length < 4 || toIndex < 3 || toIndex + 1 >= parts.Length)
            throw new Exception("Invalid format. Try: 'convert 10 km to miles'");

        if (!double.TryParse(parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var value))
            throw new Exception("Invalid numeric value.");

        var fromUnit = parts[2];
        var toUnit = parts[toIndex + 1];

        foreach (var q in Quantity.Infos)
        {
            if (UnitsNetSetup.Default.UnitParser.TryParse(fromUnit, q.UnitType, out var fromEnum) &&
                UnitsNetSetup.Default.UnitParser.TryParse(toUnit, q.UnitType, out var toEnum))
            {
                var result = UnitConverter.Convert(value, fromEnum, toEnum);
                return await Task.FromResult($"{value} {fromUnit} = {result:F4} {toUnit}".ToTextCallToolResponse());
            }
        }

        throw new Exception("Unable to determine quantity type from input.");
    });

    // CONVERT (parsed quantity → target unit)
    [Description("Converts a quantity between different units (e.g. '10 kilometers' to 'miles').")]
    [McpServerTool(
        Title = "Convert units",
        Name = "github_unitsnet_convert",
        ReadOnly = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GitHubUnitsNet_Convert(
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Quantity with unit (e.g. '10 kilometers', '25 °C', '100 kg')")] string input,
        [Description("Target unit abbreviation or name (e.g. 'miles', '°F', 'pounds')")] string targetUnit)
    => await requestContext.WithExceptionCheck(async () =>
    {
        var quantity = TryParseAnyQuantity(input) ?? throw new Exception($"Invalid quantity format: '{input}'");

        var unitType = quantity.QuantityInfo.UnitType;
        if (!UnitsNetSetup.Default.UnitParser.TryParse(targetUnit, unitType, out var targetUnitEnum))
            throw new Exception($"Unknown or invalid target unit '{targetUnit}' for quantity '{quantity.QuantityInfo.Name}'.");

        var converted = quantity.ToUnit(targetUnitEnum);
        return await Task.FromResult(converted.ToString().ToTextCallToolResponse());
    });

    // PARSE
    [Description("Parses a quantity string and returns its value and unit.")]
    [McpServerTool(
        Title = "Parse quantity",
        Name = "github_unitsnet_parse",
        ReadOnly = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GitHubUnitsNet_Parse(
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Quantity string (e.g. '5 meters', '120 °F', '3.5 hours')")] string input)
    => await requestContext.WithExceptionCheck(async () =>
    {
        var q = TryParseAnyQuantity(input) ?? throw new Exception($"Invalid quantity format: '{input}'");
        return await Task.FromResult($"{q.Value} {q.Unit}".ToTextCallToolResponse());
    });

    // LIST QUANTITY TYPES
    [Description("Lists all supported quantity types (e.g. Length, Mass, Temperature).")]
    [McpServerTool(
        Title = "List quantity types",
        Name = "github_unitsnet_list_quantity_types",
        ReadOnly = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GitHubUnitsNet_ListQuantityTypes(
        RequestContext<CallToolRequestParams> requestContext)
    => await requestContext.WithExceptionCheck(async () =>
    {
        var types = Quantity.Infos.Select(q => q.Name).OrderBy(x => x).ToArray();
        return await Task.FromResult(string.Join("\n", types).ToTextCallToolResponse());
    });

    // LIST UNITS FOR TYPE
    [Description("Lists all units available for a specific quantity type (e.g. 'Length').")]
    [McpServerTool(
        Title = "List units for type",
        Name = "github_unitsnet_list_units_for_type",
        ReadOnly = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GitHubUnitsNet_ListUnitsForType(
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Quantity type name (e.g. 'Length', 'Temperature', 'Mass')")] string quantityType)
    => await requestContext.WithExceptionCheck(async () =>
    {
        var info = Quantity.Infos.FirstOrDefault(q =>
            string.Equals(q.Name, quantityType, StringComparison.OrdinalIgnoreCase))
            ?? throw new Exception($"Unknown quantity type: {quantityType}");

        var units = info.UnitInfos.Select(u => u.Name).OrderBy(x => x).ToArray();
        return await Task.FromResult(string.Join("\n", units).ToTextCallToolResponse());
    });

    // -------- Helper --------
    private static IQuantity? TryParseAnyQuantity(string input)
    {
        foreach (var info in Quantity.Infos)
        {
            if (Quantity.TryParse(info.QuantityType, input, out var q) && q is not null)
                return q;
        }
        return null;
    }
}
