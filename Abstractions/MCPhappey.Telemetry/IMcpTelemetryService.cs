
namespace MCPhappey.Telemetry;

public interface IMcpTelemetryService
{
    /// <summary>
    /// Persists a prompt request (from any provider) to the telemetry store.
    /// </summary>
    Task TrackPromptRequestAsync(
        string server,
         string sessionId,
        string clientName,
        string clientVersion,
        int outputSize,
        DateTime started,
        DateTime ended,
        string? userId,
        string? username,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a resource request (read-resource call) to the telemetry store.
    /// </summary>
    Task TrackResourceRequestAsync(
        string server,
         string sessionId,
        string clientName,
        string clientVersion,
        string resourceUrl,
        int outputSize,
        DateTime started,
        DateTime ended,
        string? userId,
        string? username,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a tool request (tool call) to the telemetry store.
    /// </summary>
    Task TrackToolRequestAsync(
        string serverUrl,
        string sessionId,
        string clientName,
        string clientVersion,
        string toolName,
        int outputSize,
        DateTime started,
        DateTime ended,
        string? userId,
        string? username,
        CancellationToken cancellationToken = default);
}

