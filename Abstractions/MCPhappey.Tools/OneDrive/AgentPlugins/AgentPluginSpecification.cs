using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MCPhappey.Tools.OneDrive.AgentPlugins;

internal sealed record AgentPluginDiagnostic(
    string Severity,
    string Boundary,
    string Code,
    string Message,
    string? Path = null,
    string? Entry = null);

internal sealed record AgentPluginValidationResult<T>(
    T? Value,
    IReadOnlyList<AgentPluginDiagnostic> Diagnostics)
    where T : class;

internal static partial class AgentPluginSpecification
{
    public const string PluginSchema = "https://agent-plugins.org/schemas/1.0.0/plugin.schema.json";
    public const string McpSchema = "https://agent-plugins.org/schemas/1.0.0/mcp.schema.json";
    public const string PluginManifestName = "plugin.json";
    public const string McpManifestName = "mcp.json";
    public const string SkillsFolderName = "skills";

    private static readonly HashSet<string> ManifestFields =
    [
        "$schema", "name", "version", "description", "author", "homepage",
        "repository", "license", "keywords", "extensions"
    ];

    private static readonly HashSet<string> McpFields = ["$schema", "mcpServers"];
    private static readonly HashSet<string> StdioFields = ["type", "command", "args", "env", "cwd"];
    private static readonly HashSet<string> HttpFields = ["type", "url", "headers"];
    private static readonly HashSet<string> AuthorFields = ["name", "email", "url"];

    public static AgentPluginValidationResult<JsonObject> ValidateManifest(
        string json,
        string? expectedDirectoryName = null)
    {
        var diagnostics = new List<AgentPluginDiagnostic>();
        JsonObject root;
        try
        {
            root = JsonNode.Parse(json) as JsonObject
                ?? throw new JsonException("plugin.json must contain a JSON object.");
        }
        catch (JsonException exception)
        {
            diagnostics.Add(Error("manifest", "manifest-invalid-json", exception.Message, PluginManifestName));
            return new(null, diagnostics);
        }

        foreach (var field in root.Select(item => item.Key).Where(field => !ManifestFields.Contains(field)))
            diagnostics.Add(Warning("manifest", "manifest-unknown-field",
                $"Unknown plugin.json field '{field}' was ignored.", PluginManifestName));

        if (ReadString(root, "$schema") != PluginSchema)
            diagnostics.Add(Error("manifest", "manifest-unsupported-schema",
                "plugin.json must target the Agent Plugins 1.0.0 schema.", PluginManifestName));

        var name = ReadString(root, "name");
        if (!IsValidPluginName(name))
            diagnostics.Add(Error("manifest", "manifest-invalid-name",
                "Plugin name must be 1-64 lowercase letters, digits, hyphens, or periods, start and end alphanumeric, and contain no '--' or '..'.",
                PluginManifestName));
        else if (!string.IsNullOrWhiteSpace(expectedDirectoryName)
                 && !string.Equals(name, expectedDirectoryName, StringComparison.Ordinal))
            diagnostics.Add(Error("manifest", "manifest-name-directory-mismatch",
                $"Manifest name '{name}' must match plugin folder '{expectedDirectoryName}'.", PluginManifestName));

        foreach (var field in new[] { "version", "description", "homepage", "repository", "license" })
        {
            if (root.ContainsKey(field) && root[field] is not JsonValue value
                || root[field] is JsonValue typed && !typed.TryGetValue<string>(out _))
                diagnostics.Add(Error("manifest", $"manifest-invalid-{field}",
                    $"plugin.json field '{field}' must be a string.", PluginManifestName));
        }

        if (root.TryGetPropertyValue("author", out var authorNode))
        {
            if (authorNode is not JsonObject author
                || author.Any(item => !AuthorFields.Contains(item.Key)
                    || item.Value is not JsonValue value
                    || !value.TryGetValue<string>(out _)))
                diagnostics.Add(Error("manifest", "manifest-invalid-author",
                    "author may contain only string name, email, and url fields.", PluginManifestName));
        }

        if (root.TryGetPropertyValue("keywords", out var keywordsNode)
            && (keywordsNode is not JsonArray keywords
                || keywords.Any(item => item is not JsonValue value || !value.TryGetValue<string>(out _))))
            diagnostics.Add(Error("manifest", "manifest-invalid-keywords",
                "keywords must be an array of strings.", PluginManifestName));

        var preserveExtensions = false;
        if (root.TryGetPropertyValue("extensions", out var extensionsNode))
        {
            if (extensionsNode is not JsonObject extensions)
            {
                diagnostics.Add(Warning("extension", "manifest-invalid-extensions",
                    "The non-object extensions field was ignored.", PluginManifestName));
            }
            else
            {
                preserveExtensions = true;
                foreach (var extension in extensions)
                {
                    if (!IsValidExtensionNamespace(extension.Key) || extension.Value is not JsonObject)
                        diagnostics.Add(Error("extension", "manifest-invalid-extension-entry",
                            $"Extension '{extension.Key}' must use a reverse-domain namespace and contain an object.",
                            PluginManifestName, extension.Key));
                }
            }
        }

        if (diagnostics.Any(item => item.Severity == "error"))
            return new(null, diagnostics);

        var normalized = new JsonObject
        {
            ["$schema"] = PluginSchema,
            ["name"] = name
        };
        foreach (var field in new[] { "version", "description", "author", "homepage", "repository", "license", "keywords" })
        {
            if (root[field] is { } value)
                normalized[field] = value.DeepClone();
        }
        if (preserveExtensions)
            normalized["extensions"] = root["extensions"]!.DeepClone();

        return new(normalized, diagnostics);
    }

    public static AgentPluginValidationResult<JsonObject> ValidateMcpDocument(string json)
    {
        var diagnostics = new List<AgentPluginDiagnostic>();
        JsonObject root;
        try
        {
            root = JsonNode.Parse(json) as JsonObject
                ?? throw new JsonException("mcp.json must contain a JSON object.");
        }
        catch (JsonException exception)
        {
            diagnostics.Add(Error("mcp", "mcp-invalid-json", exception.Message, McpManifestName));
            return new(null, diagnostics);
        }

        if (root.Any(item => !McpFields.Contains(item.Key))
            || ReadString(root, "$schema") != McpSchema
            || root["mcpServers"] is not JsonObject servers)
        {
            diagnostics.Add(Error("mcp", "mcp-invalid-document",
                "mcp.json must contain only the Agent Plugins 1.0.0 $schema and an mcpServers object.",
                McpManifestName));
            return new(null, diagnostics);
        }

        var validServers = new JsonObject();
        foreach (var server in servers)
        {
            var validation = ValidateMcpServer(server.Key, server.Value);
            diagnostics.AddRange(validation.Diagnostics);
            if (validation.Value is not null)
                validServers[server.Key] = validation.Value;
        }

        return new(new JsonObject
        {
            ["$schema"] = McpSchema,
            ["mcpServers"] = validServers
        }, diagnostics);
    }

    public static AgentPluginValidationResult<JsonObject> ValidateMcpServer(string name, JsonNode? node)
    {
        if (string.IsNullOrWhiteSpace(name))
            return InvalidServer(name, "server-invalid-name", "MCP server names cannot be empty.");
        if (node is not JsonObject server || ReadString(server, "type") is not { } type)
            return InvalidServer(name, "server-invalid", $"MCP server '{name}' must be an object with a type.");

        return type switch
        {
            "stdio" => ValidateStdioServer(name, server),
            "streamable-http" or "sse" => ValidateHttpServer(name, server, type),
            _ => InvalidServer(name, "server-unknown-transport",
                $"MCP server '{name}' declares unknown transport '{type}'.")
        };
    }

    public static bool IsValidPluginName(string? value)
        => value is { Length: >= 1 and <= 64 } && PluginNameRegex().IsMatch(value);

    public static bool IsValidExtensionNamespace(string value)
        => value.Length <= 253 && ExtensionNamespaceRegex().IsMatch(value);

    public static string NormalizePackagePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains('\\') || value.Contains('\0') || value.StartsWith('/'))
            throw new ValidationException("Package path must be a non-empty forward-slash relative path.");

        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || parts.Any(part => part is "." or ".."))
            throw new ValidationException("Package path must remain within the plugin root.");
        return string.Join('/', parts);
    }

    public static bool IsProtectedPackagePath(string path)
        => path is PluginManifestName or McpManifestName
           || path.Equals(SkillsFolderName, StringComparison.OrdinalIgnoreCase)
           || path.StartsWith(SkillsFolderName + "/", StringComparison.OrdinalIgnoreCase);

    private static AgentPluginValidationResult<JsonObject> ValidateStdioServer(string name, JsonObject server)
    {
        var command = ReadString(server, "command");
        if (server.Any(item => !StdioFields.Contains(item.Key)) || string.IsNullOrWhiteSpace(command))
            return InvalidServer(name, "server-invalid-stdio",
                $"stdio server '{name}' has invalid or unknown fields.");

        if (command.Contains('\\')
            || command.Contains('/') && !IsSafePluginRelativePath(command))
            return InvalidServer(name, "server-unsafe-command",
                $"stdio server '{name}' command must be a bare executable name or a safe './' package path.");

        if (server.TryGetPropertyValue("args", out var argsNode)
            && (argsNode is not JsonArray args
                || args.Any(item => item is not JsonValue value || !value.TryGetValue<string>(out _))))
            return InvalidServer(name, "server-invalid-args", $"stdio server '{name}' args must be strings.");

        if (server.TryGetPropertyValue("env", out var envNode))
        {
            if (envNode is not JsonObject env || env.Any(item =>
                    item.Key is "PLUGIN_ROOT" or "PLUGIN_DATA"
                    || item.Value is not JsonValue value
                    || !value.TryGetValue<string>(out _)))
                return InvalidServer(name, "server-invalid-env",
                    $"stdio server '{name}' env must contain string values and cannot override PLUGIN_ROOT or PLUGIN_DATA.");
        }

        if (server.TryGetPropertyValue("cwd", out var cwdNode)
            && (cwdNode is not JsonValue cwdValue
                || !cwdValue.TryGetValue<string>(out var cwd)
                || !IsValidCwd(cwd)))
            return InvalidServer(name, "server-invalid-cwd", $"stdio server '{name}' cwd is unsafe.");

        return new((JsonObject)server.DeepClone(), []);
    }

    private static AgentPluginValidationResult<JsonObject> ValidateHttpServer(
        string name,
        JsonObject server,
        string type)
    {
        if (server.Any(item => !HttpFields.Contains(item.Key))
            || !TryValidateRemoteUrl(ReadString(server, "url"), out _)
            || !ValidateHeaders(server["headers"]))
            return InvalidServer(name, "server-invalid-http",
                $"Remote MCP server '{name}' has invalid or unknown fields, URL, or headers.");

        var diagnostics = type == "sse"
            ? new[] { Info("server", "server-legacy-transport", $"MCP server '{name}' uses deprecated HTTP+SSE.", McpManifestName, name) }
            : [];
        return new((JsonObject)server.DeepClone(), diagnostics);
    }

    private static bool ValidateHeaders(JsonNode? node)
    {
        if (node is null) return true;
        if (node is not JsonObject headers) return false;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers)
        {
            if (!HeaderNameRegex().IsMatch(header.Key)
                || !seen.Add(header.Key)
                || header.Value is not JsonValue value
                || !value.TryGetValue<string>(out var headerValue)
                || headerValue.Contains('\r')
                || headerValue.Contains('\n'))
                return false;
        }
        return true;
    }

    private static bool TryValidateRemoteUrl(string? value, out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var candidate)
            || candidate.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(candidate.UserInfo)
            || !string.IsNullOrEmpty(candidate.Fragment))
            return false;

        var loopback = candidate.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || IPAddress.TryParse(candidate.Host, out var address) && IPAddress.IsLoopback(address);
        if (candidate.Scheme == "http" && !loopback) return false;
        uri = candidate;
        return true;
    }

    private static bool IsSafePluginRelativePath(string value)
        => value.StartsWith("./", StringComparison.Ordinal)
           && !value.Contains('\\')
           && !value.Contains('\0')
           && !value.Split('/').Contains("..");

    private static bool IsValidCwd(string value)
    {
        if (IsSafePluginRelativePath(value)) return true;
        foreach (var root in new[] { "${PLUGIN_ROOT}", "${PLUGIN_DATA}" })
        {
            if (value == root) return true;
            if (value.StartsWith(root + "/", StringComparison.Ordinal)
                && !value[(root.Length + 1)..].Split('/').Contains(".."))
                return true;
        }
        return false;
    }

    private static string? ReadString(JsonObject value, string property)
        => value[property] is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text) ? text : null;

    private static AgentPluginValidationResult<JsonObject> InvalidServer(string name, string code, string message)
        => new(null, [Error("server", code, message, McpManifestName, name)]);

    private static AgentPluginDiagnostic Error(string boundary, string code, string message, string? path = null, string? entry = null)
        => new("error", boundary, code, message, path, entry);

    private static AgentPluginDiagnostic Warning(string boundary, string code, string message, string? path = null, string? entry = null)
        => new("warning", boundary, code, message, path, entry);

    private static AgentPluginDiagnostic Info(string boundary, string code, string message, string? path = null, string? entry = null)
        => new("info", boundary, code, message, path, entry);

    [GeneratedRegex("^(?!.*(?:--|\\.\\.))[a-z0-9](?:[a-z0-9.-]*[a-z0-9])?$")]
    private static partial Regex PluginNameRegex();

    [GeneratedRegex("^(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\\.)+[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$")]
    private static partial Regex ExtensionNamespaceRegex();

    [GeneratedRegex("^[!#$%&'*+.^_`|~0-9A-Za-z-]+$")]
    private static partial Regex HeaderNameRegex();
}
