using System.ComponentModel.DataAnnotations;

namespace MCPhappey.Tools.AgentSandbox;

internal static class AgentSandboxHelpers
{
    public static string Require(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ValidationException($"{parameterName} is required.");

        return value.Trim();
    }

    public static string[]? NormalizeFileIds(string[]? fileIds, bool required = false)
    {
        var normalized = fileIds?
            .Where(fileId => !string.IsNullOrWhiteSpace(fileId))
            .Select(fileId => fileId.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (required && normalized?.Length is not > 0)
            throw new ValidationException("At least one non-empty file ID is required.");

        return normalized?.Length > 0 ? normalized : null;
    }

    public static Dictionary<string, string>? NormalizeEnvironmentVariables(
        Dictionary<string, string>? environmentVariables)
    {
        if (environmentVariables is null || environmentVariables.Count == 0)
            return null;

        if (environmentVariables.Keys.Any(string.IsNullOrWhiteSpace))
            throw new ValidationException("Environment variable names cannot be empty.");

        return environmentVariables.ToDictionary(
            pair => pair.Key.Trim(),
            pair => pair.Value,
            StringComparer.Ordinal);
    }

    public static string EscapePath(string value)
        => Uri.EscapeDataString(Require(value, nameof(value)));
}
