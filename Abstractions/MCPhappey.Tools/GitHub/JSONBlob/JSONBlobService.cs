using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.GitHub.JSONBlob;

public static class JSONBlobService
{
    private const string BASE_URL = "https://jsonblob.com";
    private const string API_PREFIX = "/api";

    // --------------------------------- CREATE ---------------------------------
    [Description("Create a new JSON Blob (POST /api/jsonBlob). Returns the created JSON and the blob URL from the Location header.")]
    [McpServerTool(
        Title = "Create a JSONBlob",
        OpenWorld = false,
        Destructive = true,
        Idempotent = false)]
    public static async Task<CallToolResult?> JSONBlob_Create(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Arbitrary JSON to store as the blob.")]
            string data,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
    {
        var http = CreateClient(serviceProvider);
        var url = $"{BASE_URL}{API_PREFIX}/jsonBlob";

        // Optional: show call as markdown trace
        await requestContext.Server.SendMessageNotificationAsync(
            $"<details><summary>POST <a href=\"{url}\" target=\"_blank\">jsonblob.com</a></summary>\n\n```json\n{data}\n```\n</details>");

        using var res = await http.PostAsync(url,
            new StringContent(data, Encoding.UTF8, MimeTypes.Json),
            cancellationToken);

        var error = await res.ToCallToolResponseOrErrorAsync(cancellationToken);
        if (error != null) return error;

        var body = await res.Content.ReadAsStringAsync(cancellationToken);
        var json = JsonSerializer.Deserialize<JsonElement>(body);
        var location = res.Headers.Location?.ToString() ?? $"{BASE_URL}{API_PREFIX}/jsonBlob/<unknown>";

        // Include the Location for the assistant/user
        var block = json.ToJsonContentBlock(location);
        return block.ToCallToolResult();
    });

    // --------------------------------- UPDATE ---------------------------------
    [Description("Replace a JSON Blob by blobId (PUT /api/jsonBlob/{blobId}).")]
    [McpServerTool(
        Title = "Update a JSONBlob",
        OpenWorld = false,
        Destructive = true,
        Idempotent = false)]
    public static async Task<CallToolResult?> JSONBlob_Update(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The blobId to update.")] string blobId,
        [Description("New JSON that replaces the stored blob.")] JsonElement data,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
    {
        // Elicit typed payload (defensive)
        var http = CreateClient(serviceProvider);
        var url = $"{BASE_URL}{API_PREFIX}/jsonBlob/{blobId}";

        await requestContext.Server.SendMessageNotificationAsync(
            $"<details><summary>PUT <a href=\"{url}\" target=\"_blank\">jsonblob.com</a></summary>\n\n```json\n{JsonSerializer.Serialize(data, WriteIndented)}\n```\n</details>");

        using var res = await http.PutAsync(url,
            new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, MimeTypes.Json),
            cancellationToken);

        var error = await res.ToCallToolResponseOrErrorAsync(cancellationToken);
        if (error != null) return error;

        var body = await res.Content.ReadAsStringAsync(cancellationToken);
        var json = JsonSerializer.Deserialize<JsonElement>(body);

        return json.ToJsonContentBlock(url).ToCallToolResult();
    });

    // --------------------------------- DELETE ---------------------------------
    [Description("Delete a JSON Blob by blobId (DELETE /api/jsonBlob/{blobId}). Uses confirm-and-delete elicit flow.")]
    [McpServerTool(
        Title = "Delete a JSONBlob",
        OpenWorld = false,
        Destructive = true,
        Idempotent = false)]
    public static async Task<CallToolResult?> JSONBlob_Delete(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The blobId to delete.")] string blobId,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
    {
        if (string.IsNullOrWhiteSpace(blobId))
            return "blobId is required.".ToErrorCallToolResponse();

        var url = $"{BASE_URL}{API_PREFIX}/jsonBlob/{blobId}";

        return await requestContext.ConfirmAndDeleteAsync<ConfirmDeleteJsonBlob>(
            expectedName: blobId,
            deleteAction: async _ =>
            {
                var http = CreateClient(serviceProvider);
                using var res = await http.DeleteAsync(url, cancellationToken);

                if (!res.IsSuccessStatusCode)
                {
                    var body = await res.Content.ReadAsStringAsync(cancellationToken);
                    throw new Exception($"JSONBlob delete error {(int)res.StatusCode} {res.ReasonPhrase}: {body}");
                }
            },
            successText: $"Blob {blobId} has been deleted.",
            ct: cancellationToken);
    });

    // ------------------------------- Helpers & Types ---------------------------
    private static HttpClient CreateClient(IServiceProvider sp)
    {
        var factory = sp.GetRequiredService<IHttpClientFactory>();
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
        return http;
    }

    private static readonly JsonSerializerOptions WriteIndented = new() { WriteIndented = true };

    [Description("Please confirm the id of the blob you want to delete.")]
    public class ConfirmDeleteJsonBlob : IHasName
    {
        [JsonPropertyName("name")]
        [Required]
        [Description("Enter the exact blobId to confirm deletion: {0}")]
        public string Name { get; set; } = default!;
    }
}
