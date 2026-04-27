namespace MCPhappey.Tools.Anthropic;

public static class AnthropicHeaders
{
    public const string ApiHost = "api.anthropic.com";
    public const string ApiBaseUrl = "https://api.anthropic.com";
    public const string ApiKeyHeader = "x-api-key";
    public const string AnthropicVersionHeader = "anthropic-version";
    public const string AnthropicBetaHeader = "anthropic-beta";
    public const string AnthropicVersion = "2023-06-01";
    public const string ManagedAgentsBetaFeature = "managed-agents-2026-04-01";

    public static readonly string[] ManagedAgentsBetaFeatures =
    [
        ManagedAgentsBetaFeature
    ];

    public static void EnsureManagedAgentsHeaders(Dictionary<string, Dictionary<string, string>>? domainHeaders)
    {
        if (domainHeaders is null)
            return;

        var anthropicHeaders = domainHeaders
            .FirstOrDefault(host => string.Equals(host.Key, ApiHost, StringComparison.OrdinalIgnoreCase))
            .Value;

        if (anthropicHeaders is null)
            return;

        SetHeaderValue(anthropicHeaders, AnthropicVersionHeader, AnthropicVersion);
        SetHeaderValue(
            anthropicHeaders,
            AnthropicBetaHeader,
            BuildManagedAgentsBetaHeader(GetHeaderValue(anthropicHeaders, AnthropicBetaHeader)));
    }

    public static string BuildManagedAgentsBetaHeader(string? anthropicBetaCsv)
    {
        var values = new HashSet<string>(ManagedAgentsBetaFeatures, StringComparer.OrdinalIgnoreCase);
        foreach (var value in ParseDelimited(anthropicBetaCsv))
            values.Add(value);

        return string.Join(',', values);
    }

    private static string? GetHeaderValue(Dictionary<string, string> headers, string headerName)
        => headers
            .FirstOrDefault(header => string.Equals(header.Key, headerName, StringComparison.OrdinalIgnoreCase))
            .Value;

    private static void SetHeaderValue(Dictionary<string, string> headers, string headerName, string value)
    {
        var existingHeaderName = headers.Keys
            .FirstOrDefault(key => string.Equals(key, headerName, StringComparison.OrdinalIgnoreCase));

        headers[existingHeaderName ?? headerName] = value;
    }

    private static IEnumerable<string> ParseDelimited(string? value)
    {
        if (value is null)
            return [];

        return value
            .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
