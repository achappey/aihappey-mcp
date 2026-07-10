using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.AgentMail;

public static class AgentMailDomains
{
    [Description("Create an AgentMail sending domain. Inputs are confirmed via elicitation before the POST request is sent.")]
    [McpServerTool(Title = "AgentMail create domain", Name = "agentmail_domains_create", Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> AgentMail_Domains_Create(
        [Description("Domain name, for example example.com.")] string domain,
        [Description("Whether bounce and complaint notifications are sent to inboxes.")] bool feedbackEnabled,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await CreateDomainAsync("create domain", "/v0/domains", null, domain, feedbackEnabled, serviceProvider, requestContext, cancellationToken);

    [Description("Delete an AgentMail domain after explicit confirmation using the shared delete confirmation helper.")]
    [McpServerTool(Title = "AgentMail delete domain", Name = "agentmail_domains_delete", Destructive = true, OpenWorld = true)]
    public static async Task<CallToolResult?> AgentMail_Domains_Delete(
        [Description("Domain ID/name to delete.")] string domainId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            AgentMailHelpers.Require(domainId, nameof(domainId));
            var client = serviceProvider.GetRequiredService<AgentMailClient>();
            return await requestContext.ConfirmAndDeleteAsync<ConfirmDeleteAgentMailDomain>(
                domainId,
                async ct => _ = await client.DeleteAsync($"/v0/domains/{AgentMailHelpers.EscapePath(domainId)}", ct),
                $"AgentMail domain '{domainId}' deleted successfully.",
                cancellationToken);
        });

    [Description("Verify an AgentMail domain. Inputs are confirmed via elicitation before the POST request is sent.")]
    [McpServerTool(Title = "AgentMail verify domain", Name = "agentmail_domains_verify", Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> AgentMail_Domains_Verify(
        [Description("Domain ID/name to verify.")] string domainId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var (typed, _, _) = await requestContext.Server.TryElicit(new AgentMailVerifyDomainRequest
                {
                    DomainId = domainId
                }, cancellationToken);

                AgentMailHelpers.Require(typed.DomainId, nameof(domainId));
                var client = serviceProvider.GetRequiredService<AgentMailClient>();
                var response = await client.PostAsync($"/v0/domains/{AgentMailHelpers.EscapePath(typed.DomainId)}/verify", new JsonObject(), cancellationToken);
                return response.StructuredOrStatus("verify domain");
            }));

    [Description("Create an AgentMail sending domain inside a pod. Inputs are confirmed via elicitation before the POST request is sent.")]
    [McpServerTool(Title = "AgentMail create pod domain", Name = "agentmail_pods_domains_create", Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> AgentMail_Pods_Domains_Create(
        [Description("Pod ID that will own the domain.")] string podId,
        [Description("Domain name, for example example.com.")] string domain,
        [Description("Whether bounce and complaint notifications are sent to inboxes.")] bool feedbackEnabled,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await CreateDomainAsync("create pod domain", $"/v0/pods/{AgentMailHelpers.EscapePath(podId)}/domains", podId, domain, feedbackEnabled, serviceProvider, requestContext, cancellationToken);

    [Description("Delete an AgentMail pod-scoped domain after explicit confirmation using the shared delete confirmation helper.")]
    [McpServerTool(Title = "AgentMail delete pod domain", Name = "agentmail_pods_domains_delete", Destructive = true, OpenWorld = true)]
    public static async Task<CallToolResult?> AgentMail_Pods_Domains_Delete(
        [Description("Pod ID that owns the domain.")] string podId,
        [Description("Domain ID/name to delete from the pod.")] string domainId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            AgentMailHelpers.Require(podId, nameof(podId));
            AgentMailHelpers.Require(domainId, nameof(domainId));
            var client = serviceProvider.GetRequiredService<AgentMailClient>();
            return await requestContext.ConfirmAndDeleteAsync<ConfirmDeleteAgentMailPodDomain>(
                domainId,
                async ct => _ = await client.DeleteAsync($"/v0/pods/{AgentMailHelpers.EscapePath(podId)}/domains/{AgentMailHelpers.EscapePath(domainId)}", ct),
                $"AgentMail pod domain '{domainId}' deleted successfully.",
                cancellationToken);
        });

    private static async Task<CallToolResult?> CreateDomainAsync(
        string operation,
        string path,
        string? podId,
        string domain,
        bool feedbackEnabled,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var (typed, _, _) = await requestContext.Server.TryElicit(new AgentMailCreateDomainRequest
                {
                    PodId = podId,
                    Domain = domain,
                    FeedbackEnabled = feedbackEnabled
                }, cancellationToken);

                if (!string.IsNullOrWhiteSpace(podId)) AgentMailHelpers.Require(typed.PodId, nameof(podId));
                AgentMailHelpers.Require(typed.Domain, nameof(domain));

                var client = serviceProvider.GetRequiredService<AgentMailClient>();
                var response = await client.PostAsync(path, new JsonObject
                {
                    ["domain"] = typed.Domain,
                    ["feedback_enabled"] = typed.FeedbackEnabled
                }, cancellationToken);
                return response.StructuredOrStatus(operation);
            }));
}

[Description("Please confirm the AgentMail create domain request.")]
internal sealed class AgentMailCreateDomainRequest
{
    [JsonPropertyName("pod_id")]
    [Description("Optional pod ID for pod-scoped domain creation.")]
    public string? PodId { get; set; }

    [JsonPropertyName("domain")]
    [Required]
    [Description("Domain name, for example example.com.")]
    public string Domain { get; set; } = string.Empty;

    [JsonPropertyName("feedback_enabled")]
    [Description("Bounce and complaint notifications are sent to inboxes.")]
    public bool FeedbackEnabled { get; set; }
}

[Description("Please confirm the AgentMail verify domain request.")]
internal sealed class AgentMailVerifyDomainRequest
{
    [JsonPropertyName("domain_id")]
    [Required]
    public string DomainId { get; set; } = string.Empty;
}
