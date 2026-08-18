using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.Extensions;
using MCPhappey.Tools.Memory.OneDrive;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.OneDrive.AgentPlugins;

public static partial class OneDriveAgentPluginEditor
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [Description("List Agent Plugin packages stored under /plugins in the user's default OneDrive.")]
    [McpServerTool(Title = "List OneDrive Agent Plugins", Name = "onedrive_plugin_editor_list",
        ReadOnly = true, Idempotent = true, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> List(
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async graph =>
        await context.WithStructuredContent(async () =>
        {
            var drive = await graph.GetDefaultDriveAsync(cancellationToken)
                ?? throw new InvalidOperationException("Could not resolve default OneDrive.");
            var folders = await graph.ListPluginFoldersAsync(drive.Id!, cancellationToken);
            var plugins = new List<object>();
            foreach (var folder in folders)
            {
                var name = folder.Name!;
                var bytes = await graph.ReadBytesAsync(drive.Id!,
                    OneDriveAgentPluginStorage.PluginPath(name, AgentPluginSpecification.PluginManifestName), cancellationToken);
                var validation = bytes is null
                    ? new AgentPluginValidationResult<JsonObject>(null,
                    [new("error", "manifest", "manifest-missing", "plugin.json is required.", "plugin.json")])
                    : AgentPluginSpecification.ValidateManifest(Encoding.UTF8.GetString(bytes), name);
                plugins.Add(new
                {
                    name,
                    valid = validation.Value is not null,
                    description = ReadJsonString(validation.Value, "description"),
                    version = ReadJsonString(validation.Value, "version"),
                    webUrl = folder.WebUrl,
                    diagnostics = validation.Diagnostics
                });
            }
            return new { root = "/plugins", items = plugins };
        })));

    [Description("Inspect an Agent Plugin manifest, MCP configuration, embedded skills, package files, and conformance diagnostics.")]
    [McpServerTool(Title = "Inspect OneDrive Agent Plugin", Name = "onedrive_plugin_editor_inspect",
        ReadOnly = true, Idempotent = true, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> Inspect(
        [Description("Agent Plugin name and folder under /plugins.")] string pluginName,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async graph =>
        await context.WithStructuredContent(async () =>
        {
            var name = RequirePluginName(pluginName);
            var drive = await graph.GetDefaultDriveAsync(cancellationToken)
                ?? throw new InvalidOperationException("Could not resolve default OneDrive.");
            return await InspectPluginAsync(graph, drive.Id!, name, cancellationToken);
        })));

    [Description("Read a UTF-8 text file from an Agent Plugin package, including plugin.json, mcp.json, and embedded skill files.")]
    [McpServerTool(Title = "Read OneDrive Agent Plugin File", Name = "onedrive_plugin_editor_read_file",
        ReadOnly = true, Idempotent = true, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> ReadFile(
        [Description("Agent Plugin name.")] string pluginName,
        [Description("Package-relative file path.")] string relativePath,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async graph =>
        {
            var name = RequirePluginName(pluginName);
            var path = AgentPluginSpecification.NormalizePackagePath(relativePath);
            var drive = await graph.GetDefaultDriveAsync(cancellationToken)
                ?? throw new InvalidOperationException("Could not resolve default OneDrive.");
            var bytes = await graph.ReadBytesAsync(drive.Id!, OneDriveAgentPluginStorage.PluginPath(name, path), cancellationToken)
                ?? throw new ValidationException($"File '{path}' was not found in Agent Plugin '{name}'.");
            if (bytes.AsSpan().Contains((byte)0))
                throw new ValidationException("The requested file appears to be binary. Use inspect to retrieve its metadata and OneDrive URL.");
            return Encoding.UTF8.GetString(bytes).ToTextContentBlock().ToCallToolResult();
        }));

    [Description("Create a minimal Agent Plugins v1.0.0 package under /plugins. The name is immutable after creation.")]
    [McpServerTool(Title = "Create OneDrive Agent Plugin", Name = "onedrive_plugin_editor_create",
        ReadOnly = false, Idempotent = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> Create(
        string? name,
        string? version,
        string? description,
        string? authorName,
        string? authorEmail,
        string? authorUrl,
        string? homepage,
        string? repository,
        string? license,
        string? keywords,
        string? extensionsJson,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async graph =>
        {
            var (typed, notAccepted, _) = await context.Server.TryElicit(new PluginManifestInput
            {
                Name = name ?? string.Empty,
                Version = version,
                Description = description,
                AuthorName = authorName,
                AuthorEmail = authorEmail,
                AuthorUrl = authorUrl,
                Homepage = homepage,
                Repository = repository,
                License = license,
                Keywords = keywords,
                ExtensionsJson = extensionsJson
            }, cancellationToken);
            if (notAccepted is not null) return notAccepted;
            ArgumentNullException.ThrowIfNull(typed);

            var manifest = BuildManifest(typed);
            var validation = AgentPluginSpecification.ValidateManifest(manifest.ToJsonString(), typed.Name);
            if (validation.Value is null)
                throw new ValidationException(string.Join(" ", validation.Diagnostics.Select(item => item.Message)));

            var drive = await graph.GetDefaultDriveAsync(cancellationToken)
                ?? throw new InvalidOperationException("Could not resolve default OneDrive.");
            await graph.EnsurePluginFolderAsync(drive.Id!, OneDriveAgentPluginStorage.RootFolderName, cancellationToken);
            if (await graph.GetItemByPathOrNullAsync(drive.Id!, OneDriveAgentPluginStorage.PluginRoot(typed.Name), cancellationToken) is not null)
                throw new ValidationException($"Agent Plugin '{typed.Name}' already exists.");
            await graph.EnsurePluginFolderAsync(drive.Id!, OneDriveAgentPluginStorage.PluginRoot(typed.Name), cancellationToken);
            await WriteJsonAsync(graph, drive.Id!, typed.Name, AgentPluginSpecification.PluginManifestName, validation.Value, cancellationToken);
            return Mutation("created", typed.Name, [AgentPluginSpecification.PluginManifestName]).ToJsonContentBlock(PluginUri(typed.Name)).ToCallToolResult();
        }));

    [Description("Replace optional plugin.json metadata while preserving the immutable Agent Plugin name and canonical schema.")]
    [McpServerTool(Title = "Update OneDrive Agent Plugin Manifest", Name = "onedrive_plugin_editor_update_manifest",
        ReadOnly = false, Idempotent = true, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> UpdateManifest(
        string pluginName,
        string? version,
        string? description,
        string? authorName,
        string? authorEmail,
        string? authorUrl,
        string? homepage,
        string? repository,
        string? license,
        string? keywords,
        string? extensionsJson,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async graph =>
        {
            var name = RequirePluginName(pluginName);
            var drive = await graph.GetDefaultDriveAsync(cancellationToken)
                ?? throw new InvalidOperationException("Could not resolve default OneDrive.");
            var current = await ReadRequiredManifestAsync(graph, drive.Id!, name, cancellationToken);
            var author = current["author"] as JsonObject;
            var (typed, notAccepted, _) = await context.Server.TryElicit(new PluginManifestInput
            {
                Name = name,
                Version = version ?? ReadJsonString(current, "version"),
                Description = description ?? ReadJsonString(current, "description"),
                AuthorName = authorName ?? ReadJsonString(author, "name"),
                AuthorEmail = authorEmail ?? ReadJsonString(author, "email"),
                AuthorUrl = authorUrl ?? ReadJsonString(author, "url"),
                Homepage = homepage ?? ReadJsonString(current, "homepage"),
                Repository = repository ?? ReadJsonString(current, "repository"),
                License = license ?? ReadJsonString(current, "license"),
                Keywords = keywords ?? RenderKeywords(current["keywords"] as JsonArray),
                ExtensionsJson = extensionsJson ?? current["extensions"]?.ToJsonString(JsonOptions)
            }, cancellationToken);
            if (notAccepted is not null) return notAccepted;
            ArgumentNullException.ThrowIfNull(typed);
            typed.Name = name;
            var manifest = BuildManifest(typed);
            var validation = AgentPluginSpecification.ValidateManifest(manifest.ToJsonString(), name);
            if (validation.Value is null)
                throw new ValidationException(string.Join(" ", validation.Diagnostics.Select(item => item.Message)));
            await WriteJsonAsync(graph, drive.Id!, name, AgentPluginSpecification.PluginManifestName, validation.Value, cancellationToken);
            return Mutation("updated manifest for", name, [AgentPluginSpecification.PluginManifestName]).ToJsonContentBlock(PluginUri(name)).ToCallToolResult();
        }));

    [Description("Create or replace a UTF-8 package file outside protected root manifests and skills/.")]
    [McpServerTool(Title = "Upsert OneDrive Agent Plugin Text File", Name = "onedrive_plugin_editor_upsert_file",
        ReadOnly = false, Idempotent = true, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> UpsertFile(
        string pluginName,
        string? relativePath,
        string? content,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async graph =>
        {
            var name = RequirePluginName(pluginName);
            var (typed, notAccepted, _) = await context.Server.TryElicit(new PluginTextFileInput
            {
                RelativePath = relativePath ?? string.Empty,
                Content = content ?? string.Empty
            }, cancellationToken);
            if (notAccepted is not null) return notAccepted;
            ArgumentNullException.ThrowIfNull(typed);
            var path = RequireEditableFilePath(typed.RelativePath);
            var drive = await graph.GetDefaultDriveAsync(cancellationToken)
                ?? throw new InvalidOperationException("Could not resolve default OneDrive.");
            await EnsurePluginExistsAsync(graph, drive.Id!, name, cancellationToken);
            await graph.WriteBytesAsync(drive.Id!, OneDriveAgentPluginStorage.PluginPath(name, path), Encoding.UTF8.GetBytes(typed.Content), cancellationToken);
            return Mutation("updated", name, [path]).ToJsonContentBlock(PluginUri(name, path)).ToCallToolResult();
        }));

    [Description("Import a package file from an HTTPS, OneDrive, or SharePoint URL outside protected manifests and skills/.")]
    [McpServerTool(Title = "Import OneDrive Agent Plugin File", Name = "onedrive_plugin_editor_import_file",
        ReadOnly = false, Idempotent = true, OpenWorld = true, Destructive = false)]
    public static async Task<CallToolResult?> ImportFile(
        string pluginName,
        string? sourceUrl,
        string? relativePath,
        IServiceProvider services,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async graph =>
        {
            var name = RequirePluginName(pluginName);
            var (typed, notAccepted, _) = await context.Server.TryElicit(new PluginFileImportInput
            {
                SourceUrl = sourceUrl ?? string.Empty,
                RelativePath = relativePath ?? string.Empty
            }, cancellationToken);
            if (notAccepted is not null) return notAccepted;
            ArgumentNullException.ThrowIfNull(typed);
            var path = RequireEditableFilePath(typed.RelativePath);
            var downloaded = (await services.GetRequiredService<DownloadService>()
                .DownloadContentAsync(services, context.Server, typed.SourceUrl, cancellationToken)).FirstOrDefault()
                ?? throw new ValidationException("Could not download sourceUrl.");
            var drive = await graph.GetDefaultDriveAsync(cancellationToken)
                ?? throw new InvalidOperationException("Could not resolve default OneDrive.");
            await EnsurePluginExistsAsync(graph, drive.Id!, name, cancellationToken);
            await graph.WriteBytesAsync(drive.Id!, OneDriveAgentPluginStorage.PluginPath(name, path), downloaded.Contents.ToArray(), cancellationToken);
            return Mutation("imported file into", name, [path]).ToJsonContentBlock(PluginUri(name, path)).ToCallToolResult();
        }));

    [Description("Delete one package file outside protected root manifests and skills/.")]
    [McpServerTool(Title = "Delete OneDrive Agent Plugin File", Name = "onedrive_plugin_editor_delete_file",
        ReadOnly = false, Idempotent = true, OpenWorld = false, Destructive = true)]
    public static async Task<CallToolResult?> DeleteFile(
        string pluginName,
        string relativePath,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async graph =>
        {
            var name = RequirePluginName(pluginName);
            var path = RequireEditableFilePath(relativePath);
            var drive = await graph.GetDefaultDriveAsync(cancellationToken)
                ?? throw new InvalidOperationException("Could not resolve default OneDrive.");
            await EnsurePluginExistsAsync(graph, drive.Id!, name, cancellationToken);
            await graph.DeleteItemIfExistsAsync(drive.Id!, OneDriveAgentPluginStorage.PluginPath(name, path), cancellationToken);
            return Mutation("deleted file from", name, [path]).ToJsonContentBlock(PluginUri(name)).ToCallToolResult();
        }));

    [Description("Validate and create or replace one MCP server entry in root mcp.json while preserving independent entries.")]
    [McpServerTool(Title = "Upsert OneDrive Agent Plugin MCP Server", Name = "onedrive_plugin_editor_upsert_mcp_server",
        ReadOnly = false, Idempotent = true, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> UpsertMcpServer(
        string pluginName,
        string? serverName,
        string? serverJson,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async graph =>
        {
            var name = RequirePluginName(pluginName);
            var (typed, notAccepted, _) = await context.Server.TryElicit(new PluginMcpServerInput
            {
                ServerName = serverName ?? string.Empty,
                ServerJson = serverJson ?? string.Empty
            }, cancellationToken);
            if (notAccepted is not null) return notAccepted;
            ArgumentNullException.ThrowIfNull(typed);
            JsonNode? node;
            try { node = JsonNode.Parse(typed.ServerJson); }
            catch (JsonException exception) { throw new ValidationException(exception.Message); }
            var serverValidation = AgentPluginSpecification.ValidateMcpServer(typed.ServerName, node);
            if (serverValidation.Value is null)
                throw new ValidationException(string.Join(" ", serverValidation.Diagnostics.Select(item => item.Message)));

            var drive = await graph.GetDefaultDriveAsync(cancellationToken)
                ?? throw new InvalidOperationException("Could not resolve default OneDrive.");
            await EnsurePluginExistsAsync(graph, drive.Id!, name, cancellationToken);
            var document = await ReadMcpOrCreateAsync(graph, drive.Id!, name, cancellationToken);
            ((JsonObject)document["mcpServers"]!)[typed.ServerName] = serverValidation.Value;
            await WriteJsonAsync(graph, drive.Id!, name, AgentPluginSpecification.McpManifestName, document, cancellationToken);
            return Mutation("updated MCP server in", name, [AgentPluginSpecification.McpManifestName], new { serverName = typed.ServerName }).ToJsonContentBlock(PluginUri(name, AgentPluginSpecification.McpManifestName)).ToCallToolResult();
        }));

    [Description("Delete one MCP server entry while preserving the Agent Plugin and all independent components.")]
    [McpServerTool(Title = "Delete OneDrive Agent Plugin MCP Server", Name = "onedrive_plugin_editor_delete_mcp_server",
        ReadOnly = false, Idempotent = true, OpenWorld = false, Destructive = true)]
    public static async Task<CallToolResult?> DeleteMcpServer(
        string pluginName,
        string serverName,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async graph =>
        {
            var name = RequirePluginName(pluginName);
            if (string.IsNullOrWhiteSpace(serverName)) throw new ValidationException("serverName is required.");
            var drive = await graph.GetDefaultDriveAsync(cancellationToken)
                ?? throw new InvalidOperationException("Could not resolve default OneDrive.");
            var document = await ReadMcpOrCreateAsync(graph, drive.Id!, name, cancellationToken);
            var servers = (JsonObject)document["mcpServers"]!;
            if (!servers.Remove(serverName))
                throw new ValidationException($"MCP server '{serverName}' was not found.");
            await WriteJsonAsync(graph, drive.Id!, name, AgentPluginSpecification.McpManifestName, document, cancellationToken);
            return Mutation("deleted MCP server from", name, [AgentPluginSpecification.McpManifestName], new { serverName }).ToJsonContentBlock(PluginUri(name, AgentPluginSpecification.McpManifestName)).ToCallToolResult();
        }));

    [Description("Delete an entire Agent Plugin package and all of its OneDrive files after explicit confirmation.")]
    [McpServerTool(Title = "Delete OneDrive Agent Plugin", Name = "onedrive_plugin_editor_delete",
        ReadOnly = false, Idempotent = true, OpenWorld = false, Destructive = true)]
    public static async Task<CallToolResult?> Delete(
        string pluginName,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async graph =>
        {
            var name = RequirePluginName(pluginName);
            var drive = await graph.GetDefaultDriveAsync(cancellationToken)
                ?? throw new InvalidOperationException("Could not resolve default OneDrive.");
            await EnsurePluginExistsAsync(graph, drive.Id!, name, cancellationToken);
            return await context.ConfirmAndDeleteAsync<DeleteByNameConfirmation>(
                name,
                async _ => await graph.DeleteItemIfExistsAsync(drive.Id!, OneDriveAgentPluginStorage.PluginRoot(name), cancellationToken),
                $"OneDrive Agent Plugin '{name}' deleted successfully.",
                cancellationToken);
        }));

    private static async Task<object> InspectPluginAsync(
        Microsoft.Graph.Beta.GraphServiceClient graph,
        string driveId,
        string pluginName,
        CancellationToken cancellationToken)
    {
        var files = await graph.ListPluginFilesAsync(driveId, pluginName, cancellationToken);
        var diagnostics = new List<AgentPluginDiagnostic>();
        JsonObject? manifest = null;
        var manifestBytes = await graph.ReadBytesAsync(driveId,
            OneDriveAgentPluginStorage.PluginPath(pluginName, AgentPluginSpecification.PluginManifestName), cancellationToken);
        if (manifestBytes is null)
            diagnostics.Add(new("error", "manifest", "manifest-missing", "plugin.json is required.", "plugin.json"));
        else
        {
            var result = AgentPluginSpecification.ValidateManifest(Encoding.UTF8.GetString(manifestBytes), pluginName);
            manifest = result.Value;
            diagnostics.AddRange(result.Diagnostics);
        }

        JsonObject? mcp = null;
        var mcpBytes = await graph.ReadBytesAsync(driveId,
            OneDriveAgentPluginStorage.PluginPath(pluginName, AgentPluginSpecification.McpManifestName), cancellationToken);
        if (mcpBytes is not null)
        {
            var result = AgentPluginSpecification.ValidateMcpDocument(Encoding.UTF8.GetString(mcpBytes));
            mcp = result.Value;
            diagnostics.AddRange(result.Diagnostics);
        }

        var skills = new List<object>();
        var skillNames = files.Select(file => file.Path.Split('/'))
            .Where(parts => parts.Length == 3 && parts[0] == "skills" && parts[2] == "SKILL.md")
            .Select(parts => parts[1]).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal);
        foreach (var skillName in skillNames)
        {
            var bytes = await graph.ReadBytesAsync(driveId,
                OneDriveAgentPluginStorage.PluginPath(pluginName, $"skills/{skillName}/SKILL.md"), cancellationToken);
            var parsed = OpenSkills.SkillDocumentParser.Parse(Encoding.UTF8.GetString(bytes ?? []), skillName);
            foreach (var error in parsed.Errors)
                diagnostics.Add(new("error", "skill", "skill-invalid", error, $"skills/{skillName}/SKILL.md", skillName));
            foreach (var warning in parsed.Warnings)
                diagnostics.Add(new("warning", "skill", "skill-warning", warning, $"skills/{skillName}/SKILL.md", skillName));
            skills.Add(new
            {
                name = parsed.Name ?? skillName,
                directory = skillName,
                description = parsed.Description,
                valid = parsed.Errors.Count == 0,
                fileCount = files.Count(file => file.Path.StartsWith($"skills/{skillName}/", StringComparison.Ordinal))
            });
        }

        return new
        {
            name = pluginName,
            valid = manifest is not null,
            manifest,
            mcp,
            skills,
            files,
            diagnostics
        };
    }

    private static JsonObject BuildManifest(PluginManifestInput input)
    {
        var manifest = new JsonObject { ["$schema"] = AgentPluginSpecification.PluginSchema, ["name"] = input.Name.Trim() };
        AddOptional(manifest, "version", input.Version);
        AddOptional(manifest, "description", input.Description);
        var author = new JsonObject();
        AddOptional(author, "name", input.AuthorName);
        AddOptional(author, "email", input.AuthorEmail);
        AddOptional(author, "url", input.AuthorUrl);
        if (author.Count > 0) manifest["author"] = author;
        AddOptional(manifest, "homepage", input.Homepage);
        AddOptional(manifest, "repository", input.Repository);
        AddOptional(manifest, "license", input.License);
        var keywords = ParseKeywords(input.Keywords);
        if (keywords.Count > 0) manifest["keywords"] = keywords;
        if (!string.IsNullOrWhiteSpace(input.ExtensionsJson))
        {
            try
            {
                manifest["extensions"] = JsonNode.Parse(input.ExtensionsJson) as JsonObject
                    ?? throw new ValidationException("extensionsJson must contain a JSON object.");
            }
            catch (JsonException exception)
            {
                throw new ValidationException($"extensionsJson is invalid: {exception.Message}");
            }
        }
        return manifest;
    }

    private static async Task<JsonObject> ReadRequiredManifestAsync(
        Microsoft.Graph.Beta.GraphServiceClient graph, string driveId, string pluginName, CancellationToken cancellationToken)
    {
        var bytes = await graph.ReadBytesAsync(driveId,
            OneDriveAgentPluginStorage.PluginPath(pluginName, AgentPluginSpecification.PluginManifestName), cancellationToken)
            ?? throw new ValidationException($"Agent Plugin '{pluginName}' is missing plugin.json.");
        var validation = AgentPluginSpecification.ValidateManifest(Encoding.UTF8.GetString(bytes), pluginName);
        return validation.Value ?? throw new ValidationException(string.Join(" ", validation.Diagnostics.Select(item => item.Message)));
    }

    private static async Task<JsonObject> ReadMcpOrCreateAsync(
        Microsoft.Graph.Beta.GraphServiceClient graph, string driveId, string pluginName, CancellationToken cancellationToken)
    {
        await EnsurePluginExistsAsync(graph, driveId, pluginName, cancellationToken);
        var bytes = await graph.ReadBytesAsync(driveId,
            OneDriveAgentPluginStorage.PluginPath(pluginName, AgentPluginSpecification.McpManifestName), cancellationToken);
        if (bytes is null)
            return new JsonObject { ["$schema"] = AgentPluginSpecification.McpSchema, ["mcpServers"] = new JsonObject() };
        var validation = AgentPluginSpecification.ValidateMcpDocument(Encoding.UTF8.GetString(bytes));
        return validation.Value ?? throw new ValidationException(string.Join(" ", validation.Diagnostics.Select(item => item.Message)));
    }

    private static async Task EnsurePluginExistsAsync(
        Microsoft.Graph.Beta.GraphServiceClient graph, string driveId, string pluginName, CancellationToken cancellationToken)
    {
        var item = await graph.GetItemByPathOrNullAsync(driveId, OneDriveAgentPluginStorage.PluginRoot(pluginName), cancellationToken);
        if (item?.Folder is null) throw new ValidationException($"Agent Plugin '{pluginName}' was not found.");
    }

    private static Task WriteJsonAsync(
        Microsoft.Graph.Beta.GraphServiceClient graph, string driveId, string pluginName, string relativePath,
        JsonObject value, CancellationToken cancellationToken)
        => graph.WriteBytesAsync(driveId, OneDriveAgentPluginStorage.PluginPath(pluginName, relativePath),
            Encoding.UTF8.GetBytes(value.ToJsonString(JsonOptions) + "\n"), cancellationToken);

    private static string RequirePluginName(string value)
    {
        var name = value?.Trim() ?? string.Empty;
        if (!AgentPluginSpecification.IsValidPluginName(name))
            throw new ValidationException("pluginName does not satisfy Agent Plugins v1 naming constraints.");
        return name;
    }

    private static string RequireEditableFilePath(string value)
    {
        var path = AgentPluginSpecification.NormalizePackagePath(value);
        if (AgentPluginSpecification.IsProtectedPackagePath(path))
            throw new ValidationException("plugin.json, mcp.json, and skills/ are managed only by their dedicated tools.");
        return path;
    }

    private static void AddOptional(JsonObject target, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) target[key] = value.Trim();
    }

    private static JsonArray ParseKeywords(string? value)
        => new((value ?? string.Empty).Split([',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal).Select(item => (JsonNode?)JsonValue.Create(item)).ToArray());

    private static string? RenderKeywords(JsonArray? keywords)
        => keywords is null ? null : string.Join(", ", keywords.Select(item => item?.GetValue<string>()));

    private static string? ReadJsonString(JsonObject? value, string property)
        => value?[property] is JsonValue node && node.TryGetValue<string>(out var text) ? text : null;

    private static object Mutation(string action, string pluginName, IReadOnlyList<string> affectedFiles, object? details = null)
        => new { success = true, action, pluginName, affectedFiles, details };

    private static string PluginUri(string pluginName, string? relativePath = null)
        => string.IsNullOrWhiteSpace(relativePath)
            ? $"onedrive://plugins/{pluginName}"
            : $"onedrive://plugins/{pluginName}/{relativePath}";
}
