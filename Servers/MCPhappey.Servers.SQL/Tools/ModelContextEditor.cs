using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using DocumentFormat.OpenXml.Wordprocessing;
using MCPhappey.Auth.Extensions;
using MCPhappey.Common.Extensions;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Servers.SQL.Extensions;
using MCPhappey.Servers.SQL.Models;
using MCPhappey.Servers.SQL.Repositories;
using MCPhappey.Servers.SQL.Tools.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Servers.SQL.Tools;

public static partial class ModelContextEditor
{
    [Description("Clone a MCP-server")]
    [McpServerTool(Title = "Clone a MCP-server",
        Destructive = false,
        ReadOnly = false,
        Idempotent = false,
        OpenWorld = false)]
    public static async Task<CallToolResult?> ModelContextEditor_CloneServer(
     [Description("Name of the server to clone")]
        string cloneServerName,
     IServiceProvider serviceProvider,
     RequestContext<CallToolRequestParams> requestContext,
     [Description("Name of the new server")]
        string? newServerName = null,
     CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
    {
        var serverConfigs = serviceProvider.GetRequiredService<IReadOnlyList<ServerConfig>>();
        var sourceServerConfig = serverConfigs.FirstOrDefault(a => a.Server.ServerInfo.Name == cloneServerName);
        var allCustomServers = await serviceProvider.GetServers(cancellationToken);
        var customServer = allCustomServers.FirstOrDefault(a => a.Name == cloneServerName);
        var userId = serviceProvider.GetUserId();

        if (userId == null)
            throw new Exception("No user found");

        if (sourceServerConfig?.SourceType == ServerSourceType.Dynamic)
        {
            if (customServer?.Owners?.Select(a => a.Id).Contains(userId) != true)
            {
                throw new Exception("Only editors can clone a server");
            }
        }
        else if (sourceServerConfig == null)
        {
            if (customServer?.Owners?.Select(a => a.Id).Contains(userId) != true)
            {
                throw new Exception("Only editors can clone a server");
            }
        }

        var (typedResult, notAccepted, result) = await requestContext.Server.TryElicit(new CloneMcpServer
        {
            Name = newServerName ?? string.Empty,
        }, cancellationToken);

        if (typedResult == null) throw new Exception("Something went wrong");

        var serverRepository = serviceProvider.GetRequiredService<ServerRepository>();

        // Helper: Standardize server object
        async Task<object?> GetSourceServer()
        {
            if (sourceServerConfig?.SourceType == ServerSourceType.Static)
            {
                var s = sourceServerConfig;
                return new
                {
                    s.Server.ServerInfo.Name,
                    s.Server.ServerInfo.Title,
                    s.Server.ServerInfo.WebsiteUrl,
                    s.Server.ServerInfo.Description,
                    s.Server.Instructions,
                    Secured = true,
                    Prompts = s.PromptList?.Prompts?.Select(p => new
                    {
                        p.Prompt,
                        p.Template.Name,
                        p.Template.Description,
                        Arguments = p.Template.Arguments?.Select(a => new SQL.Models.PromptArgument
                        {
                            Name = a.Name,
                            Description = a.Description,
                            Required = a.Required
                        }).ToList()
                    }).ToList(),
                    Resources = s.ResourceList?.Resources?.Select(r => new
                    {
                        r.Uri,
                        r.Name,
                        r.Description
                    }).ToList(),
                    ResourceTemplates = s.ResourceTemplateList?.ResourceTemplates?.Select(r => new
                    {
                        Uri = r.UriTemplate,
                        r.Name,
                        r.Description
                    }).ToList(),
                    Tools = s.ToolList?.ToList()
                };
            }
            else
            {
                var s = await serviceProvider.GetServer(cloneServerName, cancellationToken);
                return new
                {
                    s.Name,
                    s.Instructions,
                    s.Title,
                    s.Description,
                    s.WebsiteUrl,
                    s.Secured,
                    Prompts = s.Prompts?.Select(p => new
                    {
                        Prompt = p.PromptTemplate,
                        p.Name,
                        p.Description,
                        Arguments = p.Arguments?.Select(a => new SQL.Models.PromptArgument
                        {
                            Name = a.Name,
                            Description = a.Description,
                            Required = a.Required
                        }).ToList()
                    }).ToList(),
                    Resources = s.Resources?.Select(r => new
                    {
                        r.Uri,
                        r.Name,
                        r.Description
                    }).ToList(),
                    ResourceTemplates = s.ResourceTemplates?.Select(r => new
                    {
                        Uri = r.TemplateUri,
                        r.Name,
                        r.Description
                    }).ToList(),
                    Tools = s.Plugins?.Select(t => t.PluginName).ToList()
                };
            }
        }

        // Main logic
        var source = await GetSourceServer();
        if (source == null)
            throw new Exception("Source server not found");

        // Dynamically resolve properties with dynamic
        dynamic src = source;

        var dbServer = await serverRepository.CreateServer(new SQL.Models.Server
        {
            Name = typedResult.Name.Slugify(),
            Instructions = src.Instructions,
            Title = src.Title,
            Description = src.Description,
            WebsiteUrl = src.WebsiteUrl,
            Secured = src.Secured,
            Owners = [new ServerOwner { Id = userId }]
        }, cancellationToken);

        // Helper functions to reduce repetition
        async Task AddPromptsAsync()
        {
            if (src.Prompts == null) return;
            foreach (var p in src.Prompts)
            {
                await serverRepository.AddServerPrompt(
                    dbServer.Id,
                    p.Prompt,
                    p.Name,
                    p.Description,
                    p.Arguments
                );
            }
        }
        async Task AddResourcesAsync()
        {
            if (src.Resources == null) return;
            foreach (var r in src.Resources)
            {
                await serverRepository.AddServerResource(
                    dbServer.Id,
                    r.Uri,
                    r.Name,
                    r.Description
                );
            }
        }

        async Task AddResourceTemplatesAsync()
        {
            if (src.ResourceTemplates == null) return;
            foreach (var r in src.ResourceTemplates)
            {
                await serverRepository.AddServerResourceTemplate(
                    dbServer.Id,
                    r.Uri,
                    r.Name,
                    r.Description
                );
            }
        }

        async Task AddToolsAsync()
        {
            if (src.Tools == null) return;
            foreach (var t in src.Tools)
            {
                await serverRepository.AddServerTool(
                    dbServer.Id,
                    t
                );
            }
        }

        // Perform all cloning
        await AddPromptsAsync();
        await AddResourcesAsync();
        await AddResourceTemplatesAsync();
        await AddToolsAsync();

        var fullServer = await serviceProvider.GetServer(typedResult.Name.Slugify(), cancellationToken);

        return new
        {
            fullServer.Name,
            Owners = fullServer.Owners.Select(z => z.Id),
            fullServer.Secured,
            SecurityGroups = fullServer.Groups.Select(z => z.Id)
        };
    }));

    [Description("Create a new MCP-server")]
    [McpServerTool(Title = "Create a new MCP-server",
        Destructive = false,
        ReadOnly = false,
        Idempotent = false,
        OpenWorld = false)]
    public static async Task<CallToolResult?> ModelContextEditor_CreateServer(
        [Description("Server name"), MaxLength(150), MinLength(3), RegularExpression("^[a-zA-Z0-9._-]+$")]
        string serverName,
        [Description("Clear human-readable explanation of server functionality."), MaxLength(100), MinLength(1)]
        string serverDescription,
        [Description("Human-readable title or display name for the MCP server."), MaxLength(100), MinLength(1)]
        string serverTitle,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Server instructions.")]
        string? instructions = null,
        [Description("Optional URL to the server's homepage, documentation, or project website.")]
        string? websiteUrl = null,
        [Description("Enable to exclude the MCP server from the MCP registry.")]
        bool? hidden = false,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
    {
        var serverRepository = serviceProvider.GetRequiredService<ServerRepository>();
        var userId = serviceProvider.GetUserId();
        if (userId == null) throw new Exception("No user found");

        await requestContext.Server.SendMessageNotificationAsync($"Checking name availability: {serverName}", LoggingLevel.Info, cancellationToken);

        var serverExists = await serviceProvider.ServerExists(serverName, cancellationToken);
        if (serverExists) throw new Exception("Servername already in use");

        var (typedResult, notAccepted, result) = await requestContext.Server.TryElicit(new NewMcpServer()
        {
            Name = serverName,
            WebsiteUrl = string.IsNullOrEmpty(websiteUrl) ? null : new Uri(websiteUrl),
            Title = serverTitle,
            Hidden = hidden,
            Description = serverDescription,
            Instructions = instructions,
            Secured = true,
        }, cancellationToken);

        await requestContext.Server.SendMessageNotificationAsync($"Creating server: {typedResult.Name.Slugify()}", LoggingLevel.Info, cancellationToken);

        var server = await serverRepository.CreateServer(new SQL.Models.Server()
        {
            Name = typedResult.Name.Slugify(),
            Instructions = typedResult.Instructions,
            Title = typedResult.Title,
            Description = typedResult.Description,
            WebsiteUrl = typedResult.WebsiteUrl?.ToString(),
            Secured = typedResult.Secured ?? true,
            Hidden = typedResult.Hidden,
            Owners = [new ServerOwner() {
                       Id = userId
                    }]
        }, cancellationToken);

        await requestContext.Server.SendMessageNotificationAsync($"Server created: {server.Id}", LoggingLevel.Info, cancellationToken);

        return new
        {
            server.Name,
            Owners = server.Owners.Select(z => z.Id),
            server.Secured,
            server.WebsiteUrl,
            server.Hidden,
            SecurityGroups = server.Groups.Select(z => z.Id)
        };
    }));

    [Description("Updates a MCP-server")]
    [McpServerTool(Title = "Update an MCP-server",
        Destructive = true,
        ReadOnly = false,
        Idempotent = false,
        OpenWorld = false)]
    public static async Task<CallToolResult?> ModelContextEditor_UpdateServer(
      [Description("Name of the server")] string serverName,
      IServiceProvider serviceProvider,
      RequestContext<CallToolRequestParams> requestContext,
        [Description("New server title"), MaxLength(100), MinLength(1)]
        string? serverTitle = null,
        [Description("New description for the server"), MaxLength(100), MinLength(1)]
        string? serverDescription = null,
        [Description("New website url")]
        string? websiteUrl = null,
        [Description("Updated instructions for the server")]
        string? instructions = null,
        [Description("If the server should be hidden")]
        bool? hidden = null,
      CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
    {
        var server = await serviceProvider.GetServer(serverName, cancellationToken);
        var finalUrl = !string.IsNullOrEmpty(websiteUrl)
            ? websiteUrl
            : server.WebsiteUrl;

        var (typed, notAccepted, result) = await requestContext.Server.TryElicit(new UpdateMcpServer()
        {
            Name = serverName,
            WebsiteUrl = string.IsNullOrEmpty(finalUrl) ? null : new Uri(finalUrl),
            Title = serverTitle ?? server.Title,
            Description = serverDescription ?? server.Description,
            Instructions = instructions ?? server.Instructions,
            Hidden = hidden ?? server.Hidden
        }, cancellationToken);

        if (!string.IsNullOrEmpty(typed.Name))
        {
            server.Name = typed.Name.Slugify();
        }

        server.Instructions = typed.Instructions;
        server.Hidden = typed.Hidden;
        server.Title = typed.Title;
        server.Description = typed.Description;
        server.WebsiteUrl = typed.WebsiteUrl?.ToString();

        var serverRepository = serviceProvider.GetRequiredService<ServerRepository>();
        var updated = await serverRepository.UpdateServer(server);

        return new
        {
            server.Name,
            Owners = server.Owners.Select(z => z.Id),
            server.Secured,
            server.Description,
            server.WebsiteUrl,
            SecurityGroups = server.Groups.Select(z => z.Id),
            server.Hidden
        };
    }));

    [Description("Deletes an MCP-server")]
    [McpServerTool(Title = "Delete an MCP-server",
        Destructive = true,
        ReadOnly = false,
        Idempotent = false,
        OpenWorld = false)]
    public static async Task<CallToolResult?> ModelContextEditor_DeleteServer(
    [Description("Name of the server")] string serverName,
    RequestContext<CallToolRequestParams> requestContext,
    IServiceProvider serviceProvider,
    CancellationToken cancellationToken = default) =>
    await requestContext.WithExceptionCheck(async () =>
{
    var repo = serviceProvider.GetRequiredService<ServerRepository>();

    // One-liner again
    return await requestContext.ConfirmAndDeleteAsync<DeleteMcpServer>(
        serverName,
        async _ =>
        {
            var server = await serviceProvider.GetServer(serverName, cancellationToken);
            await repo.DeleteServer(server.Id);
        },
        "Server deleted.",
        cancellationToken);
});
}

