using MCPhappey.Common;
using MCPhappey.Common.Extensions;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Servers.SQL.Extensions;
using MCPhappey.Servers.SQL.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.DataFormats;
using Microsoft.SemanticKernel;
using ModelContextProtocol.Server;

namespace MCPhappey.Servers.SQL.Providers;

public class McpEditorScraper(IEnumerable<IContentDecoder> contentDecoders, List<ServerIcon> defaultIcons) : IContentScraper
{
    public bool SupportsHost(ServerConfig serverConfig, string url)
        => new Uri(url).Scheme.Equals("mcp-editor", StringComparison.OrdinalIgnoreCase);

    public async Task<IEnumerable<FileItem>?> GetContentAsync(McpServer mcpServer,
        IServiceProvider serviceProvider, string url, CancellationToken cancellationToken = default)
    {
        var tokenService = serviceProvider.GetService<HeaderProvider>();
        if (string.IsNullOrEmpty(tokenService?.Bearer))
        {
            return null;
        }

        if (url.Equals("mcp-editor://statistics"))
        {
            var serverRepository = serviceProvider.GetRequiredService<ServerRepository>();
            var servers = await serverRepository.GetServers(cancellationToken);
            var totalServers = servers.Count;
            var totalPrompts = servers.Sum(a => a.Prompts?.Count ?? 0);
            var totalResources = servers.Sum(a => a.Resources?.Count ?? 0);
            var totalResourceTemplates = servers.Sum(a => a.ResourceTemplates?.Count ?? 0);

            var allOwnerIds = servers.SelectMany(s => s.Owners)
                             .Select(u => u.Id)
                             .Distinct()
                             .ToList();

            var totalUniqueOwners = allOwnerIds.Count;
            var serversPerOwner = servers
                    .SelectMany(s => s.Owners.Select(u => new { ServerId = s.Id, OwnerId = u.Id }))
                    .GroupBy(x => x.OwnerId)
                    .Select(g => g.Count())
                    .ToList();

            var avgServersPerOwner = totalUniqueOwners == 0 ? 0 : (double)totalServers / totalUniqueOwners;

            // (optional nerd stats)
            var minServersPerOwner = serversPerOwner.Count == 0 ? 0 : serversPerOwner.Min();
            var maxServersPerOwner = serversPerOwner.Count == 0 ? 0 : serversPerOwner.Max();

            // Avoid division by zero, obviously
            var avgPromptsPerServer = totalServers == 0 ? 0 : (double)totalPrompts / totalServers;
            var avgResourcesPerServer = totalServers == 0 ? 0 : (double)totalResources / totalServers;
            var avgTemplatesPerServer = totalServers == 0 ? 0 : (double)totalResourceTemplates / totalServers;

            var stats = new
            {
                TotalServers = totalServers,
                TotalPrompts = totalPrompts,
                TotalResources = totalResources,
                TotalResourceTemplates = totalResourceTemplates,

                AveragePromptsPerServer = avgPromptsPerServer,
                AverageResourcesPerServer = avgResourcesPerServer,
                AverageResourceTemplatesPerServer = avgTemplatesPerServer,

                TotalUniqueOwners = totalUniqueOwners,
                AverageServersPerOwner = avgServersPerOwner,
                MinServersPerOwner = minServersPerOwner,
                MaxServersPerOwner = maxServersPerOwner
            };

            return [stats.ToFileItem(url)];
        }

        if (url.Equals("mcp-editor://plugins"))
        {
            var repo = serviceProvider.GetRequiredService<IReadOnlyList<ServerConfig>>();

            return [repo.GetAllPlugins()
                .ToFileItem(url)];
        }

        if (url.Equals("mcp-editor://assemblies"))
        {
            var binDir = AppDomain.CurrentDomain.BaseDirectory;
            var dllInfo = Directory.GetFiles(binDir, "*.dll")
                .Select(f =>
                {
                    try
                    {
                        var asm = System.Reflection.AssemblyName.GetAssemblyName(f);
                        return (asm.Name, asm.Version);
                    }
                    catch
                    {
                        return (Name: null, Version: null);
                    }
                })
                .Where(a => a.Name != null
                    && !a.Name.StartsWith("System")
                    && (!a.Name.StartsWith("Microsoft") || a.Name.StartsWith("Microsoft.Graph")))
                .Select(a => $"{a.Name}, {a.Version}")
                .ToList();

            return [dllInfo.ToFileItem(url)];
        }

        if (url.Equals("mcp-editor://decoders"))
        {
            var bestDecoder = contentDecoders
                .Select(a => a.GetType().Namespace);
                
            return [bestDecoder.ToFileItem(url)];
        }


        if (url.Equals("mcp-editor://servers"))
        {
            var servers = await serviceProvider.GetServers(cancellationToken);
            var userServers = servers.Select(z => z.ToMcpServer(defaultIcons));

            return [userServers.ToFileItem(url)];
        }

        if (url.StartsWith("mcp-editor://decoders/", StringComparison.OrdinalIgnoreCase))
        {
            // Extract mimetype from URI
            var mimetype = url["mcp-editor://decoders/".Length..];

            var supportedDecoders = contentDecoders
                .ByMimeType(mimetype)
                .Select(a => a.GetType().Namespace)
                .ToList();

            return [supportedDecoders.ToFileItem(url)];
        }
        // Optionally: return empty for not matched


        if (url.StartsWith("mcp-editor://plugins/", StringComparison.OrdinalIgnoreCase)
            && url.EndsWith("/tools", StringComparison.OrdinalIgnoreCase))
        {
            // Extract plugin name
            string pluginName = GetServerNameFromEditorUrl(url);
            var kernel = serviceProvider.GetRequiredService<Kernel>();
            return [(kernel.GetToolsFromType(pluginName ?? string.Empty, []) ?? []).ToFileItem(url)];
        }

        string serverName = GetServerNameFromEditorUrl(url);
        var server = await serviceProvider.GetServer(serverName, cancellationToken);

        if (url.EndsWith("/prompts", StringComparison.OrdinalIgnoreCase))
        {
            return [.. server.Prompts
                .Select(z => z.ToPromptTemplate())
                .Select(p => p.ToFileItem(url))];
        }

        if (url.EndsWith("/resources", StringComparison.OrdinalIgnoreCase))
        {
            return [.. server.Resources
                .Select(z => z.ToResource())
                .Select(r => r.ToFileItem(url))];
        }

        if (url.EndsWith("/resourceTemplates", StringComparison.OrdinalIgnoreCase))
        {
            return [.. server.ResourceTemplates
                .Select(z => z.ToResourceTemplate())
                .Select(r => r.ToFileItem(url))];
        }

        string itemType = GetServerNameFromEditorUrl(url);
        string itemName = GetServerNameFromEditorUrl(url);

        return itemType switch
        {
            "resources" => [.. server.Resources.Where(a => a.Name == itemName)
                .Select(z => z.ToResource())
                .Select(r => r.ToFileItem(url))],
            "prompts" => [.. server.Prompts.Where(a => a.Name == itemName)
                .Select(z => z.ToPromptTemplate())
                .Select(r => r.ToFileItem(url))],
            "resourceTemplates" => [.. server.ResourceTemplates.Where(a => a.Name == itemName)
                .Select(z => z.ToResourceTemplate())
                .Select(r => r.ToFileItem(url))],
            _ => throw new Exception("Uri not supported"),
        };
    }

    private static string GetServerNameFromEditorUrl(string url)
    {
        var uri = new Uri(url);
        // Segments: ["/", "server/", "{serverName}/", ...]
        return uri.Segments.Length >= 3 ? uri.Segments[1].TrimEnd('/') : "";
    }
}
