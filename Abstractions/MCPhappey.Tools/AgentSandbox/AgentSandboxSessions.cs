using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.AgentSandbox;

public static class AgentSandboxSessions
{
    [Description("Create a persistent AgentSandbox session, optionally initialized with environment variables and existing AgentSandbox files.")]
    [McpServerTool(
        Title = "AgentSandbox create session",
        Name = "agentsandbox_sessions_create",
        ReadOnly = false,
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Create(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional environment variables injected when the session is created.")] Dictionary<string, string>? environmentVariables = null,
        [Description("Optional owned AgentSandbox file IDs copied into /workspace atomically.")] string[]? fileIds = null,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var response = await serviceProvider
                    .GetRequiredService<AgentSandboxClient>()
                    .PostJsonAsync("v1/sessions", new AgentSandboxSessionCreateRequest
                    {
                        EnvironmentVariables = AgentSandboxHelpers.NormalizeEnvironmentVariables(environmentVariables),
                        FileIds = AgentSandboxHelpers.NormalizeFileIds(fileIds)
                    }, cancellationToken);

                return response.StructuredOrStatus("create session");
            }));

    [Description("Inject one or more owned AgentSandbox files into the /workspace directory of a running session. The operation is atomic.")]
    [McpServerTool(
        Title = "AgentSandbox inject files into session",
        Name = "agentsandbox_sessions_inject_files",
        ReadOnly = false,
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> InjectFiles(
        [Description("Persistent AgentSandbox session ID.")] string sessionId,
        [Description("One or more owned AgentSandbox file IDs to copy into /workspace.")] string[] fileIds,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var normalizedFileIds = AgentSandboxHelpers.NormalizeFileIds(fileIds, required: true)!;
                var response = await serviceProvider
                    .GetRequiredService<AgentSandboxClient>()
                    .PostJsonAsync(
                        $"v1/sessions/{AgentSandboxHelpers.EscapePath(sessionId)}/files",
                        new AgentSandboxFileIdsRequest { FileIds = normalizedFileIds },
                        cancellationToken);

                return response.StructuredOrStatus("inject files into session");
            }));

    [Description("Permanently destroy a persistent AgentSandbox session and terminate its underlying sandbox after explicit typed confirmation.")]
    [McpServerTool(
        Title = "AgentSandbox delete session",
        Name = "agentsandbox_sessions_delete",
        ReadOnly = false,
        Destructive = true,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Delete(
        [Description("Persistent AgentSandbox session ID to destroy.")] string sessionId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            var normalizedSessionId = AgentSandboxHelpers.Require(sessionId, nameof(sessionId));
            var client = serviceProvider.GetRequiredService<AgentSandboxClient>();

            return await requestContext.ConfirmAndDeleteAsync<ConfirmDeleteAgentSandboxSession>(
                normalizedSessionId,
                async ct => _ = await client.DeleteAsync(
                    $"v1/sessions/{AgentSandboxHelpers.EscapePath(normalizedSessionId)}",
                    ct),
                $"AgentSandbox session '{normalizedSessionId}' destroyed successfully.",
                cancellationToken);
        });
}

internal sealed class AgentSandboxSessionCreateRequest
{
    [JsonPropertyName("env_vars")]
    public Dictionary<string, string>? EnvironmentVariables { get; init; }

    [JsonPropertyName("file_ids")]
    public string[]? FileIds { get; init; }
}

internal sealed class AgentSandboxFileIdsRequest
{
    [JsonPropertyName("file_ids")]
    public string[] FileIds { get; init; } = [];
}

[Description("Please confirm destruction of AgentSandbox session: {0}")]
public sealed class ConfirmDeleteAgentSandboxSession : IHasName
{
    [JsonPropertyName("name")]
    [Required]
    [Description("Type the session ID exactly to confirm permanent destruction.")]
    public string Name { get; set; } = string.Empty;
}
