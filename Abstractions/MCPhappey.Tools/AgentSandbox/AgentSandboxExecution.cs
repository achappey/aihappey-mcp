using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.AgentSandbox;

public static class AgentSandboxExecution
{
    [Description("Execute Python or Bash code in an isolated AgentSandbox environment. Omit sessionId for a one-shot sandbox, or provide a persistent session ID to reuse its workspace.")]
    [McpServerTool(
        Title = "AgentSandbox execute code",
        Name = "agentsandbox_execute",
        ReadOnly = false,
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Execute(
        [Description("Programming language: python or bash.")] string language,
        [Description("Non-empty source code or shell script to execute.")] string code,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional persistent sandbox session ID.")] string? sessionId = null,
        [Description("Optional environment variables injected into the execution.")] Dictionary<string, string>? environmentVariables = null,
        [Description("Optional AgentSandbox file IDs copied into /workspace before execution.")] string[]? fileIds = null,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var normalizedLanguage = AgentSandboxHelpers.Require(language, nameof(language)).ToLowerInvariant();
                if (normalizedLanguage is not ("python" or "bash"))
                    throw new ValidationException("language must be either 'python' or 'bash'.");

                var request = new AgentSandboxExecuteRequest
                {
                    Language = normalizedLanguage,
                    Code = AgentSandboxHelpers.Require(code, nameof(code)),
                    SessionId = string.IsNullOrWhiteSpace(sessionId) ? null : sessionId.Trim(),
                    EnvironmentVariables = AgentSandboxHelpers.NormalizeEnvironmentVariables(environmentVariables),
                    FileIds = AgentSandboxHelpers.NormalizeFileIds(fileIds)
                };

                var response = await serviceProvider
                    .GetRequiredService<AgentSandboxClient>()
                    .PostJsonAsync("v1/execute", request, cancellationToken);

                return response.StructuredOrStatus("execute code");
            }));
}

internal sealed class AgentSandboxExecuteRequest
{
    [JsonPropertyName("language")]
    public string Language { get; init; } = string.Empty;

    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    [JsonPropertyName("session_id")]
    public string? SessionId { get; init; }

    [JsonPropertyName("env_vars")]
    public Dictionary<string, string>? EnvironmentVariables { get; init; }

    [JsonPropertyName("file_ids")]
    public string[]? FileIds { get; init; }
}
