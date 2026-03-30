using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Auth.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using MCPhappey.Tools.Memory.OneDrive;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graph.Beta;
using Microsoft.KernelMemory.Pipeline;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.MemU;

public static class MemUService
{
    private const string BaseUrl = "https://api.memu.so";
    private const string ConversationFolder = "/memu";
    private const string ConversationFilePath = "/memu/conversations.json";

    [Description("List memory categories from MemU for the current signed-in user and a specific agent.")]
    [McpServerTool(Title = "List MemU memory categories", Name = "memu_list_categories", ReadOnly = true)]
    public static async Task<CallToolResult?> MemU_ListCategories(
        [Description("Unique identifier for the AI agent.")] string agent_id,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            var userId = serviceProvider.GetUserId() ?? throw new Exception("Could not resolve current user id from Graph auth context.");

            return await SendJsonAsync<MemUCategoriesResponse>(
                serviceProvider,
                HttpMethod.Post,
                "/api/v3/memory/categories",
                new MemUCategoriesRequest(userId, agent_id),
                cancellationToken);
        }));

    [Description("Retrieve memories from MemU for the current signed-in user and a specific agent using a plain-text query.")]
    [McpServerTool(Title = "Retrieve MemU memories", Name = "memu_retrieve_memories", ReadOnly = true)]
    public static async Task<CallToolResult?> MemU_RetrieveMemories(
        [Description("Unique identifier for the AI agent.")] string agent_id,
        [Description("Natural language query used for semantic memory retrieval.")] string query,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            var userId = serviceProvider.GetUserId() ?? throw new Exception("Could not resolve current user id from Graph auth context.");

            return await SendJsonAsync<MemURetrieveResponse>(
                serviceProvider,
                HttpMethod.Post,
                "/api/v3/memory/retrieve",
                new MemURetrieveRequest(userId, agent_id, query),
                cancellationToken);
        }));

    [Description("Clear MemU memory data for the current signed-in user. Optionally scope the clear operation to one agent.")]
    [McpServerTool(Title = "Clear MemU memories", Name = "memu_clear_memories", ReadOnly = false, OpenWorld = false, Destructive = true)]
    public static async Task<CallToolResult?> MemU_ClearMemories(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional AI agent identifier. Leave empty to clear all memories belonging to the current user across agents.")] string? agent_id = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            var userId = serviceProvider.GetUserId() ?? throw new Exception("Could not resolve current user id from Graph auth context.");

            return await SendJsonAsync<MemUClearResponse>(
                serviceProvider,
                HttpMethod.Post,
                "/api/v3/memory/clear",
                new MemUClearRequest(userId, agent_id),
                cancellationToken);
        }));

    [Description("List conversation drafts stored in the current user's OneDrive under /memu.")]
    [McpServerTool(Title = "List MemU conversations", Name = "memu_list_conversations", ReadOnly = true, OpenWorld = false)]
    public static async Task<CallToolResult?> MemU_ListConversations(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async graph =>
        await requestContext.WithStructuredContent(async () =>
        {
            var store = await LoadConversationStoreAsync(graph, cancellationToken);
            var currentUser = await ResolveCurrentUserAsync(serviceProvider, graph, cancellationToken);

            return new MemUConversationListResponse(
                ConversationFilePath,
                currentUser.UserId,
                currentUser.UserName,
                store.Conversations
                    .Where(c => string.Equals(c.UserId, currentUser.UserId, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(c => c.UpdatedAt)
                    .Select(ToSummary)
                    .ToArray());
        })));

    [Description("Get one MemU conversation draft from the current user's OneDrive-backed conversation store.")]
    [McpServerTool(Title = "Get MemU conversation", Name = "memu_get_conversation", ReadOnly = true, OpenWorld = false)]
    public static async Task<CallToolResult?> MemU_GetConversation(
        [Description("Conversation identifier returned from memu_create_conversation.")] string conversation_id,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async graph =>
        await requestContext.WithStructuredContent(async () =>
        {
            var store = await LoadConversationStoreAsync(graph, cancellationToken);
            var currentUser = await ResolveCurrentUserAsync(serviceProvider, graph, cancellationToken);
            var conversation = store.Conversations.FirstOrDefault(c =>
                c.ConversationId.Equals(conversation_id, StringComparison.OrdinalIgnoreCase)
                && c.UserId.Equals(currentUser.UserId, StringComparison.OrdinalIgnoreCase))
                ?? throw new Exception($"Conversation '{conversation_id}' was not found for the current user.");

            return ToDetail(conversation);
        })));

    [Description("Create a new MemU conversation draft in /memu/conversations.json in the current user's OneDrive.")]
    [McpServerTool(Title = "Create MemU conversation", Name = "memu_create_conversation", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> MemU_CreateConversation(
        [Description("Unique identifier for the AI agent that owns this conversation.")] string agent_id,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional display title for the conversation.")] string? title = null,
        [Description("Optional display name for the AI agent.")] string? agent_name = null,
        [Description("Optional display name for the current user. If omitted, Graph profile data is used.")] string? user_name = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async graph =>
        await requestContext.WithStructuredContent(async () =>
        {
            var currentUser = await ResolveCurrentUserAsync(requestContext.Services!, graph, cancellationToken, user_name);
            var store = await LoadConversationStoreAsync(graph, cancellationToken);

            var now = DateTimeOffset.UtcNow;
            var conversation = new MemUConversationDocument
            {
                ConversationId = $"memu_{Guid.NewGuid():N}",
                Title = string.IsNullOrWhiteSpace(title) ? $"MemU conversation {now:yyyy-MM-dd HH:mm}" : title.Trim(),
                UserId = currentUser.UserId,
                UserName = currentUser.UserName,
                AgentId = agent_id,
                AgentName = agent_name,
                SessionDate = now,
                CreatedAt = now,
                UpdatedAt = now,
                Messages = []
            };

            store.Conversations.Add(conversation);
            var webUrl = await SaveConversationStoreAsync(graph, store, cancellationToken);

            return new MemUConversationCreateResponse(
                webUrl,
                ConversationFilePath,
                conversation.ConversationId,
                conversation.Title,
                conversation.UserId,
                conversation.UserName,
                conversation.AgentId,
                conversation.AgentName,
                conversation.SessionDate,
                conversation.CreatedAt,
                conversation.UpdatedAt);
        })));

    [Description("Append a user or assistant message to an existing MemU conversation draft stored in the current user's OneDrive.")]
    [McpServerTool(Title = "Add MemU conversation message", Name = "memu_add_conversation_message", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> MemU_AddConversationMessage(
        [Description("Conversation identifier returned from memu_create_conversation.")] string conversation_id,
        [Description("Message role. Allowed values: user or assistant.")] MemUConversationRole role,
        [Description("Message text content.")] string content,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional display name override for the message sender.")] string? name = null,
        [Description("Optional ISO 8601 timestamp. If omitted, current UTC time is used.")] string? created_at = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async graph =>
        await requestContext.WithStructuredContent(async () =>
        {
            var store = await LoadConversationStoreAsync(graph, cancellationToken);
            var currentUser = await ResolveCurrentUserAsync(requestContext.Services!, graph, cancellationToken);
            var conversation = store.Conversations.FirstOrDefault(c =>
                c.ConversationId.Equals(conversation_id, StringComparison.OrdinalIgnoreCase)
                && c.UserId.Equals(currentUser.UserId, StringComparison.OrdinalIgnoreCase))
                ?? throw new Exception($"Conversation '{conversation_id}' was not found for the current user.");

            var createdAt = ParseOrNow(created_at);
            var resolvedName = !string.IsNullOrWhiteSpace(name)
                ? name.Trim()
                : role == MemUConversationRole.user
                    ? currentUser.UserName
                    : conversation.AgentName;

            conversation.Messages.Add(new MemUConversationMessageDocument
            {
                Role = role,
                Name = resolvedName,
                CreatedAt = createdAt,
                Content = content
            });
            conversation.UpdatedAt = DateTimeOffset.UtcNow;
            if (conversation.SessionDate == default)
                conversation.SessionDate = conversation.Messages.FirstOrDefault()?.CreatedAt ?? conversation.UpdatedAt;

            var webUrl = await SaveConversationStoreAsync(graph, store, cancellationToken);

            return new MemUConversationMessageAddResponse(
                webUrl,
                conversation.ConversationId,
                conversation.Messages.Count,
                ToDetail(conversation));
        })));

    [Description("Send a stored MemU conversation draft from /memu/conversations.json to the MemU backend for memorization.")]
    [McpServerTool(Title = "Memorize MemU conversation", Name = "memu_memorize_conversation", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> MemU_MemorizeConversation(
        [Description("Conversation identifier returned from memu_create_conversation.")] string conversation_id,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional display name override for the current user.")] string? user_name = null,
        [Description("Optional ISO 8601 session timestamp override.")] string? session_date = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async graph =>
        await requestContext.WithStructuredContent(async () =>
        {
            var store = await LoadConversationStoreAsync(graph, cancellationToken);
            var currentUser = await ResolveCurrentUserAsync(serviceProvider, graph, cancellationToken, user_name);
            var conversation = store.Conversations.FirstOrDefault(c =>
                c.ConversationId.Equals(conversation_id, StringComparison.OrdinalIgnoreCase)
                && c.UserId.Equals(currentUser.UserId, StringComparison.OrdinalIgnoreCase))
                ?? throw new Exception($"Conversation '{conversation_id}' was not found for the current user.");

            if (conversation.Messages.Count < 3)
                throw new Exception("MemU requires at least 3 conversation messages before memorization can be started.");
            if (string.IsNullOrWhiteSpace(conversation.AgentId))
                throw new Exception("Conversation is missing an agent_id and cannot be memorized.");

            conversation.UserName = currentUser.UserName;
            conversation.SessionDate = !string.IsNullOrWhiteSpace(session_date)
                ? ParseOrNow(session_date)
                : conversation.SessionDate == default
                    ? conversation.Messages.First().CreatedAt
                    : conversation.SessionDate;
            conversation.UpdatedAt = DateTimeOffset.UtcNow;

            var payload = new MemUMemorizeRequest(
                conversation.Messages.Select(m => new MemUMemorizeMessage(
                    m.Role.ToString(),
                    m.Name,
                    m.CreatedAt == default ? null : m.CreatedAt.UtcDateTime.ToString("O"),
                    m.Content)).ToArray(),
                currentUser.UserId,
                currentUser.UserName,
                conversation.AgentId,
                conversation.AgentName,
                conversation.SessionDate.UtcDateTime.ToString("O"));

            var response = await SendJsonAsync<MemUMemorizeResponse>(
                serviceProvider,
                HttpMethod.Post,
                "/api/v3/memory/memorize",
                payload,
                cancellationToken);

            conversation.Memorization = new MemUMemorizationState
            {
                LastTaskId = response.TaskId,
                LastStatus = response.Status,
                LastRequestedAt = DateTimeOffset.UtcNow,
                LastMessage = response.Message
            };

            var webUrl = await SaveConversationStoreAsync(graph, store, cancellationToken);

            return new MemUMemorizeConversationResponse(
                webUrl,
                conversation.ConversationId,
                response.TaskId,
                response.Status,
                response.Message,
                conversation.Messages.Count,
                conversation.AgentId,
                conversation.AgentName,
                conversation.UserId,
                conversation.UserName);
        })));

    private static async Task<T> SendJsonAsync<T>(
        IServiceProvider serviceProvider,
        HttpMethod method,
        string relativeUrl,
        object? body,
        CancellationToken cancellationToken)
    {
        var settings = serviceProvider.GetRequiredService<MemUSettings>();
        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        using var client = clientFactory.CreateClient();
        client.BaseAddress = new Uri(BaseUrl);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));

        using var request = new HttpRequestMessage(method, relativeUrl);
        if (body is not null)
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(body, JsonOptions),
                Encoding.UTF8,
                MimeTypes.Json);
        }

        using var response = await client.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"MemU API error {(int)response.StatusCode} {response.ReasonPhrase}: {json}");

        var result = JsonSerializer.Deserialize<T>(json, JsonOptions);
        return result ?? throw new Exception("MemU API returned an empty response body.");
    }

    private static async Task<MemUConversationStoreDocument> LoadConversationStoreAsync(
        GraphServiceClient graph,
        CancellationToken cancellationToken)
    {
        var drive = await graph.GetDefaultDriveAsync(cancellationToken)
                   ?? throw new Exception("Could not resolve default OneDrive.");

        await graph.EnsureFolderExistsAsync(drive.Id!, ConversationFolder, cancellationToken);

        var json = await graph.ReadTextFileAsync(drive.Id!, ConversationFilePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
            return new MemUConversationStoreDocument();

        var store = JsonSerializer.Deserialize<MemUConversationStoreDocument>(json, JsonOptions);
        return store ?? new MemUConversationStoreDocument();
    }

    private static async Task<string?> SaveConversationStoreAsync(
        GraphServiceClient graph,
        MemUConversationStoreDocument store,
        CancellationToken cancellationToken)
    {
        var drive = await graph.GetDefaultDriveAsync(cancellationToken)
                   ?? throw new Exception("Could not resolve default OneDrive.");

        await graph.EnsureFolderExistsAsync(drive.Id!, ConversationFolder, cancellationToken);

        var json = JsonSerializer.Serialize(store, JsonOptions);
        var item = await graph.WriteTextFileAsync(drive.Id!, ConversationFilePath, json, cancellationToken);
        return item?.WebUrl;
    }

    private static async Task<MemUCurrentUser> ResolveCurrentUserAsync(
        IServiceProvider serviceProvider,
        GraphServiceClient graph,
        CancellationToken cancellationToken,
        string? explicitUserName = null)
    {
        var userId = serviceProvider.GetUserId() ?? throw new Exception("Could not resolve current user id from Graph auth context.");

        var me = await graph.Me.GetAsync(config =>
        {
            config.QueryParameters.Select = ["id", "displayName", "userPrincipalName"];
        }, cancellationToken: cancellationToken);

        var resolvedName = !string.IsNullOrWhiteSpace(explicitUserName)
            ? explicitUserName.Trim()
            : me?.DisplayName
                ?? me?.UserPrincipalName
                ?? userId;

        return new MemUCurrentUser(userId, resolvedName);
    }

    private static MemUConversationSummary ToSummary(MemUConversationDocument conversation)
        => new(
            conversation.ConversationId,
            conversation.Title,
            conversation.AgentId,
            conversation.AgentName,
            conversation.UserId,
            conversation.UserName,
            conversation.Messages.Count,
            conversation.CreatedAt,
            conversation.UpdatedAt,
            conversation.Memorization?.LastTaskId,
            conversation.Memorization?.LastStatus);

    private static MemUConversationDetail ToDetail(MemUConversationDocument conversation)
        => new(
            conversation.ConversationId,
            conversation.Title,
            conversation.UserId,
            conversation.UserName,
            conversation.AgentId,
            conversation.AgentName,
            conversation.SessionDate,
            conversation.CreatedAt,
            conversation.UpdatedAt,
            conversation.Memorization,
            conversation.Messages.Select(m => new MemUConversationMessageView(m.Role.ToString(), m.Name, m.CreatedAt, m.Content)).ToArray());

    private static DateTimeOffset ParseOrNow(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? DateTimeOffset.UtcNow
            : DateTimeOffset.TryParse(value, out var parsed)
                ? parsed.ToUniversalTime()
                : throw new Exception($"Invalid ISO 8601 timestamp: '{value}'.");

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
}

public sealed record MemUCategoriesRequest(
    [property: JsonPropertyName("user_id")] string UserId,
    [property: JsonPropertyName("agent_id")] string AgentId);

public sealed record MemUCategoriesResponse(
    [property: JsonPropertyName("categories")] IReadOnlyList<MemUCategory> Categories);

public sealed record MemURetrieveRequest(
    [property: JsonPropertyName("user_id")] string UserId,
    [property: JsonPropertyName("agent_id")] string AgentId,
    [property: JsonPropertyName("query")] string Query);

public sealed record MemURetrieveResponse(
    [property: JsonPropertyName("rewritten_query")] string? RewrittenQuery,
    [property: JsonPropertyName("categories")] IReadOnlyList<MemURetrieveCategory>? Categories,
    [property: JsonPropertyName("items")] IReadOnlyList<MemURetrieveItem>? Items,
    [property: JsonPropertyName("resources")] IReadOnlyList<MemURetrieveResource>? Resources);

public sealed record MemUClearRequest(
    [property: JsonPropertyName("user_id")] string UserId,
    [property: JsonPropertyName("agent_id")] string? AgentId);

public sealed record MemUClearResponse(
    [property: JsonPropertyName("purged_categories")] int PurgedCategories,
    [property: JsonPropertyName("purged_items")] int PurgedItems,
    [property: JsonPropertyName("purged_resources")] int PurgedResources);

public sealed record MemUMemorizeRequest(
    [property: JsonPropertyName("conversation")] IReadOnlyList<MemUMemorizeMessage> Conversation,
    [property: JsonPropertyName("user_id")] string UserId,
    [property: JsonPropertyName("user_name")] string? UserName,
    [property: JsonPropertyName("agent_id")] string AgentId,
    [property: JsonPropertyName("agent_name")] string? AgentName,
    [property: JsonPropertyName("session_date")] string? SessionDate);

public sealed record MemUMemorizeMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("created_at")] string? CreatedAt,
    [property: JsonPropertyName("content")] string Content);

public sealed record MemUMemorizeResponse(
    [property: JsonPropertyName("task_id")] string TaskId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("message")] string Message);

public sealed record MemUCategory(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("user_id")] string? UserId,
    [property: JsonPropertyName("agent_id")] string? AgentId,
    [property: JsonPropertyName("summary")] string? Summary);

public sealed record MemURetrieveCategory(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("summary")] string? Summary);

public sealed record MemURetrieveItem(
    [property: JsonPropertyName("memory_type")] string? MemoryType,
    [property: JsonPropertyName("content")] string? Content);

public sealed record MemURetrieveResource(
    [property: JsonPropertyName("modality")] string? Modality,
    [property: JsonPropertyName("resource_url")] string? ResourceUrl,
    [property: JsonPropertyName("caption")] string? Caption,
    [property: JsonPropertyName("metadata")] Dictionary<string, object>? Metadata,
    [property: JsonPropertyName("content")] string? Content);

public sealed record MemUConversationListResponse(
    string FilePath,
    string UserId,
    string UserName,
    IReadOnlyList<MemUConversationSummary> Conversations);

public sealed record MemUConversationCreateResponse(
    string? WebUrl,
    string FilePath,
    string ConversationId,
    string Title,
    string UserId,
    string UserName,
    string AgentId,
    string? AgentName,
    DateTimeOffset SessionDate,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record MemUConversationMessageAddResponse(
    string? WebUrl,
    string ConversationId,
    int MessageCount,
    MemUConversationDetail Conversation);

public sealed record MemUMemorizeConversationResponse(
    string? WebUrl,
    string ConversationId,
    string TaskId,
    string Status,
    string Message,
    int MessageCount,
    string AgentId,
    string? AgentName,
    string UserId,
    string UserName);

public sealed record MemUConversationSummary(
    string ConversationId,
    string Title,
    string AgentId,
    string? AgentName,
    string UserId,
    string UserName,
    int MessageCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? LastTaskId,
    string? LastStatus);

public sealed record MemUConversationDetail(
    string ConversationId,
    string Title,
    string UserId,
    string UserName,
    string AgentId,
    string? AgentName,
    DateTimeOffset SessionDate,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    MemUMemorizationState? Memorization,
    IReadOnlyList<MemUConversationMessageView> Messages);

public sealed record MemUConversationMessageView(
    string Role,
    string? Name,
    DateTimeOffset CreatedAt,
    string Content);

public sealed record MemUCurrentUser(string UserId, string UserName);

public sealed class MemUConversationStoreDocument
{
    [JsonPropertyName("conversations")]
    public List<MemUConversationDocument> Conversations { get; set; } = [];
}

public sealed class MemUConversationDocument
{
    [JsonPropertyName("conversation_id")]
    public string ConversationId { get; set; } = default!;

    [JsonPropertyName("title")]
    public string Title { get; set; } = default!;

    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = default!;

    [JsonPropertyName("user_name")]
    public string UserName { get; set; } = default!;

    [JsonPropertyName("agent_id")]
    public string AgentId { get; set; } = default!;

    [JsonPropertyName("agent_name")]
    public string? AgentName { get; set; }

    [JsonPropertyName("session_date")]
    public DateTimeOffset SessionDate { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonPropertyName("memorization")]
    public MemUMemorizationState? Memorization { get; set; }

    [JsonPropertyName("messages")]
    public List<MemUConversationMessageDocument> Messages { get; set; } = [];
}

public sealed class MemUMemorizationState
{
    [JsonPropertyName("last_task_id")]
    public string? LastTaskId { get; set; }

    [JsonPropertyName("last_status")]
    public string? LastStatus { get; set; }

    [JsonPropertyName("last_requested_at")]
    public DateTimeOffset? LastRequestedAt { get; set; }

    [JsonPropertyName("last_message")]
    public string? LastMessage { get; set; }
}

public sealed class MemUConversationMessageDocument
{
    [JsonPropertyName("role")]
    public MemUConversationRole Role { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("content")]
    public string Content { get; set; } = default!;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MemUConversationRole
{
    user,
    assistant
}
