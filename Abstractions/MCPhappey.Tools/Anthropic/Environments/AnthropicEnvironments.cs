using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Anthropic.Environments;

public static partial class AnthropicEnvironments
{
    private const string BaseUrl = $"{AnthropicManagedAgentsHttp.ApiBaseUrl}/v1/environments";
    private const string CloudConfigType = "cloud";
    private const string NetworkingTypeLimited = "limited";
    private const string NetworkingTypeUnrestricted = "unrestricted";
    private const string PackagesConfigType = "packages";

    [Description("Please confirm to delete: {0}")]
    public sealed class AnthropicDeleteEnvironment : IHasName
    {
        [JsonPropertyName("name")]
        [Description("Confirm deletion.")]
        public string Name { get; set; } = string.Empty;
    }

    [Description("Create an Anthropic environment with only flat scalar fields. Use dedicated environment mutation tools for metadata, networking, allowed hosts, and package managers.")]
    [McpServerTool(
        Title = "Create Anthropic Environment",
        Name = "anthropic_environments_create",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = false)]
    public static async Task<CallToolResult?> AnthropicEnvironments_Create(
        [Description("Human-readable name for the environment.")] string name,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional description.")] string? description = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var (typed, _, _) = await requestContext.Server.TryElicit(new AnthropicCreateEnvironmentRequest
                {
                    Name = name,
                    Description = description
                }, cancellationToken);

                var normalizedName = NormalizeEnvironmentName(typed.Name);
                ValidateEnvironmentDescription(typed.Description);

                var body = new JsonObject
                {
                    ["name"] = normalizedName
                };

                SetStringIfProvided(body, "description", typed.Description);

                return await AnthropicManagedAgentsHttp.SendAsync(
                    serviceProvider,
                    HttpMethod.Post,
                    BaseUrl,
                    body,
                
                    cancellationToken);
            }));

    [Description("Update only the flat scalar fields on an Anthropic environment. Use dedicated environment mutation tools for metadata, networking, allowed hosts, and package managers.")]
    [McpServerTool(
        Title = "Update Anthropic Environment",
        Name = "anthropic_environments_update",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = false)]
    public static async Task<CallToolResult?> AnthropicEnvironments_Update(
        [Description("Environment ID to update.")] string environmentId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional updated name. Omit to preserve the current name.")] string? name = null,
        [Description("Optional updated description. Provide an empty string to clear.")] string? description = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var (typed, _, _) = await requestContext.Server.TryElicit(new AnthropicUpdateEnvironmentRequest
                {
                    EnvironmentId = environmentId,
                    Name = name,
                    Description = description
                }, cancellationToken);

                var normalizedEnvironmentId = NormalizeEnvironmentId(typed.EnvironmentId);
                string? normalizedName = null;

                if (typed.Name is not null)
                    normalizedName = NormalizeEnvironmentName(typed.Name);

                ValidateEnvironmentDescription(typed.Description);

                var body = new JsonObject();
                if (normalizedName is not null) body["name"] = normalizedName;
                if (typed.Description is not null) body["description"] = typed.Description;

                if (body.Count == 0)
                    throw new ValidationException("At least one scalar field to update is required.");

                return await UpdateEnvironmentAsync(
                    serviceProvider,
                    normalizedEnvironmentId,
                    body,
                    cancellationToken);
            }));

    [Description("Archive an Anthropic environment. The archive request is confirmed through elicitation before execution.")]
    [McpServerTool(
        Title = "Archive Anthropic Environment",
        Name = "anthropic_environments_archive",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> AnthropicEnvironments_Archive(
        [Description("Environment ID to archive.")] string environmentId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var (typed, _, _) = await requestContext.Server.TryElicit(new AnthropicArchiveEnvironmentRequest
                {
                    EnvironmentId = environmentId
                }, cancellationToken);

                var normalizedEnvironmentId = NormalizeEnvironmentId(typed.EnvironmentId);

                return await AnthropicManagedAgentsHttp.SendAsync(
                    serviceProvider,
                    HttpMethod.Post,
                    $"{BaseUrl}/{Uri.EscapeDataString(normalizedEnvironmentId)}/archive",
                    null,                  
                    cancellationToken: cancellationToken);
            }));

    [Description("Delete an Anthropic environment after explicit typed confirmation. The API deletion response is returned as structured JSON content.")]
    [McpServerTool(
        Title = "Delete Anthropic Environment",
        Name = "anthropic_environments_delete",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> AnthropicEnvironments_Delete(
        [Description("Environment ID to delete.")] string environmentId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var normalizedEnvironmentId = NormalizeEnvironmentId(environmentId);
                await AnthropicManagedAgentsHttp.ConfirmDeleteAsync<AnthropicDeleteEnvironment>(requestContext.Server, 
                    normalizedEnvironmentId, cancellationToken);

                return await AnthropicManagedAgentsHttp.SendAsync(
                    serviceProvider,
                    HttpMethod.Delete,
                    $"{BaseUrl}/{Uri.EscapeDataString(normalizedEnvironmentId)}",
                    null,                 
                    cancellationToken);
            }));
}
