using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using MCPhappey.Tools.Google.Agents;
using MCPhappey.Tools.Memory.OneDrive;
using MCPhappey.Tools.OneDrive.AgentPlugins;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Google.AgentEditor;

public static partial class GoogleAgentEditor
{
    [Description("Please confirm to delete: {0}")]
    public sealed class DeleteDraftConfirmation : IHasName
    {
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    }

    [Description("List Google Agent JSON drafts stored in /google-agents in the user's OneDrive.")]
    [McpServerTool(Title = "List Google Agent Drafts", Name = "google_agent_editor_list", ReadOnly = true, Idempotent = true, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> List(
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async graph =>
        await context.WithStructuredContent(async () =>
        {
            var drive = await graph.GetDefaultDriveAsync(cancellationToken)
                ?? throw new InvalidOperationException("Could not resolve default OneDrive.");
            var folders = await graph.ListFoldersAsync(drive.Id!, RootFolder, cancellationToken);
            return new { root = $"/{RootFolder}", drafts = folders.Select(item => new { name = item.Name, webUrl = item.WebUrl }) };
        })));

    [Description("Create a minimal, valid Google Agent JSON draft from primitive scalar fields.")]
    [McpServerTool(Title = "Create Google Agent Draft", Name = "google_agent_editor_create", ReadOnly = false, Idempotent = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> Create(
        string draftName,
        RequestContext<CallToolRequestParams> context,
        string? id = null,
        string? description = null,
        string? systemInstruction = null,
        string? baseAgent = null,
        string? model = null,
        long? maxTotalTokens = null,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async graph =>
        await context.WithStructuredContent(async () =>
        {
            var name = RequireDraftName(draftName);
            if (maxTotalTokens < 1) throw new ValidationException("maxTotalTokens must be positive.");
            var drive = await graph.GetDefaultDriveAsync(cancellationToken)
                ?? throw new InvalidOperationException("Could not resolve default OneDrive.");
            if (await graph.GetItemByPathOrNullAsync(drive.Id!, DraftRoot(name), cancellationToken) is not null)
                throw new ValidationException($"Google Agent draft '{name}' already exists.");
            var document = new JsonObject();
            GoogleAgents.Add(document, "id", id);
            GoogleAgents.Add(document, "description", description);
            GoogleAgents.Add(document, "system_instruction", systemInstruction);
            GoogleAgents.Add(document, "base_agent", baseAgent);
            if (model is not null || maxTotalTokens is not null)
            {
                var config = new JsonObject { ["type"] = "antigravity" };
                GoogleAgents.Add(config, "model", model);
                if (maxTotalTokens is not null) config["max_total_tokens"] = maxTotalTokens.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                document["agent_config"] = config;
            }
            await WriteAsync(graph, drive.Id!, name, document, cancellationToken);
            return new { success = true, draftName = name, path = DraftPath(name), document };
        })));

    [Description("Read and validate a Google Agent JSON draft.")]
    [McpServerTool(Title = "Inspect Google Agent Draft", Name = "google_agent_editor_inspect", ReadOnly = true, Idempotent = true, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> Inspect(
        string draftName,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async graph =>
        await context.WithStructuredContent(async () =>
        {
            var drive = await graph.GetDefaultDriveAsync(cancellationToken)
                ?? throw new InvalidOperationException("Could not resolve default OneDrive.");
            var document = await ReadRequiredAsync(graph, drive.Id!, draftName, cancellationToken);
            var diagnostics = GoogleAgentDocument.Validate(document);
            return new { draftName = RequireDraftName(draftName), valid = diagnostics.Count == 0, diagnostics, document };
        })));

    [Description("Read the raw, formatted agent.json file for a Google Agent draft.")]
    [McpServerTool(Title = "Read Google Agent Draft JSON", Name = "google_agent_editor_read", ReadOnly = true, Idempotent = true, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> Read(
        string draftName,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async graph =>
        {
            var drive = await graph.GetDefaultDriveAsync(cancellationToken)
                ?? throw new InvalidOperationException("Could not resolve default OneDrive.");
            var document = await ReadRequiredAsync(graph, drive.Id!, draftName, cancellationToken);
            return document.ToJsonString(JsonOptions).ToTextContentBlock().ToCallToolResult();
        }));

    [Description("Update scalar fields in a Google Agent draft. Omitted values remain unchanged; clear flags remove optional values.")]
    [McpServerTool(Title = "Update Google Agent Draft", Name = "google_agent_editor_update", ReadOnly = false, Idempotent = true, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> Update(
        string draftName,
        RequestContext<CallToolRequestParams> context,
        string? id = null,
        string? description = null,
        string? systemInstruction = null,
        string? baseAgent = null,
        string? model = null,
        long? maxTotalTokens = null,
        bool clearId = false,
        bool clearDescription = false,
        bool clearSystemInstruction = false,
        bool clearBaseAgent = false,
        bool clearAgentConfig = false,
        CancellationToken cancellationToken = default)
        => await WithDraftMutation(draftName, context, document =>
        {
            Set(document, "id", id, clearId);
            Set(document, "description", description, clearDescription);
            Set(document, "system_instruction", systemInstruction, clearSystemInstruction);
            Set(document, "base_agent", baseAgent, clearBaseAgent);
            if (clearAgentConfig) document.Remove("agent_config");
            else if (model is not null || maxTotalTokens is not null)
            {
                if (maxTotalTokens < 1) throw new ValidationException("maxTotalTokens must be positive.");
                var config = EnsureObject(document, "agent_config");
                config["type"] = "antigravity";
                if (model is not null) config["model"] = model;
                if (maxTotalTokens is not null) config["max_total_tokens"] = maxTotalTokens.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }, cancellationToken);

    [Description("Validate and deploy a stored Google Agent draft to the Google Agents API.")]
    [McpServerTool(Title = "Deploy Google Agent Draft", Name = "google_agent_editor_deploy", ReadOnly = false, Idempotent = false, OpenWorld = true, Destructive = false)]
    public static async Task<CallToolResult?> Deploy(
        string draftName,
        IServiceProvider services,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async graph =>
        await context.WithStructuredContent(async () =>
        {
            var drive = await graph.GetDefaultDriveAsync(cancellationToken)
                ?? throw new InvalidOperationException("Could not resolve default OneDrive.");
            var document = await ReadRequiredAsync(graph, drive.Id!, draftName, cancellationToken);
            return await GoogleAgents.CreateValidatedAsync(services, document, cancellationToken);
        })));

    [Description("Delete a Google Agent draft from OneDrive after explicit typed confirmation. This does not delete a deployed Google Agent.")]
    [McpServerTool(Title = "Delete Google Agent Draft", Name = "google_agent_editor_delete", ReadOnly = false, Idempotent = true, OpenWorld = false, Destructive = true)]
    public static async Task<CallToolResult?> Delete(
        string draftName,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async graph =>
        {
            var name = RequireDraftName(draftName);
            var drive = await graph.GetDefaultDriveAsync(cancellationToken)
                ?? throw new InvalidOperationException("Could not resolve default OneDrive.");
            _ = await ReadRequiredAsync(graph, drive.Id!, name, cancellationToken);
            return await context.ConfirmAndDeleteAsync<DeleteDraftConfirmation>(
                name,
                async ct => await graph.DeleteItemIfExistsAsync(drive.Id!, DraftRoot(name), ct),
                $"Google Agent draft '{name}' deleted successfully.",
                cancellationToken);
        }));

    private static async Task<CallToolResult?> WithDraftMutation(
        string draftName,
        RequestContext<CallToolRequestParams> context,
        Action<JsonObject> mutation,
        CancellationToken cancellationToken)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async graph =>
        await context.WithStructuredContent(async () =>
        {
            var drive = await graph.GetDefaultDriveAsync(cancellationToken)
                ?? throw new InvalidOperationException("Could not resolve default OneDrive.");
            return await MutateAsync(graph, drive.Id!, draftName, mutation, cancellationToken);
        })));

    private static void Set(JsonObject document, string field, string? value, bool clear)
    {
        if (clear && value is not null) throw new ValidationException($"{field} and its clear flag cannot be used together.");
        if (clear) document.Remove(field);
        else if (value is not null) document[field] = value;
    }
}
