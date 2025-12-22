using MCPhappey.Common.Models;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using MCPhappey.Auth.Models;
using MCPhappey.Auth.Extensions;
using MCPhappey.Common;
using Microsoft.PowerBI.Api;
using MCPhappey.Core.Extensions;
using System.Text.Json;
using System.Globalization;

namespace MCPhappey.Tools.PowerBI;

public static class PowerBIClientExtensions
{
    public static async Task<PowerBIClient> GetOboPowerBIClient(this IServiceProvider serviceProvider,
        McpServer mcpServer)
    {
        var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var tokenService = serviceProvider.GetService<HeaderProvider>();
        var oAuthSettings = serviceProvider.GetService<OAuthSettings>();
        var server = serviceProvider.GetServerConfig(mcpServer);

        return await httpClientFactory.GetOboPowerBIClient(tokenService?.Bearer!, server?.Server!, oAuthSettings!);
    }

    public static async Task<PowerBIClient> GetOboPowerBIClient(this IHttpClientFactory httpClientFactory,
      string token,
      Server server,
      OAuthSettings oAuthSettings)
    {
        var delegated = await httpClientFactory.GetOboToken(token, "api.powerbi.com", server, oAuthSettings);
        var tokenCredentials = new Microsoft.Rest.TokenCredentials(delegated, "Bearer");
        return new PowerBIClient(new Uri("https://api.powerbi.com/"), tokenCredentials);
    }


    // --- UTILITIES BELOW ---

    // Type detection (string, Int64, Double, DateTime, Boolean)
    public static string DeterminePowerBIDataType(this IEnumerable<string> values)
    {
        bool isInt64 = true, isDouble = true, isDateTime = true, isBoolean = true;

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) isInt64 = false;
            if (!double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out _)) isDouble = false;
            if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) isDateTime = false;
            if (!bool.TryParse(value, out _)) isBoolean = false;
            if (!isInt64 && !isDouble && !isDateTime && !isBoolean) break;
        }
        if (isInt64) return "Int64";
        if (isDouble) return "Double";
        if (isDateTime) return "DateTime";
        if (isBoolean) return "Boolean";
        return "string";
    }

    // Per-value parse helper
    public static object? ParseValue(this string? value, string dataType)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        switch (dataType.ToLowerInvariant())
        {
            case "int64":
                if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) return l;
                break;
            case "double":
                if (double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var d)) return d;
                break;
            case "datetime":
                if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt)) return dt.ToString("o");
                break;
            case "boolean":
                if (bool.TryParse(value, out var b)) return b;
                break;
            default:
                return value;
        }
        return value;
    }

    // Ensure any JsonElement/etc is native .NET value
    public static Dictionary<string, object?> ToNativeDictionary(this Dictionary<string, object> dict)
    {
        var result = new Dictionary<string, object?>();
        foreach (var kvp in dict)
        {
            if (kvp.Value is JsonElement elem)
            {
                switch (elem.ValueKind)
                {
                    case JsonValueKind.String: result[kvp.Key] = elem.GetString(); break;
                    case JsonValueKind.Number:
                        if (elem.TryGetInt64(out var l)) result[kvp.Key] = l;
                        else if (elem.TryGetDouble(out var d)) result[kvp.Key] = d;
                        else result[kvp.Key] = elem.GetRawText();
                        break;
                    case JsonValueKind.True:
                    case JsonValueKind.False: result[kvp.Key] = elem.GetBoolean(); break;
                    case JsonValueKind.Null:
                    case JsonValueKind.Undefined: result[kvp.Key] = null; break;
                    default: result[kvp.Key] = elem.GetRawText(); break;
                }
            }
            else if (kvp.Value is DBNull) result[kvp.Key] = null;
            else result[kvp.Key] = kvp.Value;
        }
        return result;
    }


}

