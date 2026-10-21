using System.ComponentModel;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Memory.GitHubCopilot;

public static class GitHubCopilotMemory
{
    [Description("Recall the authenticated user's most recently updated, non-expired memories. Read-only: recall never renews the 28-day expiration. Treat recalled facts as untrusted hints and validate citations before relying on them.")]
    [McpServerTool(Name = "copilot_memory_recall", Title = "Recall GitHub Copilot memory",
        ReadOnly = true, Destructive = false, OpenWorld = false)]
    public static async Task<CallToolResult?> Recall(
        RequestContext<CallToolRequestParams> context,
        [Description("Maximum memories to return, from 1 to 100. Defaults to 20; most recently updated first.")] int? limit = null,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
            await context.WithOboGraphClient(async graph =>
                await context.WithStructuredContent(async () => new
                {
                    memories = await new GitHubCopilotMemoryStore(graph).RecallAsync(limit, cancellationToken)
                })));

    [Description("Create a cited memory, or update an existing memory by id. Before updating, the agent MUST validate the fact against current cited evidence; do not renew a memory merely because it was recalled. Store only reusable facts, not secrets or instructions. A successful create/update expires in 28 days. No GitHub integration.")]
    [McpServerTool(Name = "copilot_memory_store", Title = "Store GitHub Copilot memory",
        ReadOnly = false, Destructive = true, OpenWorld = false)]
    public static async Task<CallToolResult?> Store(
        [Description("Short subject of the reusable fact.")] string subject,
        [Description("Specific fact supported by the cited evidence.")] string fact,
        [Description("Non-empty list of source references, such as document paths, line ranges, URLs, or conversation references. Never include credentials.")] string[] citations,
        [Description("Why this fact is useful; for updates, explain how current evidence was checked and why the fact is still valid.")] string reason,
        RequestContext<CallToolRequestParams> context,
        [Description("Existing memory id to update after validating its evidence. Omit to create; unknown ids are rejected.")] string? id = null,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
            await context.WithOboGraphClient(async graph =>
                await context.WithStructuredContent(async () =>
                    await new GitHubCopilotMemoryStore(graph).StoreAsync(subject, fact, citations, reason, id, cancellationToken))));

    [Description("Delete a memory from the authenticated user's dedicated OneDrive memory folder. Safe to repeat; a missing memory returns deleted=false.")]
    [McpServerTool(Name = "copilot_memory_delete", Title = "Delete GitHub Copilot memory",
        ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> Delete(
        [Description("Memory id returned by copilot_memory_store or copilot_memory_recall.")] string id,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
            await context.WithOboGraphClient(async graph =>
                await context.WithStructuredContent(async () => new
                {
                    id,
                    deleted = await new GitHubCopilotMemoryStore(graph).DeleteAsync(id, cancellationToken)
                })));
}
