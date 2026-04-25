using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MCPhappey.Tools.Anthropic;

namespace MCPhappey.Tools.Anthropic.Environments;

public static partial class AnthropicEnvironments
{
    private static readonly Regex MetadataKeyRegex = new("^[A-Za-z0-9._:-]{1,64}$", RegexOptions.Compiled);
    private static readonly Regex PackageEntryRegex = new(@"^[^\s,;\r\n][^\r\n,;]{0,255}$", RegexOptions.Compiled);
    private static readonly HashSet<string> PackageManagers =
    [
        "apt",
        "cargo",
        "gem",
        "go",
        "npm",
        "pip"
    ];

    private static async Task<JsonObject> GetEnvironmentAsync(
        IServiceProvider serviceProvider,
        string environmentId,
        string? anthropicBeta,
        CancellationToken cancellationToken)
        => await AnthropicManagedAgentsHttp.GetJsonObjectAsync(
            serviceProvider,
            $"{BaseUrl}/{Uri.EscapeDataString(NormalizeEnvironmentId(environmentId))}",
            NormalizeAnthropicBeta(anthropicBeta),
            cancellationToken);

    private static async Task<JsonNode> UpdateEnvironmentAsync(
        IServiceProvider serviceProvider,
        string environmentId,
        string? anthropicBeta,
        JsonObject body,
        CancellationToken cancellationToken)
        => await AnthropicManagedAgentsHttp.SendAsync(
            serviceProvider,
            HttpMethod.Post,
            $"{BaseUrl}/{Uri.EscapeDataString(NormalizeEnvironmentId(environmentId))}",
            body,
            NormalizeAnthropicBeta(anthropicBeta),
            cancellationToken);

    private static JsonObject CreateConfigPatch()
        => new()
        {
            ["type"] = CloudConfigType
        };

    private static void SetStringIfProvided(JsonObject body, string propertyName, string? value)
    {
        if (value is not null)
            body[propertyName] = value;
    }

    private static JsonObject CreateLimitedNetworkingNode(bool allowMcpServers, bool allowPackageManagers, IEnumerable<string> allowedHosts)
    {
        var normalizedHosts = new List<string>();
        foreach (var host in allowedHosts)
        {
            var normalizedHost = NormalizeAllowedHost(host);
            if (!normalizedHosts.Contains(normalizedHost, StringComparer.OrdinalIgnoreCase))
                normalizedHosts.Add(normalizedHost);
        }

        return new JsonObject
        {
            ["type"] = NetworkingTypeLimited,
            ["allow_mcp_servers"] = allowMcpServers,
            ["allow_package_managers"] = allowPackageManagers,
            ["allowed_hosts"] = AnthropicManagedAgentsHttp.ToJsonArray(normalizedHosts)
        };
    }

    private static JsonObject CreateUnrestrictedNetworkingNode()
        => new()
        {
            ["type"] = NetworkingTypeUnrestricted
        };

    private static JsonObject GetLimitedNetworkingOrDefault(JsonObject environment)
    {
        var networking = environment["config"]?["networking"] as JsonObject;
        if (networking is null)
            return CreateLimitedNetworkingNode(false, false, []);

        var networkingType = networking["type"]?.GetValue<string>();
        if (string.Equals(networkingType, NetworkingTypeUnrestricted, StringComparison.OrdinalIgnoreCase))
            return CreateLimitedNetworkingNode(false, false, []);

        if (!string.Equals(networkingType, NetworkingTypeLimited, StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("Environment networking configuration is invalid.");

        return CreateLimitedNetworkingNode(
            GetBooleanOrDefault(networking, "allow_mcp_servers"),
            GetBooleanOrDefault(networking, "allow_package_managers"),
            GetStringValues(networking["allowed_hosts"] as JsonArray, "allowed_hosts"));
    }

    private static JsonObject GetExistingLimitedNetworking(JsonObject environment)
    {
        var networking = environment["config"]?["networking"] as JsonObject
                         ?? throw new ValidationException("The environment is not currently using limited networking.");

        var networkingType = networking["type"]?.GetValue<string>();
        if (!string.Equals(networkingType, NetworkingTypeLimited, StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("The environment is not currently using limited networking.");

        return CreateLimitedNetworkingNode(
            GetBooleanOrDefault(networking, "allow_mcp_servers"),
            GetBooleanOrDefault(networking, "allow_package_managers"),
            GetStringValues(networking["allowed_hosts"] as JsonArray, "allowed_hosts"));
    }

    private static JsonObject EnsurePackagesNode(JsonObject environment)
    {
        var packages = environment["config"]?["packages"] as JsonObject;
        if (packages is null)
        {
            return new JsonObject
            {
                ["type"] = PackagesConfigType
            };
        }

        var clone = AnthropicManagedAgentsHttp.CloneObject(packages);
        clone["type"] = PackagesConfigType;
        return clone;
    }

    private static JsonArray EnsurePackageArray(JsonObject packages, string packageManager)
    {
        ValidatePackageManager(packageManager);

        if (packages[packageManager] is JsonArray values)
            return values;

        if (packages[packageManager] is not null)
            throw new ValidationException($"Package manager '{packageManager}' contains a non-array value.");

        values = new JsonArray();
        packages[packageManager] = values;
        return values;
    }

    private static bool GetBooleanOrDefault(JsonObject node, string propertyName, bool defaultValue = false)
    {
        if (node[propertyName] is null)
            return defaultValue;

        try
        {
            return node[propertyName]!.GetValue<bool>();
        }
        catch (Exception ex)
        {
            throw new ValidationException($"Property '{propertyName}' must be a boolean.", ex);
        }
    }

    private static List<string> GetStringValues(JsonArray? array, string propertyName)
    {
        var values = new List<string>();
        if (array is null)
            return values;

        foreach (var item in array)
        {
            var value = item?.GetValue<string>()
                        ?? throw new ValidationException($"Property '{propertyName}' contains a non-string value.");

            if (!string.IsNullOrWhiteSpace(value))
                values.Add(value.Trim());
        }

        return values;
    }

    private static bool ContainsValue(JsonArray array, string value)
    {
        foreach (var item in array)
        {
            var current = item?.GetValue<string>();
            if (string.Equals(current, value, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool RemoveValue(JsonArray array, string value)
    {
        var removed = false;

        for (var index = array.Count - 1; index >= 0; index--)
        {
            var current = array[index]?.GetValue<string>();
            if (!string.Equals(current, value, StringComparison.OrdinalIgnoreCase))
                continue;

            array.RemoveAt(index);
            removed = true;
        }

        return removed;
    }

    private static string NormalizeEnvironmentId(string? environmentId)
    {
        if (string.IsNullOrWhiteSpace(environmentId))
            throw new ValidationException("environmentId is required.");

        var normalized = environmentId.Trim();
        if (!normalized.StartsWith("env_", StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("environmentId must start with 'env_'.");

        return normalized;
    }

    private static string NormalizeEnvironmentName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ValidationException("name is required.");

        var normalized = name.Trim();
        if (normalized.Length > 256)
            throw new ValidationException("name cannot exceed 256 characters.");

        return normalized;
    }

    private static void ValidateEnvironmentDescription(string? description)
    {
        if (description is not null && description.Length > 2048)
            throw new ValidationException("description cannot exceed 2048 characters.");
    }

    private static string NormalizeMetadataKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ValidationException("key is required.");

        var normalized = key.Trim();
        if (!MetadataKeyRegex.IsMatch(normalized))
            throw new ValidationException("key must be 1-64 characters and contain only letters, numbers, dot, underscore, colon, or hyphen.");

        return normalized;
    }

    private static string NormalizeMetadataValue(string? value)
    {
        if (value is null)
            throw new ValidationException("value is required.");

        if (value.Length == 0)
            throw new ValidationException("value cannot be empty. Use the metadata removal tool to delete a key.");

        if (value.Length > 512)
            throw new ValidationException("value cannot exceed 512 characters.");

        return value;
    }

    private static string? NormalizeAnthropicBeta(string? anthropicBeta)
    {
        if (anthropicBeta is null)
            return null;

        if (string.IsNullOrWhiteSpace(anthropicBeta))
            throw new ValidationException("anthropicBeta cannot be empty.");

        var normalized = anthropicBeta.Trim();
        if (normalized.IndexOfAny([',', ';', '\n', '\r']) >= 0)
            throw new ValidationException("anthropicBeta must be a single beta header value, not a delimited list.");

        if (normalized.Length > 128)
            throw new ValidationException("anthropicBeta cannot exceed 128 characters.");

        return normalized;
    }

    private static string NormalizeAllowedHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new ValidationException("host is required.");

        var normalized = host.Trim();
        if (normalized.Length > 253)
            throw new ValidationException("host cannot exceed 253 characters.");

        if (normalized.Contains("://", StringComparison.Ordinal)
            || normalized.Contains('/', StringComparison.Ordinal)
            || normalized.Contains('\\', StringComparison.Ordinal)
            || normalized.Contains('?', StringComparison.Ordinal)
            || normalized.Contains('#', StringComparison.Ordinal))
        {
            throw new ValidationException("host must be a bare hostname or IP address without scheme, path, query, or fragment.");
        }

        if (normalized.Contains(':', StringComparison.Ordinal) && !IPAddress.TryParse(normalized, out _))
            throw new ValidationException("host must not include a port.");

        var hostType = Uri.CheckHostName(normalized);
        var isLocalhost = string.Equals(normalized, "localhost", StringComparison.OrdinalIgnoreCase);
        if (!isLocalhost && hostType == UriHostNameType.Unknown)
            throw new ValidationException("host must be a valid hostname or IP address.");

        return normalized;
    }

    private static void ValidatePackageManager(string? packageManager)
    {
        if (string.IsNullOrWhiteSpace(packageManager) || !PackageManagers.Contains(packageManager))
            throw new ValidationException("packageManager must be one of: apt, cargo, gem, go, npm, pip.");
    }

    private static string NormalizePackageEntry(string? package)
    {
        if (string.IsNullOrWhiteSpace(package))
            throw new ValidationException("package is required.");

        var normalized = package.Trim();
        if (!PackageEntryRegex.IsMatch(normalized))
            throw new ValidationException("package must be a single package entry without spaces, commas, semicolons, or line breaks.");

        return normalized;
    }
}
