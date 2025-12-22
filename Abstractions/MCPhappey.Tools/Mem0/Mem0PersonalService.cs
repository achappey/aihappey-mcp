using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCPhappey.Auth.Extensions;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Mem0;

public static class Mem0PersonalService
{
    // ---------------------------------------------------------------------
    // LIST PERSONAL
    // ---------------------------------------------------------------------
    [Description("List personal (user-only) memories from Mem0 with paging.")]
    [McpServerTool(
        Title = "List personal memories",
        Name = "mem0_list_personal_memories",
        ReadOnly = true)]
    public static async Task<CallToolResult?> Mem0_ListPersonalMemories(
        int page,
        [Range(1, 100)] int pageSize,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default)
        => await context.WithExceptionCheck(async () =>
        await context.WithStructuredContent(async () =>
        {
            if (page < 1) throw new ArgumentException("Page must be >= 1");
            if (pageSize < 1) throw new ArgumentException("PageSize must be >= 1");

            var userId = serviceProvider.GetUserId();

            var filters = new JsonObject
            {
                ["user_id"] = new JsonObject
                {
                    ["in"] = new JsonArray(userId)
                }
            };

            var body = new Dictionary<string, object>
            {
                ["filters"] = filters,
                ["page_size"] = pageSize,
                ["page"] = page,
            };

            return await serviceProvider.SendAsync("https://api.mem0.ai/v2/memories/", body, cancellationToken);
        }));

    // ---------------------------------------------------------------------
    // SEARCH PERSONAL
    // ---------------------------------------------------------------------
    [Description("Search personal memories only.")]
    [McpServerTool(
        Title = "Search personal memories",
        Name = "mem0_search_personal_memories",
        ReadOnly = true)]
    public static async Task<CallToolResult?> Mem0_SearchPersonalMemories(
        string query,
        int topK,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default)
        => await context.WithExceptionCheck(async () =>
        await context.WithStructuredContent(async () =>
        {
            var userId = serviceProvider.GetUserId();

            var filters = new JsonObject
            {
                ["user_id"] = new JsonObject
                {
                    ["in"] = new JsonArray(userId)
                }
            };

            var body = new Dictionary<string, object>
            {
                ["query"] = query,
                ["filters"] = filters,
                ["version"] = "v2",
                ["top_k"] = topK
            };

            return await serviceProvider.SendAsync("https://api.mem0.ai/v2/memories/search/", body, cancellationToken);
        }));

    // ---------------------------------------------------------------------
    // CREATE PERSONAL MEMORY
    // ---------------------------------------------------------------------
    [Description("Add a new personal memory, only visible to the current user.")]
    [McpServerTool(
        Title = "Add personal memory",
        Name = "mem0_add_personal_memory",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = false)]
    public static async Task<CallToolResult?> Mem0_AddPersonalMemory(
        Mem0RoleType role,
        string content,
        bool immutable,
        bool infer,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default)
        => await context.WithExceptionCheck(async () =>
        await context.WithStructuredContent(async () =>
        {
            var userId = serviceProvider.GetUserId();

            var (typed, notAccepted, _) = await context.Server.TryElicit(
                new Mem0AddMemory { Role = role, Content = content, Immutable = immutable, Infer = infer },
                cancellationToken);

            var body = new Dictionary<string, object>
            {
                ["messages"] = new[] { new { role, content } },
                ["user_id"] = userId!,
                ["immutable"] = typed.Immutable,
                ["infer"] = typed.Infer,
                ["version"] = "v2",
            };

            if (typed.ExpirationDate.HasValue && typed.ExpirationDate.Value > DateTime.MinValue)
                body["expiration_date"] = typed.ExpirationDate.FormatDate()!;

            return await serviceProvider.SendAsync("https://api.mem0.ai/v1/memories/", body, cancellationToken);
        }));

    // ---------------------------------------------------------------------
    // UPDATE PERSONAL
    // ---------------------------------------------------------------------
    [Description("Update a personal memory text.")]
    [McpServerTool(
        Title = "Update personal memory",
        Name = "mem0_update_personal_memory",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> Mem0_UpdatePersonalMemory(
        string memoryId,
        string text,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default)
        => await context.WithExceptionCheck(async () =>
        await context.WithStructuredContent(async () =>
        {
            var userId = serviceProvider.GetUserId();
            var mem0Settings = serviceProvider.GetRequiredService<Mem0Settings>();
            var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

            var (typed, notAccepted, _) = await context.Server.TryElicit(
                new Mem0UpdateMemory { Text = text }, cancellationToken);

            var body = new Dictionary<string, object>
            {
                ["text"] = typed.Text,
                ["metadata"] = new { updatedBy = userId }
            };

            using var client = clientFactory.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Put, $"https://api.mem0.ai/v1/memories/{memoryId}/")
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, MimeTypes.Json)
            };

            req.Headers.Authorization = new AuthenticationHeaderValue("Token", mem0Settings.ApiKey);

            using var resp = await client.SendAsync(req, cancellationToken);
            var json = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode) throw new Exception($"{resp.StatusCode}: {json}");

            return await JsonNode.ParseAsync(BinaryData.FromString(json).ToStream(), cancellationToken: cancellationToken);
        }));

    // ---------------------------------------------------------------------
    // DELETE PERSONAL
    // ---------------------------------------------------------------------
    [Description("Delete a personal memory by ID.")]
    [McpServerTool(
        Title = "Delete personal memory",
        Name = "mem0_delete_personal_memory",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> Mem0_DeletePersonalMemory(
        string memoryId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default)
        => await context.WithExceptionCheck(async () =>
        await context.WithStructuredContent(async () =>
        {
            var mem0Settings = serviceProvider.GetRequiredService<Mem0Settings>();
            var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

            return await context.ConfirmAndDeleteAsync<Mem0DeleteMemory>(
                expectedName: memoryId,
                deleteAction: async _ =>
                {
                    using var client = clientFactory.CreateClient();
                    using var req = new HttpRequestMessage(HttpMethod.Delete, $"https://api.mem0.ai/v1/memories/{memoryId}/");
                    req.Headers.Authorization = new AuthenticationHeaderValue("Token", mem0Settings.ApiKey);

                    using var resp = await client.SendAsync(req, cancellationToken);
                    var json = await resp.Content.ReadAsStringAsync(cancellationToken);
                    if (!resp.IsSuccessStatusCode) throw new Exception($"{resp.StatusCode}: {json}");
                },
                successText: $"Personal memory '{memoryId}' deleted.",
                ct: cancellationToken);
        }));
}
