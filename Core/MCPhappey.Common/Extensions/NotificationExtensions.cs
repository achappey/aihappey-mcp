
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Common.Extensions;

public static class NotificationExtensions
{

    public static bool ShouldLog(this LoggingLevel messageLevel, LoggingLevel? minLevel)
        => messageLevel >= (minLevel ?? LoggingLevel.Info);

    public static async Task<int?> SendProgressNotificationAsync(
       this McpServer mcpServer,
       RequestContext<CallToolRequestParams> requestContext,
       int? progressCounter,
       string? message,
       int? total = null,
       CancellationToken cancellationToken = default)
    {
        var progressToken = requestContext.Params?.ProgressToken;
        if (progressToken is not null && progressCounter is not null)
        {
            await mcpServer.SendNotificationAsync(
                "notifications/progress",
                new ProgressNotificationParams
                {
                    ProgressToken = progressToken.Value,
                    Progress = new ProgressNotificationValue
                    {
                        Progress = progressCounter.Value,
                        Total = total,
                        Message = message
                    }
                },
                cancellationToken: CancellationToken.None
            );

            progressCounter++;
            return progressCounter;
        }

        return progressCounter;
    }

    public static async Task SendMessageNotificationAsync(
      this McpServer requestContext,
      string? message,
      LoggingLevel loggingLevel = LoggingLevel.Info,
      CancellationToken cancellationToken = default)
    {
        if (loggingLevel.ShouldLog(requestContext.LoggingLevel))
        {
            await requestContext.SendNotificationAsync(
                "notifications/message",
                new LoggingMessageNotificationParams
                {
                    Level = loggingLevel,
                    Data = JsonSerializer.SerializeToElement(message),
                },
                cancellationToken: CancellationToken.None
            );
        }
    }
}
