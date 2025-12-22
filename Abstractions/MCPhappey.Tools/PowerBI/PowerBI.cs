using System.ComponentModel;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using Microsoft.PowerBI.Api;
using Microsoft.PowerBI.Api.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.PowerBI;

public static class PowerBI
{
    [Description("Executes a DAX query on a Power BI dataset via the ExecuteQueries endpoint.")]
    [McpServerTool(Name = "powerbi_execute_dax_query",
        Title = "Execute a DAX query on a Power BI dataset",
        Destructive = false,
        OpenWorld = false,
        ReadOnly = true)]
    public static async Task<CallToolResult?> PowerBI_ExecuteDaxQuery(
        [Description("PowerBI datasetId")] string datasetId,
        [Description("PowerBI DAX query")]
            string daxQuery,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
            await requestContext.WithExceptionCheck(async () =>
        {
            var mcpServer = requestContext.Server;
            PowerBIClient client = await serviceProvider.GetOboPowerBIClient(mcpServer);

            // Build the query request using PowerBI .NET SDK types
            DatasetExecuteQueriesRequest queryRequest = new()
            {
                Queries =
            [
                new() { Query = daxQuery }
            ]
            };

            // Call the API (for "My Workspace" datasets)
            var result = await client.Datasets.ExecuteQueriesAsync(datasetId, queryRequest, cancellationToken: cancellationToken);

            return Newtonsoft.Json.JsonConvert.SerializeObject(result)
                .ToJsonContent($"https://api.powerbi.com/v1.0/myorg/datasets/{datasetId}/executeQueries")
                .ToCallToolResult();
        });

    [Description("Creates a new Power BI streaming (push) dataset with the specified table schema.")]
    [McpServerTool(Name = "powerbi_create_streaming_dataset",
        Title = "Create a new Power BI streaming dataset",
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> PowerBI_CreateStreamingDataset(
    [Description("Dataset name")] string datasetName,
    [Description("Table name")] string tableName,
    [Description("Columns (name/type)")] List<PowerBIColumnSchema> columns,
    IServiceProvider serviceProvider,
    RequestContext<CallToolRequestParams> requestContext,
    CancellationToken cancellationToken = default) =>
            await requestContext.WithExceptionCheck(async () =>
    {
        var mcpServer = requestContext.Server;
        var client = await serviceProvider.GetOboPowerBIClient(mcpServer);

        // Build columns schema for API model
        var apiColumns = columns.Select(col => new Column
        {
            Name = col.Name,
            DataType = col.DataType // "string", "Int64", "Double", "Boolean", "DateTime"
        }).ToList();

        var table = new Table
        {
            Name = tableName,
            Columns = [.. apiColumns.Select(a => new Microsoft.PowerBI.Api.Models.Column()
            {
                Name = a.Name,
                DataType = a.DataType
            })]
        };

        // Mark as Push/Streaming dataset
        var createRequest = new CreateDatasetRequest
        {
            Name = datasetName,
            Tables = [table],
            DefaultMode = DatasetMode.Push
        };

        var response = await client.Datasets.PostDatasetAsync(createRequest, cancellationToken: cancellationToken);

        return Newtonsoft.Json.JsonConvert.SerializeObject(response)
                    .ToJsonContent($"https://api.powerbi.com/v1.0/myorg/datasets/{response.Id}")
                    .ToCallToolResult();
    });

    [Description("Adds rows to a table in a Power BI streaming (push) dataset, with automatic type detection and mapping.")]
    [McpServerTool(Name = "powerbi_add_rows_to_dataset_table",
        Title = "Add rows to a table in a Power BI streaming dataset",
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> PowerBI_AddRowsToDatasetTable(
        [Description("PowerBI dataset ID")] string datasetId,
        [Description("Table name")] string tableName,
        [Description("Rows to insert (each row as a dictionary of column name to value)")]
        List<Dictionary<string, object>> rows,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
            await requestContext.WithExceptionCheck(async () =>
    {
        var mcpServer = requestContext.Server;
        var client = await serviceProvider.GetOboPowerBIClient(mcpServer);

        if (rows == null || rows.Count == 0)
            throw new Exception("No rows provided.");

        // 1. Headers
        var headers = rows.First().Keys.ToList();

        // 2. Gather all values per column (for type detection)
        var columnData = new Dictionary<string, List<string>>();
        foreach (var header in headers)
            columnData[header] = [.. rows.Select(r => r[header]?.ToString() ?? "")];

        // 3. Detect types per column
        var columns = headers.Select(header => new Column
        {
            Name = header,
            DataType = columnData[header].DeterminePowerBIDataType()
        }).ToList();

        var columnTypes = columns.ToDictionary(c => c.Name, c => c.DataType);

        // 4. Parse values per cell according to detected type
        foreach (var row in rows)
        {
            foreach (var header in headers)
            {
                var value = row[header]?.ToString();
                var dataType = columnTypes[header];
                var parsedValue = value.ParseValue(dataType);
                row[header] = parsedValue ?? DBNull.Value;
            }
        }

        // 5. Make sure all values are native types (handle possible JsonElement etc)
        var sdkRows = rows.Select(a => a.ToNativeDictionary())
            .Cast<object>()
            .ToList();

        var rowRequest = new PostRowsRequest
        {
            Rows = sdkRows
        };

        await client.Datasets.PostRowsAsync(datasetId, tableName, rowRequest, cancellationToken: cancellationToken);

        return Newtonsoft.Json.JsonConvert.SerializeObject(rowRequest)
                           .ToJsonContent($"https://api.powerbi.com/v1.0/myorg/datasets/{datasetId}/tables/{tableName}/rows")
                           .ToCallToolResult();
    });

    // Minimal Column class for mapping
    public class Column
    {
        public string Name { get; set; } = "";
        public string DataType { get; set; } = "";
    }

    // Helper record for columns
    public record PowerBIColumnSchema(string Name, string DataType);


}