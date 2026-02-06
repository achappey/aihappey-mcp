using System.ComponentModel;
using System.Net.Mime;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using Microsoft.Kiota.Abstractions.Serialization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.Workbooks;

public static partial class GraphWorkbooks
{
    [Description("Get filtered rows from an Excel table on OneDrive/SharePoint using a 'values' filter (multiple allowed values) via Microsoft Graph.")]
    [McpServerTool(
        Title = "Get filtered rows from Excel table by values",
        ReadOnly = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphWorkbooks_GetRowsByValuesFilter(
            string excelFileUrl,
            string worksheetName,
            string tableName,
            string filterColumn,
            [Description("List of allowed values for the filter column.")]
            List<string> values,
            IServiceProvider serviceProvider,
            RequestContext<CallToolRequestParams> requestContext,
            CancellationToken cancellationToken = default)
            => await requestContext.WithExceptionCheck(async () =>
    {
        var mcpServer = requestContext.Server;
        using var client = await serviceProvider.GetOboGraphClient(mcpServer);


        var driveItem = await client.GetDriveItem(excelFileUrl, cancellationToken);

        // 1. Start session
        var session = await client.Drives[driveItem?.ParentReference?.DriveId]
            .Items[driveItem?.Id]
            .Workbook
            .CreateSession
            .PostAsync(new() { PersistChanges = false }, cancellationToken: cancellationToken);

        var sessionId = session?.Id ?? throw new Exception("Failed to create workbook session.");

        // 2. Stel criteria in voor "values"-filter

        // List<string> values komt als parameter binnen
        var untypedValues = new UntypedArray(
            [.. values.Select(v => new UntypedString(v)).Cast<UntypedNode>()]);

        var criteria = new Microsoft.Graph.Beta.Models.WorkbookFilterCriteria
        {
            FilterOn = "values",
            Values = untypedValues        // ← compilet: UntypedArray erft van UntypedNode
        };

        // 3. Pas de filter toe
        await client.Drives[driveItem?.ParentReference?.DriveId]
            .Items[driveItem?.Id]
            .Workbook
            .Worksheets[worksheetName]
            .Tables[tableName]
            .Columns[filterColumn]
            .Filter
            .Apply
            .PostAsync(
                new() { Criteria = criteria },
                requestConfiguration =>
                {
                    requestConfiguration.Headers.Add("workbook-session-id", sessionId);
                },
                cancellationToken);

        // 4. Haal alle kolomnamen op
        var columns = await client.Drives[driveItem?.ParentReference?.DriveId]
            .Items[driveItem?.Id]
            .Workbook
            .Tables[tableName]
            .Columns
            .GetAsync(rc => rc.Headers.Add("workbook-session-id", sessionId), cancellationToken);

        var columnNames = columns?.Value?.Select(c => c.Name).ToList() ?? [];

        // 5. Haal de gefilterde data op (zelfde als in je bestaande tool)
        var bodyRange = await client.Drives[driveItem?.ParentReference?.DriveId]
            .Items[driveItem?.Id]
            .Workbook
            .Tables[tableName]
            .DataBodyRange
            .GetAsync(rc => rc.Headers.Add("workbook-session-id", sessionId), cancellationToken);

        var addressOnly = (bodyRange?.AddressLocal ?? bodyRange?.Address)!.Split('!').Last();

        var url =
            $"https://graph.microsoft.com/beta/drives/{driveItem?.ParentReference?.DriveId}/items/{driveItem?.Id}" +
            $"/workbook/worksheets('{worksheetName}')" +
            $"/range(address='{addressOnly}')/visibleView" +
            "?$select=values,rowCount,columnCount,rows&$expand=rows";

        var reqInfo = new Microsoft.Kiota.Abstractions.RequestInformation
        {
            HttpMethod = Microsoft.Kiota.Abstractions.Method.GET,
            UrlTemplate = url
        };

        reqInfo.Headers.Add("workbook-session-id", sessionId);

        var view = await client.RequestAdapter.SendAsync(
            reqInfo,
            Microsoft.Graph.Beta.Models.WorkbookRangeView.CreateFromDiscriminatorValue,
            cancellationToken: cancellationToken);

        // Matrix helpers
        static List<List<object?>> ToMatrix(UntypedNode? n)
        {
            if (n == null) return [];
            var w = new Microsoft.Kiota.Serialization.Json.JsonSerializationWriterFactory()
                .GetSerializationWriter(MediaTypeNames.Application.Json);
            w.WriteObjectValue(null, n);
            using var s = w.GetSerializedContent();
            return System.Text.Json.JsonSerializer.Deserialize<List<List<object?>>>(s) ?? [];
        }

        // Pak alle zichtbare rijen
        var matrices = new List<List<object?>>();
        if (view?.Rows is { Count: > 0 })
        {
            foreach (var rv in view.Rows)
                matrices.AddRange(ToMatrix(rv.Values));
        }
        else
        {
            matrices.AddRange(ToMatrix(view?.Values));
        }

        // Map naar dicts o.b.v. columnNames
        var rowObjs = matrices.Select(cells =>
        {
            var dict = new Dictionary<string, object?>(columnNames.Count);
            for (int i = 0; i < columnNames.Count; i++)
                dict[columnNames[i]!] = i < cells.Count ? cells[i] : null;
            return dict;
        }).ToList();

        var workbookGraphUrl = $"https://graph.microsoft.com/beta/drives/{driveItem?.ParentReference?.DriveId}/items/{driveItem?.Id}/workbook";
        return rowObjs.ToJsonContentBlock(workbookGraphUrl).ToCallToolResult();
    });


    [Description("Get filtered rows from an Excel table on OneDrive or SharePoint via Microsoft Graph, without persisting changes.")]
    [McpServerTool(Title = "Get filtered rows from Excel table by custom query",
        ReadOnly = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphWorkbooks_GetFilteredRows(
        string excelFileUrl,
        string worksheetName,
        string tableName,
        string filterColumn,
        [Description("First filter value, e.g. '2024-07-01'")]
        string criterion1,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional operator between values, e.g. 'And' or 'Or'")]
        string? operatorValue = null,
        [Description("Second filter value, for ranges (e.g. '2024-07-31')")]
        string? criterion2 = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
    {
        var mcpServer = requestContext.Server;
        var client = await serviceProvider.GetOboGraphClient(mcpServer);
        var driveItem = await client.GetDriveItem(excelFileUrl, cancellationToken);
        // 1. Start session (persistChanges = false)
        var session = await client.Drives[driveItem?.ParentReference?.DriveId]
            .Items[driveItem?.Id]
            .Workbook
            .CreateSession
            .PostAsync(new()
            {
                PersistChanges = false
            }, cancellationToken: cancellationToken);

        var sessionId = session?.Id
            ?? throw new Exception("Failed to create workbook session.");

        var criteria = new Microsoft.Graph.Beta.Models.WorkbookFilterCriteria
        {
            FilterOn = "custom",
            Criterion1 = criterion1
        };

        if (!string.IsNullOrEmpty(operatorValue))
            criteria.Operator = operatorValue;
        if (!string.IsNullOrEmpty(criterion2))
            criteria.Criterion2 = criterion2;


        // 2. Apply filter
        await client.Drives[driveItem?.ParentReference?.DriveId]
            .Items[driveItem?.Id]
            .Workbook
            .Worksheets[worksheetName]
            .Tables[tableName]
            .Columns[filterColumn]
            .Filter
            .Apply
            .PostAsync(
                new()
                {
                    Criteria = criteria
                },
                requestConfiguration =>
                {
                    requestConfiguration.Headers.Add("workbook-session-id", sessionId);
                },
                cancellationToken);

        var columns = await client.Drives[driveItem?.ParentReference?.DriveId]
            .Items[driveItem?.Id]
            .Workbook
            .Tables[tableName]
            .Columns
            .GetAsync(requestConfiguration =>
            {
                requestConfiguration.Headers.Add("workbook-session-id", sessionId);
            }, cancellationToken);

        var columnNames = columns?.Value?.Select(c => c.Name).ToList() ?? [];
        var bodyRange = await client.Drives[driveItem?.ParentReference?.DriveId]
            .Items[driveItem?.Id]
            .Workbook
            .Tables[tableName]
            .DataBodyRange
            .GetAsync(rc => rc.Headers.Add("workbook-session-id", sessionId), cancellationToken);

        var addressOnly = (bodyRange?.AddressLocal ?? bodyRange?.Address)!.Split('!').Last(); // "A2:D999"
        var url =
            $"https://graph.microsoft.com/beta/drives/{driveItem?.ParentReference?.DriveId}/items/{driveItem?.Id}" +
            $"/workbook/worksheets('{worksheetName}')" +
            $"/range(address='{addressOnly}')/visibleView" +
            "?$select=values,rowCount,columnCount,rows&$expand=rows";

        var reqInfo = new Microsoft.Kiota.Abstractions.RequestInformation
        {
            HttpMethod = Microsoft.Kiota.Abstractions.Method.GET,
            UrlTemplate = url
        };
        reqInfo.Headers.Add("workbook-session-id", sessionId);

        var view = await client.RequestAdapter.SendAsync(
            reqInfo,
            Microsoft.Graph.Beta.Models.WorkbookRangeView.CreateFromDiscriminatorValue,
            cancellationToken: cancellationToken);

        // 3) UntypedNode helpers
        static List<List<object?>> ToMatrix(UntypedNode? n)
        {
            if (n == null) return [];
            var w = new Microsoft.Kiota.Serialization.Json.JsonSerializationWriterFactory()
                .GetSerializationWriter(MediaTypeNames.Application.Json);
            w.WriteObjectValue(null, n);
            using var s = w.GetSerializedContent();
            return System.Text.Json.JsonSerializer.Deserialize<List<List<object?>>>(s) ?? [];
        }

        // 4) Pak alle zichtbare rijen (view.Values én view.Rows[*].Values)
        var matrices = new List<List<object?>>();
        if (view?.Rows is { Count: > 0 })
        {
            foreach (var rv in view.Rows)
                matrices.AddRange(ToMatrix(rv.Values));   // alleen rows
        }
        else
        {
            matrices.AddRange(ToMatrix(view?.Values));    // fallback
        }

        // 5) Map naar dicts o.b.v. columnNames
        var rowObjs = matrices.Select(cells =>
        {
            var dict = new Dictionary<string, object?>(columnNames.Count);
            for (int i = 0; i < columnNames.Count; i++)
                dict[columnNames[i]!] = i < cells.Count ? cells[i] : null;
            return dict;
        }).ToList();

        var workbookGraphUrl = $"https://graph.microsoft.com/beta/drives/{driveItem?.ParentReference?.DriveId}/items/{driveItem?.Id}/workbook";

        return rowObjs.ToJsonContentBlock(workbookGraphUrl).ToCallToolResult();


    });

}
