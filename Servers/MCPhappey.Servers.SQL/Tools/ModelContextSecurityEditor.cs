using System.ComponentModel;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Servers.SQL.Extensions;
using MCPhappey.Servers.SQL.Repositories;
using MCPhappey.Servers.SQL.Tools.Models;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Servers.SQL.Tools;

public static partial class ModelContextSecurityEditor
{
    [Description("Adds an owner to a MCP-server")]
    [McpServerTool(
        Title = "Add an owner to an MCP-server",
        Destructive = true,
        ReadOnly = false,
        Idempotent = false,
        OpenWorld = false)]
    public static async Task<CallToolResult?> ModelContextSecurityEditor_AddOwner(
       [Description("Name of the server")] string serverName,
       [Description("User id of new owner")] string ownerUserId,
       IServiceProvider serviceProvider,
       RequestContext<CallToolRequestParams> requestContext,
       CancellationToken cancellationToken = default) =>
       await requestContext.WithExceptionCheck(async () =>
    {
        var serverRepository = serviceProvider.GetRequiredService<ServerRepository>();
        var server = await serviceProvider.GetServer(serverName, cancellationToken);
        using var graphClient = await serviceProvider.GetOboGraphClient(requestContext.Server);
        var user = await graphClient.Users[ownerUserId].GetAsync();

        var (typed, notAccepted, _) = await requestContext.Server.TryElicit(new McpServerOwner()
        {
            UserId = ownerUserId
        }, cancellationToken);

        if (server.Owners.Any(a => a.Id == typed.UserId) == true)
        {
            return $"Owner {typed.UserId} already exists on server {serverName}.".ToErrorCallToolResponse();
        }

        if (!typed.UserId.Equals(ownerUserId, StringComparison.OrdinalIgnoreCase))
        {
            return $"Owner {typed.UserId} does not match {ownerUserId}.".ToErrorCallToolResponse();
        }

        var owner = await graphClient.Users[typed.UserId].GetAsync();

        await serverRepository.AddServerOwner(server.Id, owner?.Id!);

        return $"Owner {typed.UserId} added to MCP server {serverName}".ToTextCallToolResponse();
    });

    [Description("Removes an owner from a MCP-server")]
    [McpServerTool(
        Title = "Remove an owner from an MCP-server",
        Destructive = true,
        ReadOnly = false,
        Idempotent = false,
        OpenWorld = false)]
    public static async Task<CallToolResult?> ModelContextSecurityEditor_RemoveOwner(
       [Description("Name of the server")] string serverName,
       [Description("User id of owner")] string ownerUserId,
       IServiceProvider serviceProvider,
       RequestContext<CallToolRequestParams> requestContext,
       CancellationToken cancellationToken = default) =>
       await requestContext.WithExceptionCheck(async () =>
    {
        var serverRepository = serviceProvider.GetRequiredService<ServerRepository>();
        var server = await serviceProvider.GetServer(serverName, cancellationToken);

        if (server.Owners.Count() <= 1)
            throw new Exception("MCP servers need at least 1 owner.");

        if (server.Owners.Any(a => a.Id == ownerUserId) != true)
        {
            throw new Exception($"User {ownerUserId} is not an owner on server {serverName}.");
        }

        var (typed, notAccepted, _) = await requestContext.Server.TryElicit(new McpServerOwner()
        {
            UserId = ownerUserId
        }, cancellationToken);

        if (!typed.UserId.Equals(ownerUserId, StringComparison.OrdinalIgnoreCase))
        {
            throw new Exception($"Owner {typed.UserId} does not match {ownerUserId}.");
        }

        await serverRepository.DeleteServerOwner(server.Id, typed.UserId);

        return $"Owner {typed.UserId} deleted from MCP server {serverName}".ToTextCallToolResponse();
    });

    [Description("Updates the security of a MCP-server")]
    [McpServerTool(
        Title = "Update the security of an MCP-server",
        Destructive = true,
        ReadOnly = false,
        Idempotent = false,
        OpenWorld = false)]
    public static async Task<CallToolResult?> ModelContextSecurityEditor_UpdateServerSecurity(
      [Description("Name of the server")] string serverName,
      IServiceProvider serviceProvider,
      RequestContext<CallToolRequestParams> requestContext,
      CancellationToken cancellationToken = default) =>
      await requestContext.WithExceptionCheck(async () =>
      await requestContext.WithStructuredContent(async () =>
    {
        var serverRepository = serviceProvider.GetRequiredService<ServerRepository>();
        var server = await serviceProvider.GetServer(serverName, cancellationToken);
        var (typed, notAccepted, _) = await requestContext.Server.TryElicit(new UpdateMcpServerSecurity()
        {
            Secured = server.Secured
        }, cancellationToken);

        if (typed.Secured.HasValue)
        {
            server.Secured = typed.Secured.Value;
        }

        var updated = await serverRepository.UpdateServer(server);

        return new
        {
            server.Name,
            Owners = server.Owners.Select(z => z.Id),
            server.Secured,
            SecurityGroups = server.Groups.Select(z => z.Id)
        };
    }));

    [Description("Adds a security group to a MCP-server")]
    [McpServerTool(
        Title = "Add a security group to an MCP-server",
        Destructive = true,
        ReadOnly = false,
        Idempotent = false,
        OpenWorld = false)]
    public static async Task<CallToolResult?> ModelContextSecurityEditor_AddSecurityGroup(
        [Description("Name of the server")] string serverName,
        [Description("Entra id of security group to add")] string securityGroupId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
    {
        var serverRepository = serviceProvider.GetRequiredService<ServerRepository>();
        var server = await serviceProvider.GetServer(serverName, cancellationToken);

        var (typed, notAccepted, _) = await requestContext.Server.TryElicit(new McpSecurityGroup()
        {
            GroupId = securityGroupId
        }, cancellationToken);

        if (server.Groups.Any(g => g.Id == typed.GroupId))
            throw new Exception($"Group {typed.GroupId} already assigned.");

        if (!typed.GroupId.Equals(securityGroupId, StringComparison.OrdinalIgnoreCase))
        {
            throw new Exception($"Group {typed.GroupId} does not match {securityGroupId}.");
        }

        await serverRepository.AddServerGroup(server.Id, typed.GroupId);

        return $"Security group {typed.GroupId} added to MCP server {serverName}".ToTextCallToolResponse();
    });

    [Description("Removes a security group from a MCP-server")]
    [McpServerTool(
        Title = "Remove a security group from an MCP-server",
        Destructive = true,
        ReadOnly = false,
        Idempotent = false,
        OpenWorld = false)]
    public static async Task<CallToolResult?> ModelContextSecurityEditor_RemoveSecurityGroup(
        [Description("Name of the server")] string serverName,
        [Description("Entra id of security group to remove")] string securityGroupId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
    {
        var serverRepository = serviceProvider.GetRequiredService<ServerRepository>();
        var server = await serviceProvider.GetServer(serverName, cancellationToken);
        var (typed, notAccepted, _) = await requestContext.Server.TryElicit(new McpSecurityGroup()
        {
            GroupId = securityGroupId
        }, cancellationToken);

        if (!server.Groups.Any(g => g.Id == typed.GroupId))
            throw new Exception($"Group {typed.GroupId} not assigned.");

        if (!typed.GroupId.Equals(securityGroupId, StringComparison.OrdinalIgnoreCase))
        {
            throw new Exception($"Group {typed.GroupId} does not match {securityGroupId}.");
        }

        await serverRepository.DeleteServerGroup(server.Id, typed.GroupId);

        return $"Security group {typed.GroupId} removed from MCP server {serverName}".ToTextCallToolResponse();
    });
}

