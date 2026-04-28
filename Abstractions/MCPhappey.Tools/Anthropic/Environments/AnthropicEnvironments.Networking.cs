using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Anthropic.Environments;

public static partial class AnthropicEnvironments
{
    [Description("Set an Anthropic environment to unrestricted networking.")]
    [McpServerTool(
        Title = "Set Anthropic Environment unrestricted networking",
        Name = "anthropic_environments_set_unrestricted_networking",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = false)]
    public static async Task<CallToolResult?> AnthropicEnvironments_SetUnrestrictedNetworking(
        [Description("Environment ID.")] string environmentId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var (typed, _, _) = await requestContext.Server.TryElicit(new AnthropicArchiveEnvironmentRequest
                {
                    EnvironmentId = environmentId,
                }, cancellationToken);

                var normalizedEnvironmentId = NormalizeEnvironmentId(typed.EnvironmentId);
                var config = CreateConfigPatch();
                config["networking"] = CreateUnrestrictedNetworkingNode();

                var body = new JsonObject
                {
                    ["config"] = config
                };

                return await UpdateEnvironmentAsync(
                    serviceProvider,
                    normalizedEnvironmentId,
                    body,
                    cancellationToken);
            }));

    [Description("Set an Anthropic environment to limited networking and explicitly configure the policy toggles. Existing allowed hosts are preserved when the environment is already limited.")]
    [McpServerTool(
        Title = "Set Anthropic Environment limited networking",
        Name = "anthropic_environments_set_limited_networking",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = false)]
    public static async Task<CallToolResult?> AnthropicEnvironments_SetLimitedNetworking(
        [Description("Environment ID.")] string environmentId,
        [Description("Whether outbound access to configured MCP servers is allowed beyond the allowed host list.")] bool allowMcpServers,
        [Description("Whether outbound access to public package registries is allowed beyond the allowed host list.")] bool allowPackageManagers,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var (typed, _, _) = await requestContext.Server.TryElicit(new AnthropicEnvironmentLimitedNetworkingRequest
                {
                    EnvironmentId = environmentId,
                    AllowMcpServers = allowMcpServers,
                    AllowPackageManagers = allowPackageManagers
                }, cancellationToken);

                var normalizedEnvironmentId = NormalizeEnvironmentId(typed.EnvironmentId);
                var current = await GetEnvironmentAsync(serviceProvider, normalizedEnvironmentId, cancellationToken);
                var networking = GetLimitedNetworkingOrDefault(current);
                var allowedHosts = GetStringValues(networking["allowed_hosts"] as JsonArray, "allowed_hosts");

                var config = CreateConfigPatch();
                config["networking"] = CreateLimitedNetworkingNode(typed.AllowMcpServers, typed.AllowPackageManagers, allowedHosts);

                var body = new JsonObject
                {
                    ["config"] = config
                };

                return await UpdateEnvironmentAsync(
                    serviceProvider,
                    normalizedEnvironmentId,
                    body,
                    cancellationToken);
            }));

    [Description("Add a single allowed host to an Anthropic environment. If the environment is currently unrestricted, this tool switches it to limited networking with both policy toggles defaulting to false.")]
    [McpServerTool(
        Title = "Add allowed host to Anthropic Environment",
        Name = "anthropic_environments_add_allowed_host",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = false)]
    public static async Task<CallToolResult?> AnthropicEnvironments_AddAllowedHost(
        [Description("Environment ID.")] string environmentId,
        [Description("Hostname or IP address to allow.")] string host,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var (typed, _, _) = await requestContext.Server.TryElicit(new AnthropicEnvironmentAllowedHostMutationRequest
                {
                    EnvironmentId = environmentId,
                    Host = host
                }, cancellationToken);

                var normalizedEnvironmentId = NormalizeEnvironmentId(typed.EnvironmentId);
                var normalizedHost = NormalizeAllowedHost(typed.Host);
                var current = await GetEnvironmentAsync(serviceProvider, normalizedEnvironmentId, cancellationToken);
                var networking = GetLimitedNetworkingOrDefault(current);
                var allowedHosts = GetStringValues(networking["allowed_hosts"] as JsonArray, "allowed_hosts");

                if (!allowedHosts.Contains(normalizedHost, StringComparer.OrdinalIgnoreCase))
                    allowedHosts.Add(normalizedHost);

                var config = CreateConfigPatch();
                config["networking"] = CreateLimitedNetworkingNode(
                    GetBooleanOrDefault(networking, "allow_mcp_servers"),
                    GetBooleanOrDefault(networking, "allow_package_managers"),
                    allowedHosts);

                var body = new JsonObject
                {
                    ["config"] = config
                };

                return await UpdateEnvironmentAsync(
                    serviceProvider,
                    normalizedEnvironmentId,
                    body,
                    cancellationToken);
            }));

    [Description("Remove a single allowed host from an Anthropic environment after explicit typed confirmation.")]
    [McpServerTool(
        Title = "Remove allowed host from Anthropic Environment",
        Name = "anthropic_environments_remove_allowed_host",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> AnthropicEnvironments_RemoveAllowedHost(
        [Description("Environment ID.")] string environmentId,
        [Description("Hostname or IP address to remove.")] string host,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var normalizedEnvironmentId = NormalizeEnvironmentId(environmentId);
                var normalizedHost = NormalizeAllowedHost(host);
                await AnthropicManagedAgentsHttp.ConfirmDeleteAsync<AnthropicDeleteEnvironment>(requestContext.Server, $"{normalizedEnvironmentId}:{normalizedHost}", cancellationToken);

                var current = await GetEnvironmentAsync(serviceProvider, normalizedEnvironmentId, cancellationToken);
                var networking = GetExistingLimitedNetworking(current);
                var allowedHosts = GetStringValues(networking["allowed_hosts"] as JsonArray, "allowed_hosts");
                if (!allowedHosts.RemoveAll(existing => string.Equals(existing, normalizedHost, StringComparison.OrdinalIgnoreCase)).Equals(0))
                {
                    var config = CreateConfigPatch();
                    config["networking"] = CreateLimitedNetworkingNode(
                        GetBooleanOrDefault(networking, "allow_mcp_servers"),
                        GetBooleanOrDefault(networking, "allow_package_managers"),
                        allowedHosts);

                    var body = new JsonObject
                    {
                        ["config"] = config
                    };

                    return await UpdateEnvironmentAsync(
                        serviceProvider,
                        normalizedEnvironmentId,
                        body,
                        cancellationToken);
                }

                throw new ValidationException($"Allowed host '{normalizedHost}' was not found on environment '{normalizedEnvironmentId}'.");
            }));
}
