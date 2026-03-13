using System.ComponentModel;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Azuce;

public static class AzuceForecast
{
    [Description("Generate a solar production forecast with optional financial and carbon analysis via Azuce POST /forecast.")]
    [McpServerTool(
        Name = "azuce_forecast_generate",
        Title = "Azuce forecast generate",
        ReadOnly = true,
        OpenWorld = true,
        Destructive = false)]
    public static async Task<CallToolResult?> Azuce_Forecast_Generate(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Latitude of the installation site. Range: -90 to 90.")] double latitude,
        [Description("Longitude of the installation site. Range: -180 to 180.")] double longitude,
        [Description("Total system capacity in kilowatts (kWp). Range: 0.1 to 10000.")] double systemSizeKw,
        [Description("Panel compass direction in degrees. 0 = North, 90 = East, 180 = South, 270 = West. Range: 0 to 360.")] double panelAzimuth,
        [Description("Panel angle from horizontal in degrees. Range: 0 to 90.")] double panelTilt,
        [Description("Optional shading loss percentage. Range: 0 to 30.")] double? shadingLoss = null,
        [Description("Optional total system losses percentage. Range: 0 to 50.")] double? systemLosses = null,
        [Description("Optional temperature coefficient of power (%/°C). Range: -1 to 0.")] double? tempCoefficient = null,
        [Description("Optional human-readable address stored for reference only.")] string? address = null,
        [Description("Optional customer or project name stored for reference only.")] string? customerName = null,
        [Description("Enable financial analysis in the forecast result.")] bool? enableFinancials = null,
        [Description("Optional total installed cost of the solar system in the specified currency.")] double? installedCost = null,
        [Description("Optional electricity purchase rate per kWh in the specified currency.")] double? electricityRate = null,
        [Description("Optional feed-in or export tariff per kWh in the specified currency.")] double? exportRate = null,
        [Description("Optional estimated percentage of generated energy consumed on-site. Range: 0 to 100.")] double? selfConsumptionPct = null,
        [Description("Optional annual maintenance cost in the specified currency.")] double? annualMaintenanceCost = null,
        [Description("Optional annual panel degradation rate. Range: 0 to 5.")] double? annualDegradationPct = null,
        [Description("Enable carbon impact analysis in the forecast result.")] bool? enableCarbon = null,
        [Description("Optional grid carbon intensity in kg CO2 per kWh.")] double? gridKgCo2PerKwh = null,
        [Description("Optional ISO 4217 currency code such as USD, GBP, or EUR.")] string? currencyCode = null,
        [Description("Optional currency symbol for display such as $, £, or €.")] string? currencySymbol = null,
        [Description("Optional ISO 3166-1 alpha-2 country code used for defaults.")] string? countryCode = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ValidateRange(latitude, -90, 90, nameof(latitude));
                ValidateRange(longitude, -180, 180, nameof(longitude));
                ValidateRange(systemSizeKw, 0.1, 10000, nameof(systemSizeKw));
                ValidateRange(panelAzimuth, 0, 360, nameof(panelAzimuth));
                ValidateRange(panelTilt, 0, 90, nameof(panelTilt));
                ValidateOptionalRange(shadingLoss, 0, 30, nameof(shadingLoss));
                ValidateOptionalRange(systemLosses, 0, 50, nameof(systemLosses));
                ValidateOptionalRange(tempCoefficient, -1, 0, nameof(tempCoefficient));
                ValidateOptionalRange(selfConsumptionPct, 0, 100, nameof(selfConsumptionPct));
                ValidateOptionalRange(annualDegradationPct, 0, 5, nameof(annualDegradationPct));

                var payload = new JsonObject
                {
                    ["latitude"] = latitude,
                    ["longitude"] = longitude,
                    ["systemSizeKw"] = systemSizeKw,
                    ["panelAzimuth"] = panelAzimuth,
                    ["panelTilt"] = panelTilt,
                    ["shadingLoss"] = shadingLoss,
                    ["systemLosses"] = systemLosses,
                    ["tempCoefficient"] = tempCoefficient,
                    ["address"] = NullIfWhiteSpace(address),
                    ["customerName"] = NullIfWhiteSpace(customerName),
                    ["enableFinancials"] = enableFinancials,
                    ["installedCost"] = installedCost,
                    ["electricityRate"] = electricityRate,
                    ["exportRate"] = exportRate,
                    ["selfConsumptionPct"] = selfConsumptionPct,
                    ["annualMaintenanceCost"] = annualMaintenanceCost,
                    ["annualDegradationPct"] = annualDegradationPct,
                    ["enableCarbon"] = enableCarbon,
                    ["gridKgCo2PerKwh"] = gridKgCo2PerKwh,
                    ["currencyCode"] = NullIfWhiteSpace(currencyCode),
                    ["currencySymbol"] = NullIfWhiteSpace(currencySymbol),
                    ["countryCode"] = NullIfWhiteSpace(countryCode)
                };

                var client = serviceProvider.GetRequiredService<AzuceClient>();
                return await client.PostAsync("forecast", payload, cancellationToken);
            }));

    private static void ValidateRange(double value, double min, double max, string parameterName)
    {
        if (value < min || value > max)
            throw new ArgumentOutOfRangeException(parameterName, value, $"{parameterName} must be between {min} and {max}.");
    }

    private static void ValidateOptionalRange(double? value, double min, double max, string parameterName)
    {
        if (value.HasValue)
            ValidateRange(value.Value, min, max, parameterName);
    }

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
