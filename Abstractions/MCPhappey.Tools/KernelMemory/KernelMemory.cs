using System.ComponentModel;
using MCPhappey.Auth.Models;
using MCPhappey.Common.Extensions;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.KernelMemory;

public static class KernelMemory
{
    private static Common.Models.SearchResultContentBlock ToSearchResult(this string citation)
      => new()
      {
          Text = citation
      };

    private static Common.Models.SearchResult ToSearchResult(this Citation citation)
        => new()
        {
            Title = citation.SourceName,
            Source = citation.SourceUrl!,
            Content = citation.Partitions.Select(a => a.Text.ToSearchResult()),
            Citations = new()
        };

    [Description("Search Microsoft Kernel Memory")]
    [McpServerTool(Title = "Search Microsoft kernel memory",
        Name = "kernel_memory_search",
        ReadOnly = true)]
    public static async Task<CallToolResult?> KernelMemory_Search(
        [Description("Search query")]
        string query,
        [Description("Kernel memory index")]
        string index,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Minimum relevance")]
        double? minRelevance = 0,
        [Description("Limit the number of results")]
        int? limit = 10,
        CancellationToken cancellationToken = default) => 
        await requestContext.WithStructuredContent(async () =>
    {
        var memory = serviceProvider.GetRequiredService<IKernelMemory>();
        var appSettings = serviceProvider.GetService<OAuthSettings>();
        if (appSettings?.ClientId.Equals(index, StringComparison.OrdinalIgnoreCase) == true)
        {
            throw new UnauthorizedAccessException();
        }

        var answer = await memory.SearchAsync(query,
            index,
            minRelevance: minRelevance ?? 0,
            limit: limit ?? int.MaxValue,
            cancellationToken: cancellationToken);

        return new SearchResults()
        {
            Results = answer.Results.Select(b => b.ToSearchResult())
        };
    });

    [Description("Ask Microsoft Kernel Memory")]
    [McpServerTool(Title = "Ask Microsoft kernel memory",
        ReadOnly = true)]
    public static async Task<CallToolResult> KernelMemory_Ask(
        [Description("Question prompt")]
        string prompt,
        [Description("Kernel memory index")]
        string index,
        IServiceProvider serviceProvider,
        [Description("Minimum relevance")]
        double? minRelevance = 0,
        CancellationToken cancellationToken = default)
    {
        var memory = serviceProvider.GetRequiredService<IKernelMemory>();
        var appSettings = serviceProvider.GetService<OAuthSettings>();
        if (appSettings?.ClientId.Equals(index, StringComparison.OrdinalIgnoreCase) == true)
        {
            return "Not authorized".ToErrorCallToolResponse();
        }

        var answer = await memory.AskAsync(prompt, index, minRelevance: minRelevance ?? 0,
            cancellationToken: cancellationToken);

        return new
        {
            answer.Question,
            answer.NoResult,
            Text = answer.Result,
            RelevantSources = answer.RelevantSources.Select(b => new
            {
                b.SourceUrl,
                b.Partitions.OrderByDescending(y => y.LastUpdate).FirstOrDefault()?.LastUpdate,
                Citations = b.Partitions.Select(z => z.Text)
            })
        }.ToJsonContentBlock(index)
                    .ToCallToolResult();
    }

    [Description("List available Microsoft Kernel Memory indexes")]
    [McpServerTool(Title = "List kernel memory indexes",
        Idempotent = true,
        ReadOnly = true,
        OpenWorld = false)]
    public static async Task<CallToolResult> KernelMemory_ListIndexes(
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        var memory = serviceProvider.GetService<IKernelMemory>();
        var appSettings = serviceProvider.GetService<OAuthSettings>();
        ArgumentNullException.ThrowIfNull(memory);
        var indexes = await memory.ListIndexesAsync(cancellationToken: cancellationToken);

        return indexes.Where(a => a.Name != appSettings?.ClientId)
            .ToJsonContentBlock("kernel://list")
            .ToCallToolResult();
    }
}

