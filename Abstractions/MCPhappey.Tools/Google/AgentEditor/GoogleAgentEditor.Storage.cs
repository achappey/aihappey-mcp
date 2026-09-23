using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MCPhappey.Tools.OneDrive.AgentPlugins;
using Microsoft.Graph.Beta;

namespace MCPhappey.Tools.Google.AgentEditor;

public static partial class GoogleAgentEditor
{
    private const string RootFolder = "google-agents";
    private const string DocumentName = "agent.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string DraftRoot(string draftName) => $"/{RootFolder}/{RequireDraftName(draftName)}";
    private static string DraftPath(string draftName) => $"{DraftRoot(draftName)}/{DocumentName}";

    private static string RequireDraftName(string? value)
    {
        var name = value?.Trim() ?? string.Empty;
        if (!DraftNamePattern().IsMatch(name))
            throw new ValidationException("draftName must start with a letter or digit and contain only letters, digits, dot, underscore, or hyphen (maximum 128 characters).");
        return name;
    }

    private static async Task<JsonObject> ReadRequiredAsync(
        GraphServiceClient graph,
        string driveId,
        string draftName,
        CancellationToken cancellationToken)
    {
        var name = RequireDraftName(draftName);
        var bytes = await graph.ReadBytesAsync(driveId, DraftPath(name), cancellationToken)
            ?? throw new ValidationException($"Google Agent draft '{name}' was not found.");
        return Agents.GoogleAgentDocument.Parse(Encoding.UTF8.GetString(bytes), $"Draft '{name}'");
    }

    private static async Task WriteAsync(
        GraphServiceClient graph,
        string driveId,
        string draftName,
        JsonObject document,
        CancellationToken cancellationToken)
    {
        Agents.GoogleAgentDocument.EnsureValid(document);
        await graph.WriteBytesAsync(
            driveId,
            DraftPath(draftName),
            Encoding.UTF8.GetBytes(document.ToJsonString(JsonOptions) + "\n"),
            cancellationToken);
    }

    private static async Task<JsonObject> MutateAsync(
        GraphServiceClient graph,
        string driveId,
        string draftName,
        Action<JsonObject> mutation,
        CancellationToken cancellationToken)
    {
        var document = await ReadRequiredAsync(graph, driveId, draftName, cancellationToken);
        mutation(document);
        await WriteAsync(graph, driveId, draftName, document, cancellationToken);
        return document;
    }

    private static JsonObject EnsureObject(JsonObject parent, string name)
    {
        if (parent[name] is JsonObject current) return current;
        var created = new JsonObject();
        parent[name] = created;
        return created;
    }

    private static JsonArray EnsureArray(JsonObject parent, string name)
    {
        if (parent[name] is JsonArray current) return current;
        var created = new JsonArray();
        parent[name] = created;
        return created;
    }

    private static JsonObject EnsureRemoteEnvironment(JsonObject document)
    {
        if (document["base_environment"] is JsonObject environment)
        {
            environment["type"] = "remote";
            return environment;
        }
        environment = new JsonObject { ["type"] = "remote" };
        document["base_environment"] = environment;
        return environment;
    }

    private static void RemoveMatches(JsonArray array, Func<JsonObject, bool> predicate)
    {
        for (var index = array.Count - 1; index >= 0; index--)
            if (array[index] is JsonObject item && predicate(item)) array.RemoveAt(index);
    }

    private static string? String(JsonObject value, string field)
        => value[field] is JsonValue node && node.TryGetValue<string>(out var text) ? text : null;

    private static JsonArray Strings(string? value)
        => new((value ?? string.Empty)
            .Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .Select(item => (JsonNode?)JsonValue.Create(item)).ToArray());

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex DraftNamePattern();
}
