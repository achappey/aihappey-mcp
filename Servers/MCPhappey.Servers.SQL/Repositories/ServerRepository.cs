using MCPhappey.Servers.SQL.Context;
using MCPhappey.Servers.SQL.Models;
using Microsoft.EntityFrameworkCore;

namespace MCPhappey.Servers.SQL.Repositories;

public class ServerRepository(McpDatabaseContext databaseContext)
{
    public async Task<ResourceTemplate?> GetResourceTemplate(int id) =>
        await databaseContext.ResourceTemplates
                .FirstOrDefaultAsync(r => r.Id == id);

    public async Task<Resource?> GetResource(int id) =>
        await databaseContext.Resources
            .FirstOrDefaultAsync(r => r.Id == id);

    public async Task<Server?> GetServer(string name, CancellationToken cancellationToken = default) =>
        await databaseContext.Servers
            .Include(r => r.Prompts)
            .ThenInclude(r => r.Arguments)
            .Include(r => r.Resources)
            .Include(r => r.ResourceTemplates)
            .Include(r => r.Owners)
            .Include(r => r.Plugins)
            .Include(r => r.Groups)
            .Include(r => r.Icons)
            .ThenInclude(r => r.Icon)
            .ThenInclude(r => r.Sizes)
            .ThenInclude(r => r.Size)
            .Include(r => r.Prompts)
            .ThenInclude(r => r.Icons)
            .ThenInclude(r => r.Icon)
            .ThenInclude(r => r.Sizes)
            .ThenInclude(r => r.Size)
            .Include(r => r.Resources)
            .ThenInclude(r => r.Icons)
            .ThenInclude(r => r.Icon)
            .ThenInclude(r => r.Sizes)
            .ThenInclude(r => r.Size)
            .FirstOrDefaultAsync(r => r.Name == name, cancellationToken);

    public async Task<List<Server>> GetServers(CancellationToken cancellationToken = default) =>
        await databaseContext.Servers.AsNoTracking()
            .Include(r => r.Prompts)
            .ThenInclude(r => r.Arguments)
            .Include(r => r.Resources)
            .Include(r => r.Owners)
            .Include(r => r.Plugins)
            .Include(r => r.Groups)
            .Include(r => r.ResourceTemplates)
            .Include(r => r.Icons)
            .ThenInclude(r => r.Icon)
            .ThenInclude(r => r.Sizes)
            .ThenInclude(r => r.Size)
            .Include(r => r.Prompts)
            .ThenInclude(r => r.Icons)
            .ThenInclude(r => r.Icon)
            .ThenInclude(r => r.Sizes)
            .ThenInclude(r => r.Size)
            .Include(r => r.Resources)
            .ThenInclude(r => r.Icons)
            .ThenInclude(r => r.Icon)
            .ThenInclude(r => r.Sizes)
            .ThenInclude(r => r.Size)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

    public async Task<List<Resource>> GetResources(string serverName, CancellationToken cancellationToken = default) =>
        await databaseContext.Resources.AsNoTracking()
            .Where(a => a.Server.Name == serverName)
            .OrderByDescending(a => a.Priority)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

    public async Task<List<ResourceTemplate>> GetResourceTemplates(string serverName, CancellationToken cancellationToken = default) =>
        await databaseContext.ResourceTemplates.AsNoTracking()
            .Where(a => a.Server.Name == serverName)
            .OrderByDescending(a => a.Priority)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

    public async Task<Server> UpdateServer(Server server)
    {
        databaseContext.Servers.Update(server);
        await databaseContext.SaveChangesAsync();

        return server;
    }

    public async Task<ResourceTemplate> UpdateResourceTemplate(ResourceTemplate resource)
    {
        databaseContext.ResourceTemplates.Update(resource);
        await databaseContext.SaveChangesAsync();

        return resource;
    }

    public async Task<Resource> UpdateResource(Resource resource)
    {
        databaseContext.Resources.Update(resource);
        await databaseContext.SaveChangesAsync();

        return resource;
    }

    public async Task<Prompt> UpdatePrompt(Prompt prompt)
    {
        databaseContext.Prompts.Update(prompt);
        await databaseContext.SaveChangesAsync();

        return prompt;
    }

    public async Task<PromptArgument> UpdatePromptArgument(PromptArgument promptArgument)
    {
        databaseContext.PromptArguments.Update(promptArgument);
        await databaseContext.SaveChangesAsync();

        return promptArgument;
    }

    public async Task<Server> CreateServer(Server server, CancellationToken cancellationToken)
    {
        await databaseContext.Servers.AddAsync(server, cancellationToken);
        await databaseContext.SaveChangesAsync(cancellationToken);

        return server;
    }



    public async Task DeletePromptArgument(int id)
    {
        var item = await databaseContext.PromptArguments.FirstOrDefaultAsync(a => a.Id == id);

        if (item != null)
        {
            databaseContext.PromptArguments.Remove(item);
            await databaseContext.SaveChangesAsync();
        }
    }


    public async Task DeleteServerPlugin(int serverId, string ownerId)
    {
        var item = await databaseContext.Plugins.FirstOrDefaultAsync(a => a.PluginName == ownerId && a.ServerId == serverId);

        if (item != null)
        {
            databaseContext.Plugins.Remove(item);
            await databaseContext.SaveChangesAsync();
        }
    }

    public async Task DeleteServerOwner(int serverId, string ownerId)
    {
        var item = await databaseContext.ServerOwners.FirstOrDefaultAsync(a => a.Id == ownerId && a.ServerId == serverId);

        if (item != null)
        {
            databaseContext.ServerOwners.Remove(item);
            await databaseContext.SaveChangesAsync();
        }
    }

    public async Task DeleteServerGroup(int serverId, string ownerId)
    {
        var item = await databaseContext.ServerGroups.FirstOrDefaultAsync(a => a.Id == ownerId && a.ServerId == serverId);

        if (item != null)
        {
            databaseContext.ServerGroups.Remove(item);
            await databaseContext.SaveChangesAsync();
        }
    }

    public async Task DeleteServer(int id)
    {
        var item = await databaseContext.Servers.FirstOrDefaultAsync(a => a.Id == id);

        if (item != null)
        {
            databaseContext.Servers.Remove(item);
            await databaseContext.SaveChangesAsync();
        }
    }

    public async Task DeletePrompt(int id)
    {
        var item = await databaseContext.Prompts.FirstOrDefaultAsync(a => a.Id == id);

        if (item != null)
        {
            databaseContext.Prompts.Remove(item);
            await databaseContext.SaveChangesAsync();
        }
    }

    public async Task DeleteResource(int id)
    {
        var item = await databaseContext.Resources.FirstOrDefaultAsync(a => a.Id == id);

        if (item != null)
        {
            databaseContext.Resources.Remove(item);
            await databaseContext.SaveChangesAsync();

        }
    }

    public async Task DeleteResource(string uri)
    {
        var item = await databaseContext.Resources.FirstOrDefaultAsync(a => a.Uri == uri);

        if (item != null)
        {
            databaseContext.Resources.Remove(item);
            await databaseContext.SaveChangesAsync();

        }
    }

    public async Task DeleteResourceTemplate(int id)
    {
        var item = await databaseContext.ResourceTemplates.FirstOrDefaultAsync(a => a.Id == id);

        if (item != null)
        {
            databaseContext.ResourceTemplates.Remove(item);
            await databaseContext.SaveChangesAsync();
        }
    }

    public async Task DeleteTool(int serverId, string name)
    {
        var item = await databaseContext.Plugins.FirstOrDefaultAsync(a => a.ServerId == serverId && a.PluginName == name);

        if (item != null)
        {
            databaseContext.Plugins.Remove(item);
            await databaseContext.SaveChangesAsync();
        }
    }

    public async Task AddServerGroup(int serverId, string owner)
    {

        await databaseContext.ServerGroups.AddAsync(new()
        {
            Id = owner,
            ServerId = serverId
        });

        await databaseContext.SaveChangesAsync();
    }

    public async Task AddServerOwner(int serverId, string owner)
    {

        await databaseContext.ServerOwners.AddAsync(new()
        {
            Id = owner,
            ServerId = serverId
        });

        await databaseContext.SaveChangesAsync();
    }

    public async Task AddServerTool(int serverId, string toolName)
    {
        await databaseContext.Plugins.AddAsync(new ServerPlugin()
        {
            PluginName = toolName,
            ServerId = serverId
        });

        await databaseContext.SaveChangesAsync();
    }

    public async Task<Resource> AddServerResource(int serverId, string uri,
        string name,
        string? description = null,
        string? title = null,
        string? mimeType = null,
        float? priority = null,
        bool? assistantAudience = null,
        bool? userAudience = null)
    {
        var item = await databaseContext.Resources.AddAsync(new()
        {
            Uri = uri,
            Name = name,
            Description = description,
            Title = title,
            MimeType = mimeType,
            Priority = priority,
            AssistantAudience = assistantAudience,
            UserAudience = userAudience,
            ServerId = serverId
        });

        await databaseContext.SaveChangesAsync();

        return item.Entity;
    }

    public async Task<ToolMetadata> AddToolMetadata(int serverId, string toolName,
            string? outputTemplate = null)
    {
        var item = await databaseContext.ToolMetadata.AddAsync(new()
        {
            ServerId = serverId,
            ToolName = toolName,
            OutputTemplate = outputTemplate
        });

        await databaseContext.SaveChangesAsync();

        return item.Entity;
    }


    public async Task RemoveToolMetadata(int serverId, string toolName)
    {
        var item = await databaseContext.ToolMetadata.FirstOrDefaultAsync(a => a.ServerId == serverId && a.ToolName == toolName);

        if (item != null)
        {
            databaseContext.ToolMetadata.Remove(item);
            await databaseContext.SaveChangesAsync();
        }
    }

    public async Task<ResourceTemplate> AddServerResourceTemplate(int serverId,
        string uri,
        string? name = null,
        string? description = null,
        string? title = null,
        float? priority = null,
        bool? assistantAudience = null,
        bool? userAudience = null)
    {
        var item = await databaseContext.ResourceTemplates.AddAsync(new()
        {
            TemplateUri = uri,
            Name = name ?? string.Empty,
            Description = description,
            Title = title,
            Priority = priority,
            AssistantAudience = assistantAudience,
            UserAudience = userAudience,
            ServerId = serverId
        });

        await databaseContext.SaveChangesAsync();

        return item.Entity;
    }

    public async Task<Prompt> AddServerPrompt(int serverId,
        string prompt,
        string name,
        string? description = null,
        string? title = null,
        IEnumerable<PromptArgument>? arguments = null)
    {
        var item = await databaseContext.Prompts.AddAsync(new Prompt()
        {
            PromptTemplate = prompt,
            Name = name ?? string.Empty,
            Description = description,
            Title = title,
            Arguments = arguments?.ToList() ?? [],
            ServerId = serverId,
        });

        await databaseContext.SaveChangesAsync();

        return item.Entity;
    }
}
