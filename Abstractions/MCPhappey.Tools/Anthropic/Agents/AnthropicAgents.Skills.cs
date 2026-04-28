using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Anthropic.Agents;

public static partial class AnthropicAgents
{
    [Description("Add or replace a skill on an Anthropic Managed Agent using flat primitive inputs.")]
    [McpServerTool(
        Title = "Add skill to Anthropic Agent",
        Name = "anthropic_agents_add_skill",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = false)]
    public static async Task<CallToolResult?> AnthropicAgents_AddSkillToAgent(
        [Description("Agent ID.")] string agentId,
        [Description("Skill ID to add.")] string skillId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Skill type. Allowed values: anthropic or custom.")] string skillType = "anthropic",
        [Description("Optional skill version.")] string? skillVersion = null,
        
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var (typed, _, _) = await requestContext.Server.TryElicit(new AnthropicAgentSkillMutationRequest
                {
                    AgentId = agentId,
                    SkillId = skillId,
                    SkillType = skillType,
                    SkillVersion = skillVersion,
                   
                }, cancellationToken);

                if (string.IsNullOrWhiteSpace(typed.AgentId))
                    throw new ValidationException("agentId is required.");

                if (string.IsNullOrWhiteSpace(typed.SkillId))
                    throw new ValidationException("skillId is required.");

                ValidateSkillType(typed.SkillType);

                var current = await GetAgentAsync(serviceProvider, typed.AgentId,  cancellationToken);
                var skills = AnthropicManagedAgentsHttp.CloneArray(current["skills"]);

                RemoveSkill(skills, typed.SkillId, typed.SkillType);

                var skill = new JsonObject
                {
                    ["skill_id"] = typed.SkillId,
                    ["type"] = typed.SkillType
                };

                if (!string.IsNullOrWhiteSpace(typed.SkillVersion))
                    skill["version"] = typed.SkillVersion;

                skills.Add(skill);

                var body = CreateVersionedUpdateBody(current);
                body["skills"] = skills;

                return await UpdateAgentAsync(serviceProvider, typed.AgentId,  body, cancellationToken);
            }));

    [Description("Remove a skill from an Anthropic Managed Agent after explicit typed confirmation.")]
    [McpServerTool(
        Title = "Remove skill from Anthropic Agent",
        Name = "anthropic_agents_remove_skill",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> AnthropicAgents_RemoveSkillFromAgent(
        [Description("Agent ID.")] string agentId,
        [Description("Skill ID to remove.")] string skillId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Skill type. Allowed values: anthropic or custom.")] string skillType = "anthropic",
        
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ValidateSkillType(skillType);

                var expected = $"{agentId}:{skillType}:{skillId}";
                await AnthropicManagedAgentsHttp.ConfirmDeleteAsync<AnthropicDeleteAgentItem>(requestContext.Server, expected, cancellationToken);

                var current = await GetAgentAsync(serviceProvider, agentId,  cancellationToken);
                var skills = AnthropicManagedAgentsHttp.CloneArray(current["skills"]);

                if (!RemoveSkill(skills, skillId, skillType))
                    throw new ValidationException($"Skill '{skillId}' with type '{skillType}' was not found on agent '{agentId}'.");

                var body = CreateVersionedUpdateBody(current);
                body["skills"] = skills;

                return await UpdateAgentAsync(serviceProvider, agentId,  body, cancellationToken);
            }));
}
