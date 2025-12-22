using System.ComponentModel;
using System.Text.Json;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graph.Beta.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.SharePoint;

public static class SharePointSearch
{
    [Description("Search Microsoft 365 content and return raw Microsoft search results")]
    [McpServerTool(Name = "sharepoint_search",
        IconSource = SharePointExtensions.ICON_SOURCE,
        Title = "Search Microsoft 365 content raw",
        OpenWorld = false, ReadOnly = true)]
    public static async Task<CallToolResult?> SharePoint_Search(
        [Description("Search query")] string query,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Page size")] int pageSize = 10,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithStructuredContent(async () =>
    {
        var mcpServer = requestContext.Server;
        using var client = await serviceProvider.GetOboGraphClient(mcpServer);

        var entityCombinations = new List<EntityType?[]>
            {
                new EntityType?[] { EntityType.Message, EntityType.ChatMessage },
                new EntityType?[] { EntityType.DriveItem, EntityType.ListItem },
                new EntityType?[] { EntityType.Site },
                new EntityType?[] { EntityType.Person }
            };

        var results = await client.ExecuteSearchAcrossEntities(query, entityCombinations, 2, pageSize, cancellationToken);

        return new Common.Models.SearchResults
        {
            Results = results
                .OfType<SearchHit>()
                .Select(a => a.MapHit())
                .Where(a => !string.IsNullOrEmpty(a.Source))
                .Take(pageSize)
        };
    });

    [Description("Read a Microsoft 365 search result")]
    [McpServerTool(Name = "sharepoint_read_search_result",
        IconSource = SharePointExtensions.ICON_SOURCE,
        Title = "Read a Microsoft 365 search result",
        OpenWorld = false, ReadOnly = true)]
    public static async Task<CallToolResult> SharePoint_ReadSearchResult(
        [Description("Url to the search result item")] string url,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var mcpServer = requestContext.Server;

        var content = await downloadService.ScrapeContentAsync(serviceProvider, mcpServer, url, cancellationToken);
        var text = string.Join("\n\n", content.Select(c => c.Contents.ToString()));

        return text.ToTextContentBlock().ToCallToolResult();
    }


    [Description("Executes a prompt on Microsoft 365 search results")]
    [McpServerTool(Name = "sharepoint_prompt_search_results",
        IconSource = SharePointExtensions.ICON_SOURCE,
        Title = "Executes a prompt on Microsoft 365 search results",
        Destructive = false, Idempotent = true, OpenWorld = false, ReadOnly = true)]
    public static async Task<CallToolResult> SharePoint_PromptSearchResults(
        [Description("Prompt to execute on the search results")] string prompt,
        [Description("List of urls to the search result items")] string urlList,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
    {
        var urls = urlList.Split(['\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries)
                          .Select(u => u.Trim()).Where(u => u.StartsWith("http"));

        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var mcpServer = requestContext.Server;

        var allContent = new List<string>();
        
        foreach (var url in urls)
        {
            try
            {
                var scraped = await downloadService.ScrapeContentAsync(serviceProvider, mcpServer, url, cancellationToken);
                allContent.AddRange(scraped.Select(c => c.Contents.ToString()));
            }
            catch (Exception e)
            {
                return $"Error retrieving file content {e.Message}".ToErrorCallToolResponse();
            }
        }

        var samplingService = serviceProvider.GetRequiredService<SamplingService>();

        var args = new Dictionary<string, JsonElement>()
        {
            ["facts"] = JsonSerializer.SerializeToElement(string.Join("\n\n", allContent)),
            ["question"] = JsonSerializer.SerializeToElement(prompt)
        };

        var result = await samplingService.GetPromptSample(
            serviceProvider, mcpServer, "extract-with-facts", args, "gpt-5-mini",
            metadata: new Dictionary<string, object>()
                {
                    {"openai", new {
                         reasoning = new {
                            effort = "medium"
                         }
                     } },

                },
            cancellationToken: cancellationToken
        );

        return result.Content.ToCallToolResult();
    }
}