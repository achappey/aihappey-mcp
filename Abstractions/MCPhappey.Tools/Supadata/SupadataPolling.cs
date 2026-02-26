using System.Diagnostics;
using System.Text.Json.Nodes;

namespace MCPhappey.Tools.Supadata;

public static class SupadataPolling
{
    public static async Task<JsonNode?> PollUntilCompletedAsync(
        Func<CancellationToken, Task<JsonNode?>> poll,
        Func<JsonNode?, string?> statusSelector,
        Func<JsonNode?, string?> errorSelector,
        int pollIntervalSeconds,
        CancellationToken cancellationToken)
    {
        var intervalSeconds = Math.Clamp(pollIntervalSeconds, 1, 60);
        var sw = Stopwatch.StartNew();
        JsonNode? latest = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            latest = await poll(cancellationToken);
            var status = statusSelector(latest)?.Trim().ToLowerInvariant();
            if (status is "completed" or "done" or "success" or "succeeded")
                return latest;
            if (status is "failed" or "error" or "cancelled" or "canceled")
            {
                var error = errorSelector(latest);
                throw new Exception($"Supadata job failed after {sw.Elapsed.TotalSeconds:0}s. Status={status}. Error={error}");
            }

            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), cancellationToken);
        }
    }
}
