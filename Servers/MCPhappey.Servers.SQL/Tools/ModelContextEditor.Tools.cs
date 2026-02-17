using System.ComponentModel;
using DocumentFormat.OpenXml.Wordprocessing;
using MCPhappey.Common.Extensions;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Servers.SQL.Extensions;
using MCPhappey.Servers.SQL.Repositories;
using MCPhappey.Servers.SQL.Tools.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Servers.SQL.Tools;

public static partial class ModelContextEditor
{
    [Description("Add an plugin to a MCP-server")]
    [McpServerTool(Title = "Add an plugin to a MCP-server",
        Destructive = true,
        ReadOnly = false,
        Idempotent = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> ModelContextEditor_AddPlugin(
       [Description("Name of the server")] string serverName,
       [Description("Name of the plugin")] string pluginName,
       IServiceProvider serviceProvider,
       RequestContext<CallToolRequestParams> requestContext,
       CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
    {
        var server = await serviceProvider.GetServer(serverName, cancellationToken);
        var (typed, notAccepted, result) = await requestContext.Server.TryElicit(new McpServerPlugin()
        {
            PluginName = pluginName
        }, cancellationToken);

        var repo = serviceProvider.GetRequiredService<IReadOnlyList<ServerConfig>>();
        HashSet<string> allPlugins = repo.GetAllPlugins();

        if (allPlugins.Any(a => a == typed.PluginName) != true)
        {
            throw new Exception($"Plugin {typed.PluginName} not found");
        }

        if (server.Plugins.Any(a => a.PluginName == typed.PluginName) == true)
        {
            throw new Exception($"Plugin {typed.PluginName} already exists on server {serverName}.");
        }

        var serverRepository = serviceProvider.GetRequiredService<ServerRepository>();

        await serverRepository.AddServerTool(server.Id, typed.PluginName);

        return $"Plugin {typed.PluginName} added to MCP server {serverName}".ToTextCallToolResponse();
    });

    [Description("Add tool output template to MCP-server to show as app widget after tool call.")]
    [McpServerTool(
        Title = "Add tool output template to MCP-server",
        Destructive = true,
        ReadOnly = false,
        Idempotent = false,
        OpenWorld = false)]
    public static async Task<CallToolResult?> ModelContextEditor_AddToolOutputTemplate(
      [Description("Name of the server")] string serverName,
      [Description("Name of the tool")] string toolName,
      [Description("Uri of the outputTemplate")] string outputTemplate,
      IServiceProvider serviceProvider,
      RequestContext<CallToolRequestParams> requestContext,
      CancellationToken cancellationToken = default) =>
      await requestContext.WithExceptionCheck(async () =>
    {
        var server = await serviceProvider.GetServer(serverName, cancellationToken);
        var (typed, notAccepted, _) = await requestContext.Server.TryElicit(new McpServerToolTemplate()
        {
            ToolName = toolName,
            OutputTemplate = outputTemplate
        }, cancellationToken);

        var serverRepository = serviceProvider.GetRequiredService<ServerRepository>();

        await serverRepository.AddToolMetadata(server.Id, typed.ToolName, typed.OutputTemplate);

        return $"Tool output template {typed.OutputTemplate} for tool {typed.ToolName} added to MCP server {serverName}"
            .ToTextCallToolResponse();
    });

    [Description("Get tools from a plugin")]
    [McpServerTool(
        Title = "Get plugin tools",
        ReadOnly = true,
        Idempotent = true,
        Destructive = false,
        OpenWorld = false)]
    public static async Task<CallToolResult?> ModelContextEditor_GetPluginTools(
      [Description("Name of the plugin")] string pluginName,
      IServiceProvider serviceProvider,
      RequestContext<CallToolRequestParams> requestContext) =>
      await requestContext.WithExceptionCheck(async () =>
    {
        var kernel = serviceProvider.GetRequiredService<Kernel>();

        return await Task.FromResult(
                kernel.GetToolsFromType(pluginName, []).ToJsonContentBlock("mcp-editor://tools").ToCallToolResult());
    });

    [Description("Search tools across all plugins by name or description.")]
    [McpServerTool(
     Title = "Search tools",
     ReadOnly = true,
     Idempotent = true,
     Destructive = false,
     OpenWorld = false)]
    public static async Task<CallToolResult?> ModelContextEditor_SearchTools(
     IServiceProvider serviceProvider,
     RequestContext<CallToolRequestParams> requestContext,
     [Description("Search text used to match tool name or description.")]
    string query) =>
     await requestContext.WithExceptionCheck(async () =>
     await requestContext.WithStructuredContent(async () =>
     {
         var repo = serviceProvider.GetRequiredService<IReadOnlyList<ServerConfig>>();
         var kernel = serviceProvider.GetRequiredService<Kernel>();

         var q = query?.Trim().ToLowerInvariant();

         var tools =
             repo.GetAllPlugins()
                 .SelectMany(plugin =>
                     kernel.GetToolsFromType(plugin, [])?
                         .Where(t =>
                             string.IsNullOrWhiteSpace(q) ||
                             (t.ProtocolTool.Name?.ToLowerInvariant().Contains(q) ?? false) ||
                             (t.ProtocolTool.Description?.ToLowerInvariant().Contains(q) ?? false))
                         .Select(t => new
                         {
                             Plugin = plugin,
                             t.ProtocolTool.Name,
                             t.ProtocolTool.Description
                         }) ?? []);

         return await Task.FromResult(new
         {
             query,
             tools
         });
     }));


    [Description("Removes a plugin from a MCP-server")]
    [McpServerTool(Title = "Remove a plugin from an MCP-server",
        Destructive = true,
        ReadOnly = false,
        Idempotent = false,
        OpenWorld = false)]
    public static async Task<CallToolResult> ModelContextEditor_RemovePlugin(
       [Description("Name of the server")] string serverName,
       [Description("Name of the plugin")] string pluginName,
       IServiceProvider serviceProvider,
       RequestContext<CallToolRequestParams> requestContext,
       CancellationToken cancellationToken = default)
    {
        var server = await serviceProvider.GetServer(serverName, cancellationToken);

        if (server.Plugins.Any(a => a.PluginName == pluginName) != true)
        {
            return $"Plugin {pluginName} is not a plugin on server {serverName}.".ToErrorCallToolResponse();
        }

        var (typed, notAccepted, result) = await requestContext.Server.TryElicit(new McpServerPlugin()
        {
            PluginName = pluginName
        }, cancellationToken);

        if (notAccepted != null) return notAccepted;
        if (typed == null) return "Something went wrong".ToErrorCallToolResponse();
        var serverRepository = serviceProvider.GetRequiredService<ServerRepository>();

        await serverRepository.DeleteServerPlugin(server.Id, typed.PluginName);

        return $"Plugin {typed.PluginName} deleted from MCP server {serverName}".ToTextCallToolResponse();
    }

    [Description("Search plugins across all MCP servers by name. Optionally include tools per plugin.")]
    [McpServerTool(
    Title = "Search plugins",
    ReadOnly = true,
    Idempotent = true,
    Destructive = false,
    OpenWorld = false)]
    public static async Task<CallToolResult?> ModelContextEditor_SearchPlugins(
    IServiceProvider serviceProvider,
    RequestContext<CallToolRequestParams> requestContext,
    [Description("Search text used to match plugin name.")]
    string query,
    [Description("When true, also include tools belonging to the plugin.")]
    bool includeTools = false) =>
    await requestContext.WithExceptionCheck(async () =>
    await requestContext.WithStructuredContent(async () =>
    {
        var repo = serviceProvider.GetRequiredService<IReadOnlyList<ServerConfig>>();
        var kernel = serviceProvider.GetRequiredService<Kernel>();

        var q = query?.Trim().ToLowerInvariant();

        var plugins =
            repo.GetAllPlugins()
                .Select(plugin =>
                {
                    var tools = kernel.GetToolsFromType(plugin, [])?
                        .Select(t => new
                        {
                            t.ProtocolTool.Name,
                            t.ProtocolTool.Description
                        })
                        .ToList() ?? [];

                    bool matches =
                        string.IsNullOrWhiteSpace(q)
                        || plugin.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || tools.Any(t =>
                            (t.Name?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                            (t.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));

                    return new
                    {
                        Plugin = plugin,
                        Tools = includeTools ? tools : null,
                        Matches = matches
                    };
                })
                .Where(p => p.Matches)
                .OrderBy(p => p.Plugin)
                .Select(p => new
                {
                    p.Plugin,
                    p.Tools
                });


        return await Task.FromResult(new
        {
            query,
            includeTools,
            plugins
        });
    }));


}

