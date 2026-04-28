using System.ComponentModel;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Anthropic.MemoryStores;

public static partial class AnthropicMemoryVersions
{
    [Description("Redact an Anthropic memory version. Only memory store owners can redact versions.")]
    [McpServerTool(Title = "Redact Anthropic Memory Version", Name = "anthropic_memory_versions_redact", ReadOnly = false, OpenWorld = false, Destructive = true)]
    public static async Task<CallToolResult?> AnthropicMemoryVersions_Redact(
        [Description("Memory store ID.")] string memoryStoreId,
        [Description("Memory version ID.")] string memoryVersionId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var (typed, _, _) = await requestContext.Server.TryElicit(new AnthropicRedactMemoryVersionRequest
                {
                    MemoryStoreId = memoryStoreId,
                    MemoryVersionId = memoryVersionId,
                   
                }, cancellationToken);

                var normalizedMemoryStoreId = AnthropicMemoryStores.NormalizeMemoryStoreId(typed.MemoryStoreId);
                var normalizedMemoryVersionId = AnthropicMemoryStores.NormalizeId(typed.MemoryVersionId, "memoryVersionId");
                await AnthropicMemoryStores.GetOwnerMemoryStoreAsync(serviceProvider, normalizedMemoryStoreId,  cancellationToken);

                return await AnthropicManagedAgentsHttp.SendAsync(
                    serviceProvider,
                    HttpMethod.Post,
                    $"{AnthropicMemoryStores.BaseUrl}/{Uri.EscapeDataString(normalizedMemoryStoreId)}/memory_versions/{Uri.EscapeDataString(normalizedMemoryVersionId)}/redact",
                    null,
                    
                    cancellationToken);
            }));
}

