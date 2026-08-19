using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using UnitsNet;

namespace MCPhappey.Tools.GitHub.UnitsNet;

public static partial class UnitsNetService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly Dictionary<string, (string Quantity, string Unit)> PreferredAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["m"] = ("Length", "Meter"),
            ["meter"] = ("Length", "Meter"),
            ["meters"] = ("Length", "Meter"),
            ["metre"] = ("Length", "Meter"),
            ["metres"] = ("Length", "Meter"),
            ["min"] = ("Duration", "Minute"),
            ["minute"] = ("Duration", "Minute"),
            ["minutes"] = ("Duration", "Minute"),
            ["minuut"] = ("Duration", "Minute"),
            ["minuten"] = ("Duration", "Minute"),
            ["liter"] = ("Volume", "Liter"),
            ["liters"] = ("Volume", "Liter"),
            ["litre"] = ("Volume", "Liter"),
            ["litres"] = ("Volume", "Liter"),
            ["l"] = ("Volume", "Liter"),
            ["gallon"] = ("Volume", "UsGallon"),
            ["gallons"] = ("Volume", "UsGallon"),
            ["gal"] = ("Volume", "UsGallon"),
            ["mile"] = ("Length", "Mile"),
            ["miles"] = ("Length", "Mile"),
            ["mijl"] = ("Length", "Mile"),
            ["mijlen"] = ("Length", "Mile"),
            ["pound"] = ("Mass", "Pound"),
            ["pounds"] = ("Mass", "Pound"),
            ["pond"] = ("Mass", "Pound"),
            ["uur"] = ("Duration", "Hour"),
            ["uren"] = ("Duration", "Hour")
        };

    [Description("Calculates the ratio between two quantities of the same UnitsNet quantity type.")]
    [McpServerTool(Title = "Compare quantities (ratio)", Name = "github_unitsnet_ratio", ReadOnly = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(UnitsNetRatio))]
    public static Task<CallToolResult?> GitHubUnitsNet_Ratio(
        [Description("First quantity, for example '50 m' or '50 meters'.")] string first,
        [Description("Second quantity, for example '10 m' or '10 meters'.")] string second,
        [Description("Optional UnitsNet quantity name used to disambiguate units, for example 'Length'.")] string? quantityType = null)
        => Execute(() =>
        {
            var q1 = ParseQuantity(first, quantityType);
            var q2 = ParseQuantity(second, quantityType);
            EnsureSameQuantityType(q1, q2);
            if (q2.Value == 0) throw new UnitsNetToolException("DIVIDE_BY_ZERO", "The denominator cannot be zero.");
            var ratio = q1.Value / UnitConverter.Default.ConvertTo(q2, q1.Unit).Value;
            return new UnitsNetRatio(ratio, ToDto(q1), ToDto(q2));
        });

    [Description("Adds two quantities of the same UnitsNet quantity type.")]
    [McpServerTool(Title = "Add quantities", Name = "github_unitsnet_add", ReadOnly = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(UnitsNetArithmetic))]
    public static Task<CallToolResult?> GitHubUnitsNet_Add(string first, string second, string? quantityType = null)
        => Execute(() => Arithmetic(first, second, quantityType, "Add", static (a, b) => a + b));

    [Description("Converts a numeric value between exact UnitsNet quantity and unit names, for example Length/Kilometer/Mile.")]
    [McpServerTool(Title = "Convert by name", Name = "github_unitsnet_convert_by_name", ReadOnly = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(UnitsNetConversion))]
    public static Task<CallToolResult?> GitHubUnitsNet_ConvertByName(string quantityName, string fromUnit, string toUnit, double value)
        => Execute(() =>
        {
            var quantity = CreateQuantity(value, ResolveUnit(fromUnit, quantityName));
            return ConversionResult(quantity, ResolveUnit(toUnit, quantityName));
        });

    [Description("Converts English or Dutch text such as 'convert 10 km to miles', 'convert 5 liters to gallons', or 'zet 10 km om naar mijl'.")]
    [McpServerTool(Title = "Auto-detect and convert text", Name = "github_unitsnet_auto_convert_text", ReadOnly = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(UnitsNetConversion))]
    public static Task<CallToolResult?> GitHubUnitsNet_AutoConvertText(string text, string? quantityType = null)
        => Execute(() =>
        {
            var match = AutoConvertRegex().Match(text.Trim());
            if (!match.Success)
                throw new UnitsNetToolException("INVALID_FORMAT", "Use 'convert <value> <unit> to <unit>' or 'zet <value> <unit> om naar <unit>'.");

            var input = $"{match.Groups["value"].Value} {match.Groups["from"].Value.Trim()}";
            var quantity = ParseQuantity(input, quantityType);
            return ConversionResult(quantity, ResolveUnit(match.Groups["to"].Value.Trim(), quantity.GetQuantityInfo().Name));
        });

    [Description("Converts a quantity to another compatible unit. Unit abbreviations and English singular/plural names are supported.")]
    [McpServerTool(Title = "Convert units", Name = "github_unitsnet_convert", ReadOnly = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(UnitsNetConversion))]
    public static Task<CallToolResult?> GitHubUnitsNet_Convert(string input, string targetUnit, string? quantityType = null)
        => Execute(() =>
        {
            var quantity = ParseQuantity(input, quantityType);
            return ConversionResult(quantity, ResolveUnit(targetUnit, quantity.GetQuantityInfo().Name));
        });

    [Description("Parses a quantity and returns explicit UnitsNet-aligned structured data.")]
    [McpServerTool(Title = "Parse quantity", Name = "github_unitsnet_parse", ReadOnly = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(UnitsNetQuantity))]
    public static Task<CallToolResult?> GitHubUnitsNet_Parse(string input, string? quantityType = null)
        => Execute(() => ToDto(ParseQuantity(input, quantityType)));

    [Description("Lists all UnitsNet quantity types.")]
    [McpServerTool(Title = "List quantity types", Name = "github_unitsnet_list_quantity_types", ReadOnly = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(UnitsNetQuantityInfoList))]
    public static Task<CallToolResult?> GitHubUnitsNet_ListQuantityTypes()
        => Execute(() => new UnitsNetQuantityInfoList([.. Quantity.Infos.OrderBy(x => x.Name).Select(ToInfoDto)]));

    [Description("Lists all UnitsNet units and abbreviations for a quantity type.")]
    [McpServerTool(Title = "List units for type", Name = "github_unitsnet_list_units_for_type", ReadOnly = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(UnitsNetUnitInfoList))]
    public static Task<CallToolResult?> GitHubUnitsNet_ListUnitsForType(string quantityType)
        => Execute(() =>
        {
            var info = ResolveQuantityInfo(quantityType);
            return new UnitsNetUnitInfoList(ToInfoDto(info), [.. info.UnitInfos.OrderBy(x => x.Name).Select(ToUnitInfoDto)]);
        });

    [Description("Subtracts two quantities of the same UnitsNet quantity type.")]
    [McpServerTool(Title = "Subtract quantities", Name = "github_unitsnet_subtract", ReadOnly = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(UnitsNetArithmetic))]
    public static Task<CallToolResult?> GitHubUnitsNet_Subtract(string first, string second, string? quantityType = null)
        => Execute(() => Arithmetic(first, second, quantityType, "Subtract", static (a, b) => a - b));

    [Description("Multiplies a quantity by a scalar.")]
    [McpServerTool(Title = "Multiply quantity", Name = "github_unitsnet_multiply", ReadOnly = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(UnitsNetScalarOperation))]
    public static Task<CallToolResult?> GitHubUnitsNet_Multiply(string input, double factor, string? quantityType = null)
        => Execute(() => Scalar(input, factor, quantityType, "Multiply", static (a, b) => a * b));

    [Description("Divides a quantity by a non-zero scalar.")]
    [McpServerTool(Title = "Divide quantity", Name = "github_unitsnet_divide", ReadOnly = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(UnitsNetScalarOperation))]
    public static Task<CallToolResult?> GitHubUnitsNet_Divide(string input, double divisor, string? quantityType = null)
        => Execute(() =>
        {
            if (divisor == 0) throw new UnitsNetToolException("DIVIDE_BY_ZERO", "The divisor cannot be zero.");
            return Scalar(input, divisor, quantityType, "Divide", static (a, b) => a / b);
        });

    [Description("Returns UnitsNet quantity metadata and all UnitInfo entries.")]
    [McpServerTool(Title = "Get quantity information", Name = "github_unitsnet_get_quantity_info", ReadOnly = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(UnitsNetUnitInfoList))]
    public static Task<CallToolResult?> GitHubUnitsNet_GetQuantityInfo(string quantityType)
        => GitHubUnitsNet_ListUnitsForType(quantityType);

    [Description("Detects a quantity and lists all compatible UnitsNet units.")]
    [McpServerTool(Title = "Get compatible units", Name = "github_unitsnet_get_compatible_units", ReadOnly = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(UnitsNetCompatibleUnits))]
    public static Task<CallToolResult?> GitHubUnitsNet_GetCompatibleUnits(string input, string? quantityType = null)
        => Execute(() =>
        {
            var quantity = ParseQuantity(input, quantityType);
            var info = quantity.GetQuantityInfo();
            return new UnitsNetCompatibleUnits(ToDto(quantity), [.. info.UnitInfos.OrderBy(x => x.Name).Select(ToUnitInfoDto)]);
        });

    [Description("Compares two quantities of the same UnitsNet quantity type.")]
    [McpServerTool(Title = "Compare quantities", Name = "github_unitsnet_compare", ReadOnly = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(UnitsNetComparison))]
    public static Task<CallToolResult?> GitHubUnitsNet_Compare(string first, string second, string? quantityType = null)
        => Execute(() =>
        {
            var q1 = ParseQuantity(first, quantityType);
            var q2 = ParseQuantity(second, quantityType);
            EnsureSameQuantityType(q1, q2);
            var comparison = q1.Value.CompareTo(UnitConverter.Default.ConvertTo(q2, q1.Unit).Value) switch
            {
                < 0 => "LessThan",
                > 0 => "GreaterThan",
                _ => "Equal"
            };
            return new UnitsNetComparison(comparison, ToDto(q1), ToDto(q2));
        });

    [Description("Checks whether two parsed quantities have the same UnitsNet quantity type.")]
    [McpServerTool(Title = "Check quantity compatibility", Name = "github_unitsnet_is_compatible", ReadOnly = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(UnitsNetCompatibility))]
    public static Task<CallToolResult?> GitHubUnitsNet_IsCompatible(string first, string second)
        => Execute(() =>
        {
            var q1 = ParseQuantity(first);
            var q2 = ParseQuantity(second);
            return new UnitsNetCompatibility(q1.GetQuantityInfo().Name == q2.GetQuantityInfo().Name, ToDto(q1), ToDto(q2));
        });

    [Description("Converts a quantity to every compatible UnitsNet unit.")]
    [McpServerTool(Title = "Convert to all compatible units", Name = "github_unitsnet_convert_all", ReadOnly = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(UnitsNetConversionList))]
    public static Task<CallToolResult?> GitHubUnitsNet_ConvertAll(string input, string? quantityType = null)
        => Execute(() =>
        {
            var quantity = ParseQuantity(input, quantityType);
            var results = quantity.GetQuantityInfo().UnitInfos.OrderBy(x => x.Name)
                .Select(x => ToDto(UnitConverter.Default.ConvertTo(quantity, x.Value))).ToArray();
            return new UnitsNetConversionList(ToDto(quantity), results);
        });

    private static UnitsNetArithmetic Arithmetic(string first, string second, string? quantityType, string operation, Func<double, double, double> calculate)
    {
        var q1 = ParseQuantity(first, quantityType);
        var q2 = ParseQuantity(second, quantityType);
        EnsureSameQuantityType(q1, q2);
        var result = CreateQuantity(calculate(q1.Value, UnitConverter.Default.ConvertTo(q2, q1.Unit).Value), q1.Unit);
        return new UnitsNetArithmetic(operation, ToDto(q1), ToDto(q2), ToDto(result));
    }

    private static UnitsNetScalarOperation Scalar(string input, double scalar, string? quantityType, string operation, Func<double, double, double> calculate)
    {
        var quantity = ParseQuantity(input, quantityType);
        var result = CreateQuantity(calculate(quantity.Value, scalar), quantity.Unit);
        return new UnitsNetScalarOperation(operation, ToDto(quantity), scalar, ToDto(result));
    }

    private static UnitsNetConversion ConversionResult(IQuantity source, Enum targetUnit)
        => new(ToDto(source), ToDto(UnitConverter.Default.ConvertTo(source, targetUnit)));

    private static IQuantity ParseQuantity(string input, string? quantityType = null)
    {
        var match = QuantityRegex().Match(input.Trim());
        if (!match.Success || !double.TryParse(match.Groups["value"].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            throw new UnitsNetToolException("INVALID_QUANTITY", $"Invalid quantity format: '{input}'.");

        return CreateQuantity(value, ResolveUnit(match.Groups["unit"].Value.Trim(), quantityType));
    }

    private static Enum ResolveUnit(string input, string? quantityType = null)
    {
        var token = input.Trim().TrimEnd('.');
        if (PreferredAliases.TryGetValue(token, out var preferred) &&
            (quantityType is null || preferred.Quantity.Equals(quantityType, StringComparison.OrdinalIgnoreCase)))
            return FindUnit(ResolveQuantityInfo(preferred.Quantity), preferred.Unit);

        var infos = quantityType is null ? Quantity.Infos : [ResolveQuantityInfo(quantityType)];
        var matches = new List<(QuantityInfo Info, Enum Unit)>();
        foreach (var info in infos)
        {
            foreach (var unitInfo in info.UnitInfos)
            {
                var abbreviations = UnitsNetSetup.Default.UnitAbbreviations.GetUnitAbbreviations(unitInfo.Value);
                if (NameMatches(token, unitInfo.Name) || abbreviations.Any(x => x.Equals(token, StringComparison.OrdinalIgnoreCase)))
                    matches.Add((info, unitInfo.Value));
            }
        }

        if (matches.Count == 1) return matches[0].Unit;
        if (matches.Count > 1)
            throw new UnitsNetToolException("AMBIGUOUS_UNIT", $"The unit '{input}' is ambiguous. Supply quantityType. Candidates: {string.Join(", ", matches.Select(x => x.Info.Name).Distinct())}.");
        throw new UnitsNetToolException("INVALID_UNIT", $"The unit '{input}' is not supported{(quantityType is null ? "." : $" for quantity '{quantityType}'.")}");
    }

    private static bool NameMatches(string input, string unitName)
    {
        var normalizedInput = NormalizeName(input);
        var normalizedName = NormalizeName(unitName);
        return normalizedInput == normalizedName || Singularize(normalizedInput) == normalizedName;
    }

    private static string NormalizeName(string value) => Regex.Replace(value, "[^a-z0-9]", "", RegexOptions.IgnoreCase).ToLowerInvariant();
    private static string Singularize(string value) => value.EndsWith("ies", StringComparison.Ordinal) ? $"{value[..^3]}y" : value.EndsWith("es", StringComparison.Ordinal) ? value[..^2] : value.EndsWith('s') ? value[..^1] : value;

    private static QuantityInfo ResolveQuantityInfo(string quantityType)
        => Quantity.Infos.FirstOrDefault(x => x.Name.Equals(quantityType.Trim(), StringComparison.OrdinalIgnoreCase))
           ?? throw new UnitsNetToolException("INVALID_QUANTITY_TYPE", $"Unknown UnitsNet quantity type '{quantityType}'.");

    private static Enum FindUnit(QuantityInfo info, string unitName)
        => info.UnitInfos.FirstOrDefault(x => x.Name.Equals(unitName, StringComparison.OrdinalIgnoreCase))?.Value
           ?? throw new UnitsNetToolException("INVALID_UNIT", $"Unknown unit '{unitName}' for quantity '{info.Name}'.");

    private static IQuantity CreateQuantity(double value, Enum unit) => Quantity.From(value, unit);

    private static void EnsureSameQuantityType(IQuantity first, IQuantity second)
    {
        if (first.GetQuantityInfo().Name != second.GetQuantityInfo().Name)
            throw new UnitsNetToolException("INCOMPATIBLE_QUANTITIES", $"Quantities must have the same UnitsNet quantity type ({first.GetQuantityInfo().Name} vs {second.GetQuantityInfo().Name}).");
    }

    private static UnitsNetQuantity ToDto(IQuantity quantity)
        => new(quantity.Value, quantity.Unit.ToString(), ToInfoDto(quantity.GetQuantityInfo()), ToUnitInfoDto(quantity.GetQuantityInfo().UnitInfos.First(x => Equals(x.Value, quantity.Unit))));

    private static UnitsNetQuantityInfo ToInfoDto(QuantityInfo info)
        => new(info.Name, info.QuantityType.Name, info.UnitType.Name);

    private static UnitsNetUnitInfo ToUnitInfoDto(UnitInfo info)
        => new(info.Name, info.Value.ToString(), [.. UnitsNetSetup.Default.UnitAbbreviations.GetUnitAbbreviations(info.Value)]);

    private static Task<CallToolResult?> Execute(Func<object> action)
    {
        try { 
            return Task.FromResult<CallToolResult?>(Structured(action())); }
        catch (UnitsNetToolException exception)
        {
            return Task.FromResult<CallToolResult?>(Structured(new UnitsNetError(false, exception.Code, exception.Message), true));
        }
        catch (Exception)
        {
            return Task.FromResult<CallToolResult?>(Structured(new UnitsNetError(false, "UNITSNET_ERROR", "UnitsNet could not process the request."), true));
        }
    }

    private static CallToolResult Structured(object value, bool isError = false)
        => new()
        {
            IsError = isError,
            StructuredContent = JsonSerializer.SerializeToElement(value, value.GetType(), JsonOptions)
        };

    [GeneratedRegex(@"^\s*(?<value>[+-]?(?:\d+(?:[.,]\d+)?|[.,]\d+))\s*(?<unit>.+?)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex QuantityRegex();

    [GeneratedRegex(@"^\s*(?:(?:convert|converteer)\s+|zet\s+)(?<value>[+-]?(?:\d+(?:[.,]\d+)?|[.,]\d+))\s+(?<from>.+?)\s+(?:(?:to|naar)|om\s+naar)\s+(?<to>.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AutoConvertRegex();
}

public sealed record UnitsNetQuantity(double Value, string Unit, UnitsNetQuantityInfo QuantityInfo, UnitsNetUnitInfo UnitInfo);
public sealed record UnitsNetQuantityInfo(string Name, string QuantityType, string UnitType);
public sealed record UnitsNetUnitInfo(string Name, string Value, IReadOnlyList<string> Abbreviations);
public sealed record UnitsNetQuantityInfoList(IReadOnlyList<UnitsNetQuantityInfo> QuantityInfos);
public sealed record UnitsNetUnitInfoList(UnitsNetQuantityInfo QuantityInfo, IReadOnlyList<UnitsNetUnitInfo> UnitInfos);
public sealed record UnitsNetCompatibleUnits(UnitsNetQuantity Quantity, IReadOnlyList<UnitsNetUnitInfo> UnitInfos);
public sealed record UnitsNetConversion(UnitsNetQuantity Source, UnitsNetQuantity Result);
public sealed record UnitsNetConversionList(UnitsNetQuantity Source, IReadOnlyList<UnitsNetQuantity> Results);
public sealed record UnitsNetArithmetic(string Operation, UnitsNetQuantity First, UnitsNetQuantity Second, UnitsNetQuantity Result);
public sealed record UnitsNetScalarOperation(string Operation, UnitsNetQuantity Quantity, double Scalar, UnitsNetQuantity Result);
public sealed record UnitsNetRatio(double Ratio, UnitsNetQuantity First, UnitsNetQuantity Second);
public sealed record UnitsNetComparison(string Comparison, UnitsNetQuantity First, UnitsNetQuantity Second);
public sealed record UnitsNetCompatibility(bool IsCompatible, UnitsNetQuantity First, UnitsNetQuantity Second);
public sealed record UnitsNetError(bool Success, string Error, string Message);

internal sealed class UnitsNetToolException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
