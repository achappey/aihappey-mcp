using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Anthropic.Agents;

public static partial class AnthropicAgents
{
    private static async Task<JsonObject> GetAgentAsync(
        IServiceProvider serviceProvider,
        string agentId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(agentId))
            throw new ValidationException("agentId is required.");

        return await AnthropicManagedAgentsHttp.GetJsonObjectAsync(
            serviceProvider,
            $"{BaseUrl}/{Uri.EscapeDataString(agentId)}",
            cancellationToken);
    }

    private static async Task<JsonNode> UpdateAgentAsync(
        IServiceProvider serviceProvider,
        string agentId,        
        JsonObject body,
        CancellationToken cancellationToken)
        => await AnthropicManagedAgentsHttp.SendAsync(
            serviceProvider,
            HttpMethod.Post,
            $"{BaseUrl}/{Uri.EscapeDataString(agentId)}",
            body,
            cancellationToken);

    private static JsonObject CreateVersionedUpdateBody(JsonObject currentAgent)
        => new()
        {
            ["version"] = AnthropicManagedAgentsHttp.GetRequiredInt(currentAgent, "version")
        };

    private static void SetStringIfProvided(JsonObject body, string propertyName, string? value)
    {
        if (value is not null)
            body[propertyName] = value;
    }

    private static JsonObject BuildToolsetDefaultConfig(bool? enabled, string? permissionPolicy)
    {
        if (enabled is null && string.IsNullOrWhiteSpace(permissionPolicy))
            throw new ValidationException("Provide enabled and/or permissionPolicy.");

        if (!string.IsNullOrWhiteSpace(permissionPolicy))
            AnthropicManagedAgentsHttp.ValidatePermissionPolicy(permissionPolicy);

        var defaultConfig = new JsonObject();

        if (enabled.HasValue)
            defaultConfig["enabled"] = enabled.Value;

        if (!string.IsNullOrWhiteSpace(permissionPolicy))
            defaultConfig["permission_policy"] = AnthropicManagedAgentsHttp.BuildPermissionPolicy(permissionPolicy);

        return defaultConfig;
    }

    private static void ValidateSkillType(string? skillType)
    {
        if (!string.Equals(skillType, "anthropic", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(skillType, "custom", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("skillType must be 'anthropic' or 'custom'.");
        }
    }

    private static void ValidateBuiltinToolName(string? toolName)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bash",
            "edit",
            "read",
            "write",
            "glob",
            "grep",
            "web_fetch",
            "web_search"
        };

        if (string.IsNullOrWhiteSpace(toolName) || !allowed.Contains(toolName))
            throw new ValidationException("toolName must be one of: bash, edit, read, write, glob, grep, web_fetch, web_search.");
    }

    private static void ValidateMetadataKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ValidationException("key is required.");

        if (key.Length > 64)
            throw new ValidationException("key cannot exceed 64 characters.");
    }

    private static void ValidateMetadataValue(string? value)
    {
        if (value is null)
            throw new ValidationException("value is required.");

        if (value.Length > 512)
            throw new ValidationException("value cannot exceed 512 characters.");
    }

    private static bool RemoveSkill(JsonArray skills, string skillId, string? skillType)
    {
        var removed = false;

        for (var index = skills.Count - 1; index >= 0; index--)
        {
            if (skills[index] is not JsonObject skill)
                continue;

            var currentId = skill["skill_id"]?.GetValue<string>();
            var currentType = skill["type"]?.GetValue<string>();
            if (!string.Equals(currentId, skillId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.IsNullOrWhiteSpace(skillType)
                && !string.Equals(currentType, skillType, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            skills.RemoveAt(index);
            removed = true;
        }

        return removed;
    }

    private static bool RemoveMcpServer(JsonArray servers, string serverName)
    {
        var removed = false;

        for (var index = servers.Count - 1; index >= 0; index--)
        {
            if (servers[index] is not JsonObject server)
                continue;

            var currentName = server["name"]?.GetValue<string>();
            if (!string.Equals(currentName, serverName, StringComparison.OrdinalIgnoreCase))
                continue;

            servers.RemoveAt(index);
            removed = true;
        }

        return removed;
    }

    private static JsonObject EnsureAgentToolset(JsonArray tools)
    {
        var existing = FindAgentToolset(tools);
        if (existing is not null)
            return existing;

        var created = new JsonObject
        {
            ["type"] = AgentToolsetType,
            ["configs"] = new JsonArray()
        };

        tools.Add(created);
        return created;
    }

    private static JsonObject? FindAgentToolset(JsonArray tools)
        => tools
            .OfType<JsonObject>()
            .FirstOrDefault(tool => string.Equals(tool["type"]?.GetValue<string>(), AgentToolsetType, StringComparison.OrdinalIgnoreCase));

    private static JsonObject EnsureMcpToolset(JsonArray tools, string mcpServerName)
    {
        var existing = FindMcpToolset(tools, mcpServerName);
        if (existing is not null)
            return existing;

        var created = new JsonObject
        {
            ["type"] = McpToolsetType,
            ["mcp_server_name"] = mcpServerName,
            ["configs"] = new JsonArray()
        };

        tools.Add(created);
        return created;
    }

    private static JsonObject? FindMcpToolset(JsonArray tools, string mcpServerName)
        => tools
            .OfType<JsonObject>()
            .FirstOrDefault(tool =>
                string.Equals(tool["type"]?.GetValue<string>(), McpToolsetType, StringComparison.OrdinalIgnoreCase)
                && string.Equals(tool["mcp_server_name"]?.GetValue<string>(), mcpServerName, StringComparison.OrdinalIgnoreCase));

    private static JsonArray EnsureConfigsArray(JsonObject toolset)
    {
        if (toolset["configs"] is JsonArray configs)
            return configs;

        configs = new JsonArray();
        toolset["configs"] = configs;
        return configs;
    }

    private static bool RemoveBuiltinToolConfig(JsonArray configs, string toolName)
    {
        var removed = false;

        for (var index = configs.Count - 1; index >= 0; index--)
        {
            if (configs[index] is not JsonObject config)
                continue;

            if (!string.Equals(config["name"]?.GetValue<string>(), toolName, StringComparison.OrdinalIgnoreCase))
                continue;

            configs.RemoveAt(index);
            removed = true;
        }

        return removed;
    }

    private static bool RemoveMcpToolConfig(JsonArray configs, string toolName)
        => RemoveBuiltinToolConfig(configs, toolName);

    private static bool RemoveCustomTool(JsonArray tools, string toolName)
    {
        var removed = false;

        for (var index = tools.Count - 1; index >= 0; index--)
        {
            if (tools[index] is not JsonObject tool)
                continue;

            if (!string.Equals(tool["type"]?.GetValue<string>(), CustomToolType, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.Equals(tool["name"]?.GetValue<string>(), toolName, StringComparison.OrdinalIgnoreCase))
                continue;

            tools.RemoveAt(index);
            removed = true;
        }

        return removed;
    }

    private static void CleanupToolsetIfEmpty(JsonArray tools, JsonObject toolset)
    {
        var configs = toolset["configs"] as JsonArray;
        var hasConfigs = configs?.Count > 0;
        var hasDefaultConfig = toolset["default_config"] is not null;

        if (!hasConfigs && !hasDefaultConfig)
            tools.Remove(toolset);
    }

    private static async Task<JsonObject> LoadCustomToolInputSchemaAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string fileUrl,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileUrl))
            throw new ValidationException("fileUrl is required.");

        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
        var file = files.FirstOrDefault()
                   ?? throw new ValidationException($"No schema file could be downloaded from '{fileUrl}'.");

        var schemaText = file.Contents.ToString();

        JsonObject schema;
        try
        {
            schema = JsonNode.Parse(schemaText) as JsonObject
                     ?? throw new ValidationException($"The schema file at '{fileUrl}' must contain a top-level JSON object.");
        }
        catch (JsonException ex)
        {
            throw new ValidationException($"The schema file at '{fileUrl}' does not contain valid JSON: {ex.Message}");
        }

        if (schema["type"] is null)
        {
            schema["type"] = "object";
        }
        else if (!string.Equals(schema["type"]?.GetValue<string>(), "object", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("The custom tool input schema must declare a top-level 'type' of 'object'.");
        }

        try
        {
            _ = await NJsonSchema.JsonSchema.FromJsonAsync(schema.ToJsonString(), cancellationToken);
        }
        catch (Exception ex)
        {
            throw new ValidationException($"The schema file at '{fileUrl}' is not a valid JSON Schema: {ex.Message}");
        }

        return schema;
    }
}
