using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MCPhappey.Auth.Models;
using MCPhappey.Common.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MCPhappey.Core.Extensions;

namespace MCPhappey.Tools.OpenMemory;

public static class OpenMemory
{
    [Description("Save a personal user memory")]
    [McpServerTool(Title = "Save memory",
        OpenWorld = false)]
    public static async Task<CallToolResult?> OpenMemory_SaveMemory(
        [Description("Memory to save")]
        string memory,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
    {
        var kernelMemory = serviceProvider.GetRequiredService<IKernelMemory>();
        var appSettings = serviceProvider.GetService<OAuthSettings>();
        ArgumentNullException.ThrowIfNull(memory);

        var (typed, notAccepted, result) = await requestContext.Server.TryElicit(
                new OpenMemoryNewMemory
                {
                    Memory = memory,
                },
                cancellationToken);

        var answer = await kernelMemory.ImportTextAsync(typed.Memory, index: appSettings?.ClientId!,
            tags: serviceProvider.ToTagCollection(),
            cancellationToken: cancellationToken);

        return answer.ToTextCallToolResponse();
    });

    [Description("Delete a personal user memory")]
    [McpServerTool(Title = "Delete memory",
        Destructive = true,
        OpenWorld = false)]
    public static async Task<CallToolResult> OpenMemory_DeleteMemory(
        [Description("Id of the memory")]
        string memoryId,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        var kernelMemory = serviceProvider.GetRequiredService<IKernelMemory>();
        var appSettings = serviceProvider.GetRequiredService<OAuthSettings>();

        await kernelMemory.DeleteDocumentAsync(memoryId, index: appSettings?.ClientId!,
            cancellationToken: cancellationToken);

        return "Memory deleted".ToTextCallToolResponse();
    }

    [Description("Ask a question to personal user memory")]
    [McpServerTool(Title = "Ask memory",
        OpenWorld = false,
        ReadOnly = true)]
    public static async Task<CallToolResult> OpenMemory_AskMemory(
        [Description("Question prompt")]
        string prompt,
        IServiceProvider serviceProvider,
        [Description("Minimum relevance")]
        double? minRelevance = 0,
        CancellationToken cancellationToken = default)
    {
        var appSettings = serviceProvider.GetService<OAuthSettings>();
        ArgumentException.ThrowIfNullOrWhiteSpace(appSettings?.ClientId);
        var memory = serviceProvider.GetRequiredService<IKernelMemory>();

        var answer = await memory.AskAsync(prompt, appSettings.ClientId, minRelevance: minRelevance ?? 0,
            filter: serviceProvider.ToMemoryFilter(),
            cancellationToken: cancellationToken);

        return new
        {
            answer.Question,
            answer.NoResult,
            Text = answer.Result,
            RelevantMemories = answer.RelevantSources.Select(b => new
            {
                Date = b.Partitions.OrderByDescending(y => y.LastUpdate).FirstOrDefault()?.LastUpdate,
                Memory = string.Join("\n\n", b.Partitions.Select(z => z.Text))
            })
        }.ToJsonContentBlock(appSettings.ClientId)
            .ToCallToolResult();
    }

    [Description("Search personal user memories with a prompt")]
    [McpServerTool(Title = "Search memories",
        Idempotent = true,
        OpenWorld = false,
        ReadOnly = true)]
    public static async Task<CallToolResult> OpenMemory_SearchMemories(
      [Description("Question prompt")]
        string prompt,
      IServiceProvider serviceProvider,
      [Description("Minimum relevance")]
        double? minRelevance = 0,
      [Description("Limit items")]
        int? limit = 10,
      CancellationToken cancellationToken = default)
    {
        var appSettings = serviceProvider.GetService<OAuthSettings>();
        ArgumentException.ThrowIfNullOrWhiteSpace(appSettings?.ClientId);
        var memory = serviceProvider.GetRequiredService<IKernelMemory>();
        var answer = await memory.SearchAsync(prompt, appSettings.ClientId, minRelevance: minRelevance ?? 0,
            filter: serviceProvider.ToMemoryFilter(),
            limit: limit ?? int.MaxValue,
            cancellationToken: cancellationToken);

        return answer.Results.Select(a => new
        {
            id = a.DocumentId,
            memory = string.Join("\n\n", a.Partitions.Select(t => t.Text))
        })
        .ToJsonContentBlock(appSettings.ClientId)
        .ToCallToolResult();
    }

    [Description("List personal user memories")]
    [McpServerTool(Title = "List memories",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false)]
    public static async Task<CallToolResult> OpenMemory_ListMemories(
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        var memory = serviceProvider.GetService<IKernelMemory>();

        ArgumentNullException.ThrowIfNull(memory);
        var appSettings = serviceProvider.GetService<OAuthSettings>();
        ArgumentException.ThrowIfNullOrWhiteSpace(appSettings?.ClientId);
        var indexes = await memory.SearchAsync("*", index: appSettings.ClientId, filter: serviceProvider.ToMemoryFilter(),
            limit: int.MaxValue,
            cancellationToken: cancellationToken);

        return indexes.Results.Select(a => new
        {
            id = a.DocumentId,
            memory = string.Join("\n\n", a.Partitions.Select(t => t.Text))
        })
        .ToJsonContentBlock(appSettings.ClientId)
        .ToCallToolResult();
    }

    [Description("Please fill in the new memory details.")]
    public class OpenMemoryNewMemory
    {
        [JsonPropertyName("memory")]
        [Required]
        [Description("The memory to add.")]
        public string Memory { get; set; } = default!;
    }
}

