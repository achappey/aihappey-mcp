using System.ComponentModel;
using System.Text.Json;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.Audit;

public static class GraphAudit
{
    [Description("Create a Purview audit log query. Please select a date range less than 6 months.")]
    [McpServerTool(Title = "Create audit log query", OpenWorld = false, Destructive = true)]
    public static async Task<CallToolResult?> GraphAudit_CreateLogQuery(
      RequestContext<CallToolRequestParams> requestContext,
      [Description("The audit query filter.")] string filter,
      [Description("The audit query display name.")] string? displayName = null,
      [Description("The audit query start date.")] DateTimeOffset? startTime = null,
      [Description("The audit query end date.")] DateTimeOffset? endTime = null,
      CancellationToken cancellationToken = default) =>
      await requestContext.WithExceptionCheck(async () =>
      await requestContext.WithOboGraphClient(async client =>
      await requestContext.WithStructuredContent(async () =>
    {
        var mcpServer = requestContext.Server;

        var (typed, notAccepted, result) = await mcpServer.TryElicit(
            new GraphNewAuditLogQuery
            {
                Filter = filter ?? string.Empty,
                DisplayName = displayName ?? string.Empty,
                StartDateTime = startTime,
                EndDateTime = endTime,
            },
            cancellationToken
        );

        if (notAccepted != null) throw new Exception(JsonSerializer.Serialize(notAccepted));

        // Defaults if not provided
        var now = DateTimeOffset.UtcNow;
        var start = typed!.StartDateTime ?? now.AddDays(-7);
        var end = typed.EndDateTime ?? now;

        var user = new Microsoft.Graph.Beta.Models.Security.AuditLogQuery()
        {
            KeywordFilter = typed?.Filter,
            DisplayName = string.IsNullOrWhiteSpace(typed?.DisplayName)
                ? $"Audit query {now:yyyy-MM-dd HH:mm:ss}Z"
                : typed.DisplayName,
            FilterStartDateTime = start,
            FilterEndDateTime = end,
            RecordTypeFilters = typed?.RecordTypeFilters?.ToList() ?? [],
            OperationFilters = typed?.OperationFilters?.ToList(),
            UserPrincipalNameFilters = typed?.UserPrincipalNameFilters?.ToList(),
            IpAddressFilters = typed?.IpAddressFilters?.ToList(),
            ObjectIdFilters = typed?.ObjectIdFilters?.ToList(),
            AdministrativeUnitIdFilters = typed?.AdministrativeUnitIdFilters?.ToList()
        };

        return await client.Security.AuditLog.Queries.PostAsync(user, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    })));

}
