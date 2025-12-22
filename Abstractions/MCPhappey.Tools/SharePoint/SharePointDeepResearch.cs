using System.ComponentModel;
using System.Text.Json;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graph.Beta.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.SharePoint;

public static class SharePointDeepResearch
{
    private static string? GetTitle(ListItem li)
    {
        if (li.Fields?.AdditionalData != null &&
            li.Fields.AdditionalData.TryGetValue("Title", out var t) &&
            t != null)
            return t.ToString();

        return li.Id;
    }

    // -------------------------------
    // MAPPERS (reuse your existing map)
    // -------------------------------
    private static dynamic? MapToSearchResult(SearchHit? hit)
    {
        var (title, url, id) = hit?.Resource switch
        {
            DriveItem d => (d.Name, d.WebUrl, d.Id),
            ListItem li => (GetTitle(li), li.WebUrl, li.Id),
            Message m => (m.Subject, m.WebLink, m.Id),
            _ => (null, null, (hit?.Resource as Entity)?.Id)
        };

        if (id == null || url == null)
            return null;

        return new
        {
            id,
            title = title ?? "",
            url
        };
    }

    // =========================================================
    //  SEARCH (Deep Research spec)
    // =========================================================
    [Description("Deep Research compliant SharePoint search")]
    [McpServerTool(
        Name = "search",
        IconSource = SharePointExtensions.ICON_SOURCE,
        Title = "SharePoint Deep Research Search",
        OpenWorld = false,
        ReadOnly = true,
        Idempotent = true)]
    public static async Task<CallToolResult?> Search(
        [Description("Search query")] string query,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Maximum number of results")] int pageSize = 10,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async (graphClient) =>
    {
        var entityCombinations = new List<EntityType?[]>
            {
                new EntityType?[] { EntityType.DriveItem, EntityType.ListItem },
                new EntityType?[] { EntityType.Message, EntityType.ChatMessage },
                new EntityType?[] { EntityType.Site }
            };

        var hits = await graphClient.ExecuteSearchAcrossEntities(
            query,
            entityCombinations,
            maxConcurrency: 2,
            pageSize: pageSize,
            cancellationToken: cancellationToken);

        // Deep Research schema: { "results": [ { id, title, url } ] }
        var results = hits
            .Select(MapToSearchResult)
            .Where(r => r != null && !string.IsNullOrEmpty(r?.url))
            .Take(pageSize)
            .Select(r => new
            {
                // IMPORTANT: we use URL as ID so fetch(id) can treat it as URL
                id = r!.url!,
                title = r.title ?? r.url!,
                url = r.url!
            })
            .ToList();

        var payload = new
        {
            results
        };

        var json = JsonSerializer.Serialize(payload, JsonSerializerOptions.Web);

        return json
            .ToTextContentBlock()
            .ToCallToolResult();
    }));

    // =========================================================
    //  FETCH (Deep Research spec)
    // =========================================================
    [Description("Deep Research fetch: returns full text of a SharePoint result")]
    [McpServerTool(
        Name = "fetch",
        IconSource = SharePointExtensions.ICON_SOURCE,
        Title = "SharePoint Deep Research Fetch",
        OpenWorld = false,
        ReadOnly = true,
        Idempotent = true)]
    public static async Task<CallToolResult?> Fetch(
        [Description("The result ID returned by search (here: the canonical URL)")] string id,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
    {
        var mcpServer = requestContext.Server;
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();

        // In search we set id = url, so here id *is* the URL
        var url = id;

        // EXACTLY the same pattern as SharePoint_ReadSearchResult
        var scrapedContent = await downloadService.ScrapeContentAsync(
            serviceProvider,
            mcpServer,
            url,
            cancellationToken);

        var fullText = string.Join(
            "\n\n",
            scrapedContent.Select(c => c.Contents?.ToString() ?? string.Empty));

        // Fallback title = URL (we don't have title from this path)
        var doc = new
        {
            id,
            title = url,
            text = fullText,
            url
        };

        var json = JsonSerializer.Serialize(doc, JsonSerializerOptions.Web);

        return json
            .ToTextContentBlock()
            .ToCallToolResult();
    });

}