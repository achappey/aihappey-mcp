using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.CaseDev;

public static class CaseDevAgents
{
    [Description("Create a Case.dev agent definition. Parameters are always confirmed via elicitation before the POST request is sent.")]
    [McpServerTool(Title = "Case.dev create agent", Name = "casedev_agents_create", Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> CaseDev_Agents_Create(
        [Description("Display name for the agent.")] string name,
        [Description("System instructions that define agent behavior.")] string instructions,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional description of the agent.")] string? description = null,
        [Description("Optional model identifier. Defaults to anthropic/claude-sonnet-4.6.")] string? model = null,
        [Description("Optional vault IDs as comma, semicolon, or newline separated values.")] string? vaultIds = null,
        [Description("Optional vault group IDs as comma, semicolon, or newline separated values.")] string? vaultGroups = null,
        [Description("Optional allowlist of enabled tools as comma, semicolon, or newline separated values.")] string? enabledTools = null,
        [Description("Optional denylist of disabled tools as comma, semicolon, or newline separated values.")] string? disabledTools = null,
        [Description("Optional sandbox CPU count.")] int? sandboxCpu = null,
        [Description("Optional sandbox memory in MiB.")] int? sandboxMemoryMiB = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var (typed, notAccepted, _) = await requestContext.Server.TryElicit(new CaseDevCreateAgentRequest
                {
                    Name = name,
                    Instructions = instructions,
                    Description = description,
                    Model = model,
                    VaultIds = vaultIds,
                    VaultGroups = vaultGroups,
                    EnabledTools = enabledTools,
                    DisabledTools = disabledTools,
                    SandboxCpu = sandboxCpu,
                    SandboxMemoryMiB = sandboxMemoryMiB
                }, cancellationToken);

                ValidateCreateOrUpdateToolLists(typed.EnabledTools, typed.DisabledTools);
                if (string.IsNullOrWhiteSpace(typed.Name))
                    throw new ValidationException("name is required.");
                if (string.IsNullOrWhiteSpace(typed.Instructions))
                    throw new ValidationException("instructions is required.");

                var body = new JsonObject
                {
                    ["name"] = typed.Name,
                    ["instructions"] = typed.Instructions
                };

                if (!string.IsNullOrWhiteSpace(typed.Description)) body["description"] = typed.Description;
                if (!string.IsNullOrWhiteSpace(typed.Model)) body["model"] = typed.Model;
                if (CaseDevHelpers.ToJsonArray(CaseDevHelpers.ParseDelimited(typed.VaultIds)) is { } vaultIdsArray) body["vaultIds"] = vaultIdsArray;
                if (CaseDevHelpers.ToJsonArray(CaseDevHelpers.ParseDelimited(typed.VaultGroups)) is { } vaultGroupsArray) body["vaultGroups"] = vaultGroupsArray;
                if (CaseDevHelpers.ToJsonArray(CaseDevHelpers.ParseDelimited(typed.EnabledTools)) is { } enabledToolsArray) body["enabledTools"] = enabledToolsArray;
                if (CaseDevHelpers.ToJsonArray(CaseDevHelpers.ParseDelimited(typed.DisabledTools)) is { } disabledToolsArray) body["disabledTools"] = disabledToolsArray;

                if (typed.SandboxCpu.HasValue || typed.SandboxMemoryMiB.HasValue)
                {
                    var sandbox = new JsonObject();
                    if (typed.SandboxCpu.HasValue) sandbox["cpu"] = typed.SandboxCpu.Value;
                    if (typed.SandboxMemoryMiB.HasValue) sandbox["memoryMiB"] = typed.SandboxMemoryMiB.Value;
                    body["sandbox"] = sandbox;
                }

                var client = serviceProvider.GetRequiredService<CaseDevClient>();
                var result = await client.SendJsonAsync(HttpMethod.Post, "/agent/v1/agents", body, cancellationToken);
                return result.Json ?? new JsonObject();
            }));

    [Description("Update a Case.dev agent definition. Only provided fields are changed, and parameters are always confirmed via elicitation before the PATCH request is sent.")]
    [McpServerTool(Title = "Case.dev update agent", Name = "casedev_agents_update", Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> CaseDev_Agents_Update(
        [Description("Agent ID to update.")] string id,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional updated display name.")] string? name = null,
        [Description("Optional updated description.")] string? description = null,
        [Description("Optional updated system instructions.")] string? instructions = null,
        [Description("Optional updated model identifier.")] string? model = null,
        [Description("Optional vault IDs as comma, semicolon, or newline separated values.")] string? vaultIds = null,
        [Description("Optional vault group IDs as comma, semicolon, or newline separated values.")] string? vaultGroups = null,
        [Description("Optional allowlist of enabled tools as comma, semicolon, or newline separated values.")] string? enabledTools = null,
        [Description("Optional denylist of disabled tools as comma, semicolon, or newline separated values.")] string? disabledTools = null,
        [Description("Optional sandbox CPU count.")] int? sandboxCpu = null,
        [Description("Optional sandbox memory in MiB.")] int? sandboxMemoryMiB = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var (typed, notAccepted, _) = await requestContext.Server.TryElicit(new CaseDevUpdateAgentRequest
                {
                    Id = id,
                    Name = name,
                    Description = description,
                    Instructions = instructions,
                    Model = model,
                    VaultIds = vaultIds,
                    VaultGroups = vaultGroups,
                    EnabledTools = enabledTools,
                    DisabledTools = disabledTools,
                    SandboxCpu = sandboxCpu,
                    SandboxMemoryMiB = sandboxMemoryMiB
                }, cancellationToken);

                if (string.IsNullOrWhiteSpace(typed.Id))
                    throw new ValidationException("id is required.");

                ValidateCreateOrUpdateToolLists(typed.EnabledTools, typed.DisabledTools);

                var body = new JsonObject();
                if (!string.IsNullOrWhiteSpace(typed.Name)) body["name"] = typed.Name;
                if (!string.IsNullOrWhiteSpace(typed.Description)) body["description"] = typed.Description;
                if (!string.IsNullOrWhiteSpace(typed.Instructions)) body["instructions"] = typed.Instructions;
                if (!string.IsNullOrWhiteSpace(typed.Model)) body["model"] = typed.Model;
                if (CaseDevHelpers.ToJsonArray(CaseDevHelpers.ParseDelimited(typed.VaultIds)) is { } vaultIdsArray) body["vaultIds"] = vaultIdsArray;
                if (CaseDevHelpers.ToJsonArray(CaseDevHelpers.ParseDelimited(typed.VaultGroups)) is { } vaultGroupsArray) body["vaultGroups"] = vaultGroupsArray;
                if (CaseDevHelpers.ToJsonArray(CaseDevHelpers.ParseDelimited(typed.EnabledTools)) is { } enabledToolsArray) body["enabledTools"] = enabledToolsArray;
                if (CaseDevHelpers.ToJsonArray(CaseDevHelpers.ParseDelimited(typed.DisabledTools)) is { } disabledToolsArray) body["disabledTools"] = disabledToolsArray;

                if (typed.SandboxCpu.HasValue || typed.SandboxMemoryMiB.HasValue)
                {
                    var sandbox = new JsonObject();
                    if (typed.SandboxCpu.HasValue) sandbox["cpu"] = typed.SandboxCpu.Value;
                    if (typed.SandboxMemoryMiB.HasValue) sandbox["memoryMiB"] = typed.SandboxMemoryMiB.Value;
                    body["sandbox"] = sandbox;
                }

                if (body.Count == 0)
                    throw new ValidationException("At least one field to update is required.");

                var client = serviceProvider.GetRequiredService<CaseDevClient>();
                var result = await client.SendJsonAsync(HttpMethod.Patch, $"/agent/v1/agents/{Uri.EscapeDataString(typed.Id)}", body, cancellationToken);
                return result.Json ?? new JsonObject();
            }));

    [Description("Delete a Case.dev agent after explicit confirmation using the shared delete confirmation helper.")]
    [McpServerTool(Title = "Case.dev delete agent", Name = "casedev_agents_delete", Destructive = true, OpenWorld = true)]
    public static async Task<CallToolResult?> CaseDev_Agents_Delete(
        [Description("Agent ID to delete.")] string id,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ValidationException("id is required.");

            var client = serviceProvider.GetRequiredService<CaseDevClient>();

            return await requestContext.ConfirmAndDeleteAsync<ConfirmDeleteCaseDevAgent>(
                id,
                async ct => _ = await client.DeleteAsync($"/agent/v1/agents/{Uri.EscapeDataString(id)}", ct),
                $"Agent '{id}' deleted successfully.",
                cancellationToken);
        });

    private static void ValidateCreateOrUpdateToolLists(string? enabledTools, string? disabledTools)
    {
        if (!string.IsNullOrWhiteSpace(enabledTools) && !string.IsNullOrWhiteSpace(disabledTools))
            throw new ValidationException("enabledTools and disabledTools are mutually exclusive. Provide only one of them.");
    }
}

[Description("Please confirm the Case.dev create agent request.")]
public sealed class CaseDevCreateAgentRequest
{
    [JsonPropertyName("name")]
    [Required]
    [Description("Display name for the agent.")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    [Description("Optional description of the agent.")]
    public string? Description { get; set; }

    [JsonPropertyName("instructions")]
    [Required]
    [Description("System instructions that define agent behavior.")]
    public string Instructions { get; set; } = string.Empty;

    [JsonPropertyName("model")]
    [Description("Optional model identifier.")]
    public string? Model { get; set; }

    [JsonPropertyName("vaultIds")]
    [Description("Optional vault IDs as comma, semicolon, or newline separated values.")]
    public string? VaultIds { get; set; }

    [JsonPropertyName("vaultGroups")]
    [Description("Optional vault group IDs as comma, semicolon, or newline separated values.")]
    public string? VaultGroups { get; set; }

    [JsonPropertyName("enabledTools")]
    [Description("Optional allowlist of enabled tools as comma, semicolon, or newline separated values.")]
    public string? EnabledTools { get; set; }

    [JsonPropertyName("disabledTools")]
    [Description("Optional denylist of disabled tools as comma, semicolon, or newline separated values.")]
    public string? DisabledTools { get; set; }

    [JsonPropertyName("sandboxCpu")]
    [Description("Optional sandbox CPU count.")]
    public int? SandboxCpu { get; set; }

    [JsonPropertyName("sandboxMemoryMiB")]
    [Description("Optional sandbox memory in MiB.")]
    public int? SandboxMemoryMiB { get; set; }
}

[Description("Please confirm the Case.dev update agent request.")]
public sealed class CaseDevUpdateAgentRequest
{
    [JsonPropertyName("id")]
    [Required]
    [Description("Agent ID to update.")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    [Description("Optional updated display name.")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    [Description("Optional updated description.")]
    public string? Description { get; set; }

    [JsonPropertyName("instructions")]
    [Description("Optional updated system instructions.")]
    public string? Instructions { get; set; }

    [JsonPropertyName("model")]
    [Description("Optional updated model identifier.")]
    public string? Model { get; set; }

    [JsonPropertyName("vaultIds")]
    [Description("Optional vault IDs as comma, semicolon, or newline separated values.")]
    public string? VaultIds { get; set; }

    [JsonPropertyName("vaultGroups")]
    [Description("Optional vault group IDs as comma, semicolon, or newline separated values.")]
    public string? VaultGroups { get; set; }

    [JsonPropertyName("enabledTools")]
    [Description("Optional allowlist of enabled tools as comma, semicolon, or newline separated values.")]
    public string? EnabledTools { get; set; }

    [JsonPropertyName("disabledTools")]
    [Description("Optional denylist of disabled tools as comma, semicolon, or newline separated values.")]
    public string? DisabledTools { get; set; }

    [JsonPropertyName("sandboxCpu")]
    [Description("Optional sandbox CPU count.")]
    public int? SandboxCpu { get; set; }

    [JsonPropertyName("sandboxMemoryMiB")]
    [Description("Optional sandbox memory in MiB.")]
    public int? SandboxMemoryMiB { get; set; }
}
