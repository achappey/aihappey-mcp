using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MCPhappey.Tools.Google.Agents;

internal static class GoogleAgentDocument
{
    private static readonly HashSet<string> AgentFields =
        ["id", "description", "system_instruction", "base_agent", "base_environment", "agent_config", "tools"];

    internal static JsonObject Parse(string json, string source)
    {
        try
        {
            return JsonNode.Parse(json) as JsonObject
                ?? throw new ValidationException($"{source} must contain one JSON object.");
        }
        catch (JsonException exception)
        {
            throw new ValidationException($"{source} is not valid JSON: {exception.Message}");
        }
    }

    internal static IReadOnlyList<string> Validate(JsonObject document)
    {
        var errors = new List<string>();
        foreach (var property in document)
            if (!AgentFields.Contains(property.Key))
                errors.Add($"Unknown agent field '{property.Key}'.");

        ValidateOptionalString(document, "id", errors);
        ValidateOptionalString(document, "description", errors);
        ValidateOptionalString(document, "system_instruction", errors);
        ValidateOptionalString(document, "base_agent", errors);

        if (document["agent_config"] is JsonNode configNode)
        {
            if (configNode is not JsonObject config)
                errors.Add("agent_config must be an object.");
            else
            {
                ValidateDiscriminator(config, "type", "antigravity", "agent_config", errors);
                ValidateOptionalString(config, "model", errors, "agent_config.");
                ValidateOptionalIntegerString(config, "max_total_tokens", errors);
            }
        }

        ValidateEnvironment(document["base_environment"], errors);
        ValidateTools(document["tools"], errors);
        return errors;
    }

    internal static void EnsureValid(JsonObject document)
    {
        var errors = Validate(document);
        if (errors.Count != 0)
            throw new ValidationException(string.Join(" ", errors));
    }

    private static void ValidateEnvironment(JsonNode? node, List<string> errors)
    {
        if (node is null) return;
        if (node is JsonValue value && value.TryGetValue<string>(out var environmentId))
        {
            if (string.IsNullOrWhiteSpace(environmentId)) errors.Add("base_environment cannot be blank.");
            return;
        }
        if (node is not JsonObject environment)
        {
            errors.Add("base_environment must be an environment ID string or object.");
            return;
        }

        ValidateDiscriminator(environment, "type", "remote", "base_environment", errors);
        ValidateOptionalString(environment, "environment_id", errors, "base_environment.");

        if (environment["env"] is JsonNode envNode)
        {
            if (envNode is not JsonObject env)
                errors.Add("base_environment.env must be an object keyed by environment variable name.");
            else
                foreach (var entry in env)
                {
                    if (string.IsNullOrWhiteSpace(entry.Key)) errors.Add("Environment variable names cannot be blank.");
                    if (entry.Value is JsonValue primitive && primitive.TryGetValue<string>(out _)) continue;
                    if (entry.Value is not JsonObject variable)
                    {
                        errors.Add($"Environment variable '{entry.Key}' must be a string or object.");
                        continue;
                    }
                    ValidateOptionalString(variable, "value", errors, $"base_environment.env.{entry.Key}.");
                    ValidateOptionalString(variable, "credential", errors, $"base_environment.env.{entry.Key}.");
                    if (variable["value"] is not null && variable["credential"] is not null)
                        errors.Add($"Environment variable '{entry.Key}' cannot have both value and credential.");
                }
        }

        ValidateNetwork(environment["network"], errors);
        if (environment["sources"] is JsonNode sourcesNode)
        {
            if (sourcesNode is not JsonArray sources)
                errors.Add("base_environment.sources must be an array.");
            else
                for (var index = 0; index < sources.Count; index++)
                    ValidateSource(sources[index], index, errors);
        }
    }

    private static void ValidateNetwork(JsonNode? node, List<string> errors)
    {
        if (node is null) return;
        if (node is JsonValue value && value.TryGetValue<string>(out var mode))
        {
            if (mode != "disabled") errors.Add("base_environment.network string must be 'disabled'.");
            return;
        }
        if (node is not JsonObject network || network["allowlist"] is not JsonArray allowlist)
        {
            errors.Add("base_environment.network must be 'disabled' or an object with an allowlist array.");
            return;
        }
        for (var index = 0; index < allowlist.Count; index++)
        {
            if (allowlist[index] is not JsonObject entry)
            {
                errors.Add($"Network allowlist item {index} must be an object.");
                continue;
            }
            ValidateRequiredString(entry, "domain", errors, $"network allowlist item {index}");
            ValidateOptionalString(entry, "credential", errors, $"network allowlist item {index}.");
            if (entry["transform"] is JsonNode transform && transform is not JsonObject && transform is not JsonArray)
                errors.Add($"Network allowlist item {index}.transform must be an object or array.");
        }
    }

    private static void ValidateSource(JsonNode? node, int index, List<string> errors)
    {
        if (node is not JsonObject source)
        {
            errors.Add($"Source {index} must be an object.");
            return;
        }
        var type = ReadString(source, "type");
        if (type is not ("gcs" or "inline" or "repository"))
            errors.Add($"Source {index}.type must be gcs, inline, or repository.");
        ValidateOptionalString(source, "source", errors, $"source {index}.");
        ValidateOptionalString(source, "target", errors, $"source {index}.");
        ValidateOptionalString(source, "content", errors, $"source {index}.");
        ValidateOptionalString(source, "encoding", errors, $"source {index}.");
        if (type == "inline" && source["content"] is null) errors.Add($"Source {index} requires content when type is inline.");
        if (type != "inline" && source["source"] is null) errors.Add($"Source {index} requires source when type is {type}.");
    }

    private static void ValidateTools(JsonNode? node, List<string> errors)
    {
        if (node is null) return;
        if (node is not JsonArray tools)
        {
            errors.Add("tools must be an array.");
            return;
        }
        for (var index = 0; index < tools.Count; index++)
        {
            if (tools[index] is not JsonObject tool)
            {
                errors.Add($"Tool {index} must be an object.");
                continue;
            }
            var type = ReadString(tool, "type");
            switch (type)
            {
                case "code_execution":
                case "url_context":
                    break;
                case "google_search":
                    if (tool["search_types"] is JsonNode searchNode)
                    {
                        if (searchNode is not JsonArray searchTypes) errors.Add($"Tool {index}.search_types must be an array.");
                        else foreach (var searchType in searchTypes)
                            if (searchType?.GetValue<string>() is not ("web_search" or "image_search"))
                                errors.Add($"Tool {index} contains an invalid Google search type.");
                    }
                    break;
                case "function":
                    ValidateRequiredString(tool, "name", errors, $"function tool {index}");
                    ValidateOptionalString(tool, "description", errors, $"function tool {index}.");
                    if (tool["parameters"] is JsonNode parameters && parameters is not JsonObject)
                        errors.Add($"Function tool {index}.parameters must be a JSON Schema object.");
                    break;
                case "mcp_server":
                    ValidateRequiredString(tool, "name", errors, $"MCP tool {index}");
                    ValidateRequiredString(tool, "url", errors, $"MCP tool {index}");
                    if (ReadString(tool, "url") is string url && (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
                        errors.Add($"MCP tool {index}.url must be an absolute HTTPS URL.");
                    ValidateAllowedTools(tool["allowed_tools"], index, errors);
                    if (tool["headers"] is JsonNode headers && headers is not JsonObject)
                        errors.Add($"MCP tool {index}.headers must be an object.");
                    break;
                default:
                    errors.Add($"Tool {index}.type must be code_execution, function, google_search, mcp_server, or url_context.");
                    break;
            }
        }
    }

    private static void ValidateAllowedTools(JsonNode? node, int index, List<string> errors)
    {
        if (node is null) return;
        if (node is not JsonArray choices)
        {
            errors.Add($"MCP tool {index}.allowed_tools must be an array.");
            return;
        }
        foreach (var choiceNode in choices)
        {
            if (choiceNode is not JsonObject choice)
            {
                errors.Add($"MCP tool {index}.allowed_tools entries must be objects.");
                continue;
            }
            if (ReadString(choice, "mode") is string mode && mode is not ("auto" or "any" or "none" or "validated"))
                errors.Add($"MCP tool {index} contains an invalid allowed-tools mode.");
            if (choice["tools"] is JsonNode names && names is not JsonArray)
                errors.Add($"MCP tool {index}.allowed_tools.tools must be an array.");
        }
    }

    private static void ValidateDiscriminator(JsonObject value, string field, string expected, string path, List<string> errors)
    {
        if (ReadString(value, field) != expected) errors.Add($"{path}.{field} must be '{expected}'.");
    }

    private static void ValidateOptionalIntegerString(JsonObject value, string field, List<string> errors)
    {
        if (value[field] is null) return;
        if (ReadString(value, field) is not string text || !long.TryParse(text, out var number) || number < 1)
            errors.Add($"agent_config.{field} must be a positive integer encoded as a string.");
    }

    private static void ValidateRequiredString(JsonObject value, string field, List<string> errors, string path)
    {
        if (string.IsNullOrWhiteSpace(ReadString(value, field))) errors.Add($"{path}.{field} is required.");
    }

    private static void ValidateOptionalString(JsonObject value, string field, List<string> errors, string prefix = "")
    {
        if (value[field] is not null && ReadString(value, field) is null)
            errors.Add($"{prefix}{field} must be a string.");
    }

    private static string? ReadString(JsonObject value, string field)
        => value[field] is JsonValue node && node.TryGetValue<string>(out var text) ? text : null;
}
