using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace MCPhappey.Tools.Smooth;

public sealed class SmoothClient
{
    public async Task<JsonObject> SubmitTaskAsync(
        HttpClient client,
        JsonObject payload,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "task")
        {
            Content = JsonContent.Create(payload)
        };

        return await SendAsync(client, request, cancellationToken);
    }

    public async Task<JsonObject> GetTaskAsync(
        HttpClient client,
        string taskId,
        long eventTimestamp,
        bool includeDownloads,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        var endpoint = $"task/{Uri.EscapeDataString(taskId)}?event_t={eventTimestamp}&downloads={(includeDownloads ? "true" : "false")}";

        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        return await SendAsync(client, request, cancellationToken);
    }

    private static async Task<JsonObject> SendAsync(
        HttpClient client,
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using var response = await client.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Smooth API call failed ({(int)response.StatusCode}): {text}");

        if (string.IsNullOrWhiteSpace(text))
            return new JsonObject();

        return JsonNode.Parse(text) as JsonObject
            ?? new JsonObject { ["raw"] = text };
    }
}

