using System.Linq.Dynamic.Core;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using MCPhappey.Core.Extensions;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions.Serialization;

namespace MCPhappey.Tools.DataAnalysis;

public static partial class DataAnalysisExcelExtensions
{
    public static async Task<List<IDictionary<string, object?>>?> ExcelToGenericTable(
        this DriveItem driveItem,
        string tableName,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
    {
        using var graphClient = await serviceProvider.GetOboGraphClient(requestContext.Server);
        var driveId = driveItem.ParentReference!.DriveId!;
        var itemId = driveItem.Id!;

        // Enumerate tables and match by name (case-insensitive).
        var tablesResp = await graphClient
            .Drives[driveId]
            .Items[itemId]
            .Workbook
            .Tables
            .GetAsync(cancellationToken: cancellationToken);

        var table = tablesResp?.Value?.FirstOrDefault(t =>
            string.Equals(t.Name, tableName, StringComparison.OrdinalIgnoreCase));

        if (table is null)
            return null;

        // Header row (names)
        var headerRange = await graphClient
            .Drives[driveId]
            .Items[itemId]
            .Workbook
            .Tables[table.Id!]
            .HeaderRowRange
            .GetAsync(cancellationToken: cancellationToken);

        // Data body (rows, excludes header and totals)
        var bodyRange = await graphClient
            .Drives[driveId]
            .Items[itemId]
            .Workbook
            .Tables[table.Id!]
            .DataBodyRange
            .GetAsync(cancellationToken: cancellationToken);

        // Build headers
        var headers = new List<string>();
        if (headerRange?.Values is not null)
        {
            var headerMatrix = UntypedToMatrix(headerRange.Values);
            if (headerMatrix.Count > 0)
                headers = [.. headerMatrix[0].Select(h => (h ?? string.Empty).Trim())];
        }

        // Fallback if headers missing: derive from first body row
        if (headers.Count == 0)
        {
            if (bodyRange?.Values is null)
                return null;

            var bodyMatrix = UntypedToMatrix(bodyRange.Values);
            if (bodyMatrix.Count == 0)
                return null;

            var colCount = bodyMatrix[0].Count;
            for (int i = 0; i < colCount; i++) headers.Add($"Column{i + 1}");
        }

        // Collect rows
        var tableModel = new List<IDictionary<string, object?>>();

        if (bodyRange?.Values is not null)
        {
            var bodyMatrix = UntypedToMatrix(bodyRange.Values);
            foreach (var rowVals in bodyMatrix)
            {
                var row = new Dictionary<string, object?>(headers.Count, StringComparer.Ordinal);
                for (int c = 0; c < headers.Count; c++)
                {
                    var val = c < rowVals.Count ? rowVals[c] : null;
                    row[headers[c]] = val;

                }
                tableModel.Add(row);
            }
        }

        return tableModel;
    }

    /// <summary>
    /// Converts Graph SDK v5 WorkbookRange.Values (UntypedNode hierarchy) into a matrix of strings.
    /// </summary>
    public static List<List<string?>> UntypedToMatrix(this UntypedNode node)
    {
        var rows = new List<List<string?>>();
        if (node is UntypedArray rowsArray)
        {
            foreach (var rowNode in rowsArray.GetValue())
            {
                var row = new List<string?>();
                if (rowNode is UntypedArray colsArray)
                {
                    foreach (var colNode in colsArray.GetValue())
                        row.Add(UntypedToString(colNode));
                }
                else
                {
                    // Single value row fallback
                    row.Add(UntypedToString(rowNode));
                }
                rows.Add(row);
            }
        }
        else
        {
            // Single row fallback
            rows.Add([UntypedToString(node)]);
        }
        return rows;
    }

    private static string? UntypedToString(UntypedNode? node) =>
        node switch
        {
            null => null,
            UntypedString s => s.GetValue(),
            UntypedBoolean b => b.GetValue().ToString(),
            UntypedInteger i => i.GetValue().ToString(),
            UntypedLong l => l.GetValue().ToString(),
            UntypedFloat f => f.GetValue().ToString(System.Globalization.CultureInfo.InvariantCulture),
            UntypedDouble d => d.GetValue().ToString(System.Globalization.CultureInfo.InvariantCulture),
            UntypedDecimal dec => dec.GetValue().ToString(System.Globalization.CultureInfo.InvariantCulture),
            UntypedNull => null,
            UntypedObject o => o.ToString(),   // fallback
            UntypedArray a => a.ToString(),   // fallback
            _ => node.ToString()
        };



}
