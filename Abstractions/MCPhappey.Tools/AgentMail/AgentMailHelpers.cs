using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using MCPhappey.Common.Extensions;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.AgentMail;

internal static class AgentMailHelpers
{
    public static Dictionary<string, string>? GetHostHeaders(
        Dictionary<string, Dictionary<string, string>>? headers,
        params string[] hosts)
    {
        foreach (var host in hosts)
        {
            var match = headers?
                .FirstOrDefault(h => h.Key.Equals(host, StringComparison.OrdinalIgnoreCase))
                .Value;

            if (match is { Count: > 0 })
                return new Dictionary<string, string>(match, StringComparer.OrdinalIgnoreCase);
        }

        return null;
    }

    public static string EscapePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ValidationException("Path value is required.");

        return Uri.EscapeDataString(value.Trim());
    }

    public static void Require(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ValidationException($"{name} is required.");
    }

    public static List<string> ParseDelimited(string? input)
        => string.IsNullOrWhiteSpace(input)
            ? []
            : input
                .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();

    public static JsonArray? ToJsonArray(string? input)
    {
        var values = ParseDelimited(input);
        if (values.Count == 0)
            return null;

        var array = new JsonArray();
        foreach (var value in values)
            array.Add(value);

        return array;
    }

    public static JsonNode? ToAddressNode(string? input)
    {
        var values = ParseDelimited(input);
        return values.Count switch
        {
            0 => null,
            1 => values[0],
            _ => ToJsonArray(input)
        };
    }

    public static JsonArray? ParseJsonArray(string? json, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        var parsed = JsonNode.Parse(json) as JsonArray;
        return parsed ?? throw new ValidationException($"{parameterName} must be a valid JSON array string.");
    }

    public static JsonObject? ParseJsonObject(string? json, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        var parsed = JsonNode.Parse(json) as JsonObject;
        return parsed ?? throw new ValidationException($"{parameterName} must be a valid JSON object string.");
    }

    public static JsonObject BuildSendMessageBody(
        string? labels,
        string? replyTo,
        string? to,
        string? cc,
        string? bcc,
        string? subject,
        string? text,
        string? html,
        string? attachmentsJson,
        string? headersJson)
    {
        var body = new JsonObject();
        AddArray(body, "labels", labels);
        AddAddress(body, "reply_to", replyTo);
        AddAddress(body, "to", to);
        AddAddress(body, "cc", cc);
        AddAddress(body, "bcc", bcc);
        AddString(body, "subject", subject);
        AddString(body, "text", text);
        AddString(body, "html", html);
        AddJsonArray(body, "attachments", attachmentsJson, nameof(attachmentsJson));
        AddJsonObject(body, "headers", headersJson, nameof(headersJson));
        return body;
    }

    public static JsonObject BuildReplyMessageBody(
        string? labels,
        string? replyTo,
        string? to,
        string? cc,
        string? bcc,
        bool? replyAll,
        string? text,
        string? html,
        string? attachmentsJson,
        string? headersJson)
    {
        var body = BuildSendMessageBody(labels, replyTo, to, cc, bcc, null, text, html, attachmentsJson, headersJson);
        if (replyAll.HasValue) body["reply_all"] = replyAll.Value;
        return body;
    }

    public static JsonObject BuildReplyAllMessageBody(
        string? labels,
        string? replyTo,
        string? text,
        string? html,
        string? attachmentsJson,
        string? headersJson)
    {
        var body = new JsonObject();
        AddArray(body, "labels", labels);
        AddAddress(body, "reply_to", replyTo);
        AddString(body, "text", text);
        AddString(body, "html", html);
        AddJsonArray(body, "attachments", attachmentsJson, nameof(attachmentsJson));
        AddJsonObject(body, "headers", headersJson, nameof(headersJson));
        return body;
    }

    public static JsonObject BuildMessageLabelsBody(string? addLabels, string? removeLabels)
    {
        var body = new JsonObject();
        AddArray(body, "add_labels", addLabels);
        AddArray(body, "remove_labels", removeLabels);
        return body;
    }

    public static JsonObject BuildDraftBody(
        string? labels,
        string? replyTo,
        string? to,
        string? cc,
        string? bcc,
        string? subject,
        string? text,
        string? html,
        string? inReplyTo,
        string? sendAt,
        string? clientId,
        bool includeCreateOnlyFields)
    {
        var body = new JsonObject();
        if (includeCreateOnlyFields) AddArray(body, "labels", labels);
        AddArray(body, "reply_to", replyTo);
        AddArray(body, "to", to);
        AddArray(body, "cc", cc);
        AddArray(body, "bcc", bcc);
        AddString(body, "subject", subject);
        AddString(body, "text", text);
        AddString(body, "html", html);
        if (includeCreateOnlyFields) AddString(body, "in_reply_to", inReplyTo);
        AddString(body, "send_at", sendAt);
        if (includeCreateOnlyFields) AddString(body, "client_id", clientId);
        return body;
    }

    public static async Task<CallToolResult> ToStructuredResultAsync(
        this AgentMailResponse response,
        RequestContext<CallToolRequestParams> requestContext,
        string operation)
        => new()
        {
            Meta = await requestContext.GetToolMeta(),
            StructuredContent = response.StructuredOrStatus(operation).ToJsonElement(),
            Content = [$"AgentMail {operation} completed.".ToTextContentBlock()]
        };

    public static void EnsureBody(JsonObject body, string message)
    {
        if (body.Count == 0)
            throw new ValidationException(message);
    }

    private static void AddString(JsonObject body, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            body[name] = value.Trim();
    }

    private static void AddArray(JsonObject body, string name, string? value)
    {
        if (ToJsonArray(value) is { } array)
            body[name] = array;
    }

    private static void AddAddress(JsonObject body, string name, string? value)
    {
        if (ToAddressNode(value) is { } address)
            body[name] = address;
    }

    private static void AddJsonArray(JsonObject body, string name, string? json, string parameterName)
    {
        if (ParseJsonArray(json, parameterName) is { } array)
            body[name] = array;
    }

    private static void AddJsonObject(JsonObject body, string name, string? json, string parameterName)
    {
        if (ParseJsonObject(json, parameterName) is { } obj)
            body[name] = obj;
    }
}

[Description("Please confirm deletion of the AgentMail inbox id: {0}")]
internal sealed class ConfirmDeleteAgentMailInbox : IHasName
{
    public string Name { get; set; } = string.Empty;
}

[Description("Please confirm deletion of the AgentMail pod-scoped inbox id: {0}")]
internal sealed class ConfirmDeleteAgentMailPodInbox : IHasName
{
    public string Name { get; set; } = string.Empty;
}

[Description("Please confirm deletion of the AgentMail thread id: {0}")]
internal sealed class ConfirmDeleteAgentMailThread : IHasName
{
    public string Name { get; set; } = string.Empty;
}

[Description("Please confirm deletion of the AgentMail draft id: {0}")]
internal sealed class ConfirmDeleteAgentMailDraft : IHasName
{
    public string Name { get; set; } = string.Empty;
}

[Description("Please confirm deletion of the AgentMail API key id: {0}")]
internal sealed class ConfirmDeleteAgentMailApiKey : IHasName
{
    public string Name { get; set; } = string.Empty;
}

[Description("Please confirm deletion of the AgentMail domain id: {0}")]
internal sealed class ConfirmDeleteAgentMailDomain : IHasName
{
    public string Name { get; set; } = string.Empty;
}

[Description("Please confirm deletion of the AgentMail pod-scoped domain id: {0}")]
internal sealed class ConfirmDeleteAgentMailPodDomain : IHasName
{
    public string Name { get; set; } = string.Empty;
}
