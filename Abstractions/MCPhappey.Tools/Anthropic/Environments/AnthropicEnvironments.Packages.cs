using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Anthropic.Environments;

public static partial class AnthropicEnvironments
{
    [Description("Add a single apt package entry to an Anthropic environment.")]
    [McpServerTool(Title = "Add apt package to Anthropic Environment", Name = "anthropic_environments_add_apt_package", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> AnthropicEnvironments_AddAptPackage(
        [Description("Environment ID.")] string environmentId,
        [Description("A single apt package entry, optionally including its version syntax.")] string package,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await AddPackageAsync("apt", environmentId, package, serviceProvider, requestContext, cancellationToken);

    [Description("Remove a single apt package entry from an Anthropic environment after explicit typed confirmation.")]
    [McpServerTool(Title = "Remove apt package from Anthropic Environment", Name = "anthropic_environments_remove_apt_package", ReadOnly = false, OpenWorld = false, Destructive = true)]
    public static async Task<CallToolResult?> AnthropicEnvironments_RemoveAptPackage(
        [Description("Environment ID.")] string environmentId,
        [Description("The exact apt package entry to remove.")] string package,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await RemovePackageAsync("apt", environmentId, package, serviceProvider, requestContext, cancellationToken);

    [Description("Add a single cargo package entry to an Anthropic environment.")]
    [McpServerTool(Title = "Add cargo package to Anthropic Environment", Name = "anthropic_environments_add_cargo_package", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> AnthropicEnvironments_AddCargoPackage(
        [Description("Environment ID.")] string environmentId,
        [Description("A single cargo package entry, optionally including its version syntax.")] string package,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await AddPackageAsync("cargo", environmentId, package, serviceProvider, requestContext, cancellationToken);

    [Description("Remove a single cargo package entry from an Anthropic environment after explicit typed confirmation.")]
    [McpServerTool(Title = "Remove cargo package from Anthropic Environment", Name = "anthropic_environments_remove_cargo_package", ReadOnly = false, OpenWorld = false, Destructive = true)]
    public static async Task<CallToolResult?> AnthropicEnvironments_RemoveCargoPackage(
        [Description("Environment ID.")] string environmentId,
        [Description("The exact cargo package entry to remove.")] string package,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await RemovePackageAsync("cargo", environmentId, package, serviceProvider, requestContext, cancellationToken);

    [Description("Add a single gem package entry to an Anthropic environment.")]
    [McpServerTool(Title = "Add gem package to Anthropic Environment", Name = "anthropic_environments_add_gem_package", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> AnthropicEnvironments_AddGemPackage(
        [Description("Environment ID.")] string environmentId,
        [Description("A single gem package entry, optionally including its version syntax.")] string package,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await AddPackageAsync("gem", environmentId, package, serviceProvider, requestContext, cancellationToken);

    [Description("Remove a single gem package entry from an Anthropic environment after explicit typed confirmation.")]
    [McpServerTool(Title = "Remove gem package from Anthropic Environment", Name = "anthropic_environments_remove_gem_package", ReadOnly = false, OpenWorld = false, Destructive = true)]
    public static async Task<CallToolResult?> AnthropicEnvironments_RemoveGemPackage(
        [Description("Environment ID.")] string environmentId,
        [Description("The exact gem package entry to remove.")] string package,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await RemovePackageAsync("gem", environmentId, package, serviceProvider, requestContext, cancellationToken);

    [Description("Add a single go package entry to an Anthropic environment.")]
    [McpServerTool(Title = "Add go package to Anthropic Environment", Name = "anthropic_environments_add_go_package", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> AnthropicEnvironments_AddGoPackage(
        [Description("Environment ID.")] string environmentId,
        [Description("A single go package entry, optionally including its version syntax.")] string package,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await AddPackageAsync("go", environmentId, package, serviceProvider, requestContext, cancellationToken);

    [Description("Remove a single go package entry from an Anthropic environment after explicit typed confirmation.")]
    [McpServerTool(Title = "Remove go package from Anthropic Environment", Name = "anthropic_environments_remove_go_package", ReadOnly = false, OpenWorld = false, Destructive = true)]
    public static async Task<CallToolResult?> AnthropicEnvironments_RemoveGoPackage(
        [Description("Environment ID.")] string environmentId,
        [Description("The exact go package entry to remove.")] string package,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await RemovePackageAsync("go", environmentId, package, serviceProvider, requestContext, cancellationToken);

    [Description("Add a single npm package entry to an Anthropic environment.")]
    [McpServerTool(Title = "Add npm package to Anthropic Environment", Name = "anthropic_environments_add_npm_package", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> AnthropicEnvironments_AddNpmPackage(
        [Description("Environment ID.")] string environmentId,
        [Description("A single npm package entry, optionally including its version syntax.")] string package,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await AddPackageAsync("npm", environmentId, package, serviceProvider, requestContext, cancellationToken);

    [Description("Remove a single npm package entry from an Anthropic environment after explicit typed confirmation.")]
    [McpServerTool(Title = "Remove npm package from Anthropic Environment", Name = "anthropic_environments_remove_npm_package", ReadOnly = false, OpenWorld = false, Destructive = true)]
    public static async Task<CallToolResult?> AnthropicEnvironments_RemoveNpmPackage(
        [Description("Environment ID.")] string environmentId,
        [Description("The exact npm package entry to remove.")] string package,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await RemovePackageAsync("npm", environmentId, package, serviceProvider, requestContext, cancellationToken);

    [Description("Add a single pip package entry to an Anthropic environment.")]
    [McpServerTool(Title = "Add pip package to Anthropic Environment", Name = "anthropic_environments_add_pip_package", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> AnthropicEnvironments_AddPipPackage(
        [Description("Environment ID.")] string environmentId,
        [Description("A single pip package entry, optionally including its version syntax.")] string package,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await AddPackageAsync("pip", environmentId, package, serviceProvider, requestContext, cancellationToken);

    [Description("Remove a single pip package entry from an Anthropic environment after explicit typed confirmation.")]
    [McpServerTool(Title = "Remove pip package from Anthropic Environment", Name = "anthropic_environments_remove_pip_package", ReadOnly = false, OpenWorld = false, Destructive = true)]
    public static async Task<CallToolResult?> AnthropicEnvironments_RemovePipPackage(
        [Description("Environment ID.")] string environmentId,
        [Description("The exact pip package entry to remove.")] string package,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await RemovePackageAsync("pip", environmentId, package, serviceProvider, requestContext, cancellationToken);

    private static async Task<CallToolResult?> AddPackageAsync(
        string packageManager,
        string environmentId,
        string package,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var (typed, _, _) = await requestContext.Server.TryElicit(new AnthropicEnvironmentPackageMutationRequest
                {
                    EnvironmentId = environmentId,
                    PackageManager = packageManager,
                    Package = package
                }, cancellationToken);

                var normalizedEnvironmentId = NormalizeEnvironmentId(typed.EnvironmentId);
                ValidatePackageManager(typed.PackageManager);
                var normalizedPackage = NormalizePackageEntry(typed.Package);

                var current = await GetEnvironmentAsync(serviceProvider, normalizedEnvironmentId, cancellationToken);
                var packages = EnsurePackagesNode(current);
                var values = EnsurePackageArray(packages, typed.PackageManager);
                if (!ContainsValue(values, normalizedPackage))
                    values.Add(normalizedPackage);

                var config = CreateConfigPatch();
                config["packages"] = packages;

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

    private static async Task<CallToolResult?> RemovePackageAsync(
        string packageManager,
        string environmentId,
        string package,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var normalizedEnvironmentId = NormalizeEnvironmentId(environmentId);
                ValidatePackageManager(packageManager);
                var normalizedPackage = NormalizePackageEntry(package);
                await AnthropicManagedAgentsHttp.ConfirmDeleteAsync<AnthropicDeleteEnvironment>(requestContext.Server, $"{normalizedEnvironmentId}:{packageManager}:{normalizedPackage}", cancellationToken);

                var current = await GetEnvironmentAsync(serviceProvider, normalizedEnvironmentId, cancellationToken);
                var packages = EnsurePackagesNode(current);
                var values = EnsurePackageArray(packages, packageManager);
                if (!RemoveValue(values, normalizedPackage))
                    throw new ValidationException($"Package '{normalizedPackage}' was not found in '{packageManager}' for environment '{normalizedEnvironmentId}'.");

                var config = CreateConfigPatch();
                config["packages"] = packages;

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
}
