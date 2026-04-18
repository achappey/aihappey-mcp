
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;

namespace MCPhappey.Common.Extensions;

public static class SamplingExtensions
{
    public static decimal? GetGatewayCost(this CreateMessageResult createMessageResult)
       => createMessageResult.Meta?.GetGatewayCost();

    public static decimal? GetGatewayCost(this JsonObject obj)
    {
        if (obj is null)
            return null;

        if (!obj.TryGetPropertyValue("gateway", out var gatewayNode))
            return null;

        if (gatewayNode is not JsonObject gateway)
            return null;

        if (!gateway.TryGetPropertyValue("cost", out var costNode))
            return null;

        if (costNode is null)
            return null;

        // number
        if (costNode is JsonValue value && value.TryGetValue<decimal>(out var dec))
            return dec;

        // string fallback
        if (decimal.TryParse(costNode.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            return parsed;

        return null;
    }


    public static string? ToText(this CreateMessageResult result) =>
         string.Join("\n\n", result.Content.OfType<TextContentBlock>().Select(a => a.Text));

    public static JsonElement ToJsonElement(this string result) =>
        JsonSerializer.SerializeToElement(result);

    public static ModelPreferences? ToModelPreferences(this string? result) => result != null ? new()
    {
        Hints = [result.ToModelHint()!]
    } : null;

    public static ModelPreferences? ToModelPreferences(this IEnumerable<string>? result) => result != null ? new()
    {
        Hints = result.Select(a => a.ToModelHint()).OfType<ModelHint>().ToList()
    } : null;

    public static ModelHint? ToModelHint(this string? result) => new() { Name = result };

    public static SamplingMessage ToSamplingMessage(this string result, Role role) =>
        result.ToTextContentBlock().ToSamplingMessage(role);

    public static SamplingMessage ToSamplingMessage(this ContentBlock contentBlock, Role role) => new()
    {
        Role = role,
        Content = [contentBlock]
    };

    public static SamplingMessage ToUserSamplingMessage(this ContentBlock contentBlock) => contentBlock.ToSamplingMessage(Role.User);

    public static SamplingMessage ToUAssistantSamplingMessage(this ContentBlock contentBlock) => contentBlock.ToSamplingMessage(Role.Assistant);

    public static SamplingMessage ToUserSamplingMessage(this string result) => result.ToSamplingMessage(Role.User);

    public static SamplingMessage ToUAssistantSamplingMessage(this string result) => result.ToSamplingMessage(Role.Assistant);

}
