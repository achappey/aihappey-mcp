using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Core.Extensions;

namespace MCPhappey.Tools.DataAnalysis;

public static partial class DataAnalysisPlugin
{
    [Description("Get a preview of data from a specific worksheet or table in an Excel file. Returns columns, row count, and sample rows for AI analysis.")]
    [McpServerTool(
        Title = "Get Excel data sample",
        ReadOnly = true)]
    public static async Task<CallToolResult?> DataAnalysis_GetExcelDataSample(
                 [Description("OneDrive/SharePoint sharing URL of the Excel file")]
             string excelFileUrl,
                 [Description("Name of the table")]
             string tableName,
                 IServiceProvider serviceProvider,
                 RequestContext<CallToolRequestParams> requestContext,
                 [Description("Number of data rows")]
             int? numberOfRows = 5,
                [Description("Number of data rows to skip")]
                    int? numberOfSkipRows = 0,
                 CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(excelFileUrl))
            return "Excel file URL is required.".ToErrorCallToolResponse();

        using var graphClient = await serviceProvider.GetOboGraphClient(requestContext.Server);

        try
        {
            // Resolve DriveItem from sharing URL
            var driveItem = await graphClient.GetDriveItem(excelFileUrl, cancellationToken);
            if (driveItem is null)
                return "Could not resolve DriveItem from the provided URL.".ToErrorCallToolResponse();

            var table = await driveItem.ExcelToGenericTable(tableName, serviceProvider, requestContext, cancellationToken);

            return table.ToDataSample(excelFileUrl, numberOfRows, numberOfSkipRows);
        }
        catch (Exception ex)
        {
            return ex.Message.ToErrorCallToolResponse();
        }
    }

    [Description("Get a preview of data from a CSV file, including columns, row count, and sample rows for AI analysis.")]
    [McpServerTool(
        Title = "Get CSV data sample",
        OpenWorld = false,
        ReadOnly = true)]
    public static async Task<CallToolResult?> DataAnalysis_GetCSVDataSample(
         [Description("Url of the CSV file")]
            string csvFileUrl,
         IServiceProvider serviceProvider,
         RequestContext<CallToolRequestParams> requestContext,
         [Description("Number of data rows")]
            int? numberOfRows = 5,
         [Description("Number of data rows to skip")]
            int? numberOfSkipRows = 0,
         CancellationToken cancellationToken = default)
         => await requestContext.WithExceptionCheck(async () =>
    {
        ArgumentNullException.ThrowIfNullOrWhiteSpace(csvFileUrl);

        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var csvRawFiles = await downloadService.ScrapeContentAsync(serviceProvider, requestContext.Server,
           csvFileUrl, cancellationToken);
        var csvRaw = csvRawFiles.Where(a => a.MimeType?.Equals("text/csv",
            StringComparison.OrdinalIgnoreCase) == true)
            .FirstOrDefault()?.Contents;

        if (csvRaw == null || csvRaw.IsEmpty)
            return "No content found".ToErrorCallToolResponse();

        var data = await csvRaw.ToStream().CsvToDynamicRecordsAsync(cancellationToken);

        if (data == null)
            return "No content found".ToErrorCallToolResponse();

        return data.ToDataSample(csvFileUrl, numberOfRows, numberOfSkipRows);

    });



    [Description("Execute a compact but powerful analytics query with filtering, grouping, aggregates, sorting and projection on a CSV file.")]
    [McpServerTool(
     Title = "Run CSV analytics query",
     Destructive = false,
     ReadOnly = true)]
    public static async Task<CallToolResult?> DataAnalysis_ExecuteCSVDataQuery(
     [Description("OneDrive/SharePoint sharing URL of the CSV file")]
        string csvFileUrl,
         IServiceProvider serviceProvider,
         RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional per-row expressions to create new columns before filtering/grouping, e.g. { \"Year\": \"int.Parse(Date.Substring(0, 4))\" }")]
        Dictionary<string, string>? compute = null,
        [Description("Optional filter expression using LINQ syntax, e.g. 'Country == \"NL\" && Revenue > 10000'")]
        string? filter = null,
        [Description("Optional list of column names to group by")]
        List<string>? groupBy = null,
        [Description("Optional dictionary of aggregate expressions like { \"Total\": \"Sum(Value)\" }")]
        Dictionary<string, string>? aggregate = null,
        [Description("Optional sort expression, e.g. 'Total descending'")]
        string? sort = null,
        [Description("Optional max number of rows to return")]
        int? limit = null,
        [Description("Optional list of fields to include in final output")]
        List<string>? select = null,
     CancellationToken cancellationToken = default)
    {

        try
        {
            var downloadService = serviceProvider.GetRequiredService<DownloadService>();
            var csvRawFiles = await downloadService.DownloadContentAsync(
                serviceProvider, requestContext.Server, csvFileUrl, cancellationToken);

            var csvRaw = csvRawFiles
                .FirstOrDefault(a => a.MimeType?.Equals("text/csv", StringComparison.OrdinalIgnoreCase) == true)
                ?.Contents;

            if (csvRaw == null || csvRaw.Length == 0)
                return "No content found".ToErrorCallToolResponse();

            var data = await csvRaw.ToStream().CsvToDynamicRecordsAsync(cancellationToken);

            if (data == null)
                return "No content found".ToErrorCallToolResponse();

            var result = data.ExecuteDataQuery(
                compute: compute,
                filter: filter,
                groupBy: groupBy,
                aggregate: aggregate,
                sort: sort,
                limit: limit,
                select: select
            );

            return result.ToJsonContentBlock(csvFileUrl)
                .ToCallToolResult();

        }
        catch (Exception e)
        {
            return (e.Message + e.StackTrace).ToErrorCallToolResponse();
        }
    }

    [Description("Execute a compact but powerful analytics query with filtering, grouping, aggregates, sorting and projection on a Excel file.")]
    [McpServerTool(
        Title = "Run Excel analytics query",
        Destructive = false,
        ReadOnly = true)]
    public static async Task<CallToolResult?> DataAnalysis_ExecuteExcelDataQuery(
        string excelFileUrl,
        string excelTableName,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        Dictionary<string, string>? compute = null,
        string? filter = null,
        List<string>? groupBy = null,
        Dictionary<string, string>? aggregate = null,
        string? sort = null,
        int? limit = null,
        List<string>? select = null,
        CancellationToken cancellationToken = default)
    {

        try
        {

            if (string.IsNullOrWhiteSpace(excelFileUrl))
                return "Excel file URL is required.".ToErrorCallToolResponse();

            using var graphClient = await serviceProvider.GetOboGraphClient(requestContext.Server);

            // Resolve DriveItem from sharing URL
            var driveItem = await graphClient.GetDriveItem(excelFileUrl, cancellationToken);
            if (driveItem is null)
                return "Could not resolve DriveItem from the provided URL.".ToErrorCallToolResponse();

            var table = await driveItem.ExcelToGenericTable(excelTableName, serviceProvider, requestContext, cancellationToken);

            var result = table?.ExecuteDataQuery(
                          compute: compute,
                          filter: filter,
                          groupBy: groupBy,
                          aggregate: aggregate,
                          sort: sort,
                          limit: limit,
                          select: select
                      );

            return result.ToJsonContentBlock(excelFileUrl)
                .ToCallToolResult();
        }
        catch (Exception e)
        {
            return (e.Message + e.StackTrace).ToErrorCallToolResponse();
        }
    }

}
