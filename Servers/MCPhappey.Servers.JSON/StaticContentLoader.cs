using System.Text.Json;
using MCPhappey.Common.Models;
using ModelContextProtocol.Protocol;

namespace MCPhappey.Servers.JSON;

public static class StaticContentLoader
{
    public static IEnumerable<ServerConfig> GetServers(this string basePath, string? tenantName = null)
    {
        var servers = new List<ServerConfig>();

        foreach (var subDir in Directory.GetDirectories(basePath, "*", SearchOption.AllDirectories))
        {
            var serverJsonFiles = Directory.GetFiles(subDir, "*Server.json", SearchOption.TopDirectoryOnly);
            if (serverJsonFiles.Length == 0)
                continue;

            foreach (var file in serverJsonFiles)
            {
                var jsonContent = File.ReadAllText(file);

                var serverObj = JsonSerializer.Deserialize<Server>(jsonContent);
                if (serverObj == null)
                    continue;

                ServerConfig serverConfig = new()
                {
                    Server = serverObj,
                    SourceType = ServerSourceType.Static,
                };

                if (!string.IsNullOrEmpty(tenantName))
                {
                    foreach (var key in serverConfig.Server.OBO?.Keys?.ToList() ?? [])
                    {
                        var value = serverConfig.Server.OBO![key];
                        if (value != null && value.Contains("{tenantName}"))
                        {
                            serverConfig.Server.OBO[key] = value.Replace("{tenantName}", tenantName);
                        }
                    }

                }

                //serverConfig.Server.OBO.
                // Check for Tools.json, Prompts.json, Resources.json in the same subDir
                var promptsFile = Path.Combine(subDir, "Prompts.json");
                var resourcesFile = Path.Combine(subDir, "Resources.json");
                var resourceTemplatesFile = Path.Combine(subDir, "ResourceTemplates.json");

                if (File.Exists(promptsFile))
                {
                    serverConfig.PromptList = JsonSerializer.Deserialize<PromptTemplates>(File.ReadAllText(promptsFile));

                    if (serverConfig.PromptList != null)
                    {
                        foreach (var p in serverConfig.PromptList.Prompts
                                .Where(p => p.Template.Icons?.Any() != true))
                        {
                            p.Template.Icons = serverConfig.Server.ServerInfo.Icons?.ToList();
                        }

                        serverObj.Capabilities.Prompts = new(); // not null
                    }


                }

                if (serverObj.Plugins?.Any() == true)
                {
                    serverObj.Capabilities.Tools = new();
                }

                // If Resources.json exists, mark as not null
                if (File.Exists(resourcesFile))
                {
                    serverConfig.ResourceList = JsonSerializer.Deserialize<ListResourcesResult>(
                            File.ReadAllText(resourcesFile));

                    serverConfig.ResourceList = new ListResourcesResult()
                    {
                        Resources = serverConfig.ResourceList?.Resources
                            .OrderByDescending(a => a.Annotations?.Priority)
                            .ToList() ?? []
                    };

                    foreach (var p in serverConfig.ResourceList.Resources
                            .Where(p => p.Icons?.Any() != true))
                    {
                        p.Icons = serverConfig.Server.ServerInfo.Icons?.ToList();
                    }

                    serverObj.Capabilities.Resources = new();
                }

                if (File.Exists(resourceTemplatesFile))
                {
                    serverConfig.ResourceTemplateList = JsonSerializer.Deserialize<ListResourceTemplatesResult>(
                            File.ReadAllText(resourceTemplatesFile));

                    serverConfig.ResourceTemplateList = new ListResourceTemplatesResult()
                    {
                        ResourceTemplates = serverConfig.ResourceTemplateList?.ResourceTemplates
                            .OrderByDescending(a => a.Annotations?.Priority)
                            .ToList() ?? []
                    };

                    foreach (var p in serverConfig.ResourceTemplateList.ResourceTemplates
                        .Where(p => p.Icons?.Any() != true))
                    {
                        p.Icons = serverConfig.Server.ServerInfo.Icons?.ToList();
                    }

                    serverObj.Capabilities.Resources = new();
                }

                servers.Add(serverConfig);
            }
        }

        return servers;
    }
}