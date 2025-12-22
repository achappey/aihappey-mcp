using System.ComponentModel;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CsvHelper;
using CsvHelper.Configuration;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.Workbooks;

public static partial class GraphWorkbooks
{
    [Description("Add a new worksheet to an Excel workbook on OneDrive/SharePoint. Optionally set a name and activate it.")]
    [McpServerTool(
        Title = "Add worksheet to Excel",
        Destructive = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphWorkbooks_AddWorksheet(
        string excelFileUrl,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional worksheet name. If omitted, Excel assigns a name.")]
            string? worksheetName = null,
        CancellationToken cancellationToken = default)
         => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async client =>
            await requestContext.WithStructuredContent(async () =>
    {
        var (typed, notAccepted, result) = await requestContext.Server.TryElicit(
            new GraphAddWorksheet
            {
                Name = worksheetName,
            },
            cancellationToken
        );

        // Resolve the DriveItem from the sharing/ODSP URL
        var driveItem = await client.GetDriveItem(excelFileUrl, cancellationToken);

        var addBody =
            new Microsoft.Graph.Beta.Drives.Item.Items.Item.Workbook.Worksheets.Add.AddPostRequestBody();

        if (!string.IsNullOrWhiteSpace(typed?.Name))
            addBody.Name = typed?.Name;

        // Add worksheet (Excel plaatst het aan het einde van de bestaande tabs)
        var newSheet = await client
            .Drives[driveItem!.ParentReference!.DriveId]
            .Items[driveItem.Id]
            .Workbook
            .Worksheets
            .Add
            .PostAsync(addBody, cancellationToken: cancellationToken);

        var workbookGraphUrl =
            $"https://graph.microsoft.com/beta/drives/{driveItem.ParentReference.DriveId}/items/{driveItem.Id}/workbook";

        return new
        {
            worksheetId = newSheet?.Id,
            name = newSheet?.Name,
            position = newSheet?.Position,
            workbookGraphUrl
        };
    })));

    [Description("Please fill in the details to add a worksheet to an Excel workbook.")]
    public class GraphAddWorksheet
    {
        [JsonPropertyName("name")]
        [Description("The name of the new worksheet.")]
        public string? Name { get; set; }
    }

    [Description("Add a new row to an Excel table on OneDrive/SharePoint. Use defaultValues dictionary to add default values to the Excel new row form.")]
    [McpServerTool(
            Title = "Add row to Excel table",
            OpenWorld = false)]
    public static async Task<CallToolResult?> GraphWorkbooks_AddRowToTable(
            string excelFileUrl,
            string tableName,
            IServiceProvider serviceProvider,
            RequestContext<CallToolRequestParams> requestContext,
            [Description("Default values for the row form. Format: key is row name, value is default value.")]
        Dictionary<string, string>? defaultValues = null,
            CancellationToken cancellationToken = default)
              => await requestContext.WithExceptionCheck(async () =>
                await requestContext.WithOboGraphClient(async (graphClient) =>
    {
        var driveItem = await graphClient.GetDriveItem(excelFileUrl, cancellationToken);
        var columnsResponse = await graphClient.Drives[driveItem?.ParentReference?.DriveId]
            .Items[driveItem?.Id]
            .Workbook
            .Tables[tableName].Columns
            .GetAsync(cancellationToken: cancellationToken);

        var columns = columnsResponse?.Value?.Select(col => col.Name).OfType<string>().ToList();

        if (defaultValues == null)
        {
            return $"defaultValues missing. Please provide some default values. Column names: {string.Join(",", columns ?? [])}"
                .ToErrorCallToolResponse();
        }

        // 2. Vraag de gebruiker om input per kolom (elicit)
        var elicited = await requestContext.Server.ElicitAsync(new ElicitRequestParams()
        {
            Message = "Please fill in the values of the Excel table",
            RequestedSchema = new ElicitRequestParams.RequestSchema()
            {
                Properties = columns?.ToDictionary(
                        a => a,
                        a => (ElicitRequestParams.PrimitiveSchemaDefinition)new ElicitRequestParams.StringSchema
                        {
                            Title = a,
                            Default = defaultValues?.ContainsKey(a) == true ? defaultValues[a] : null
                        }
                    ) ?? [],
            }

        }, cancellationToken);

        if (elicited.Action != "accept")
        {
            return elicited.Action.ToErrorCallToolResponse();
        }
        var valuesDict = ExtractValues(elicited.Content);
        var valuesNode = BuildValuesNode(columns!, valuesDict);

        var newRow = await graphClient.Drives[driveItem?.ParentReference?.DriveId]
            .Items[driveItem?.Id]
            .Workbook.Tables[tableName].Rows
            .PostAsync(new Microsoft.Graph.Beta.Models.WorkbookTableRow
            {
                Values = valuesNode
            }, cancellationToken: cancellationToken);

        var workbookGraphUrl = $"https://graph.microsoft.com/beta/drives/{driveItem?.ParentReference?.DriveId}/items/{driveItem?.Id}/workbook";

        return elicited.Content.ToJsonContentBlock(workbookGraphUrl).ToCallToolResult();
    }));

    private static readonly char[] charArray = [';', ',', '\t', '|'];

    [Description("Import a remote CSV (by URL) into a new Excel table on OneDrive/SharePoint. Downloads CSV, fills worksheet, and creates an Excel table.")]
    [McpServerTool(
        Title = "Create Excel table from remote CSV",
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphWorkbooks_CreateTableFromCsvLink(
        string excelFileUrl,
        string worksheetName,
        string csvUrl,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
            await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async client =>
    {
        var graphClient = await serviceProvider.GetGraphHttpClient(requestContext.Server);
        var driveItem = await client.GetDriveItem(excelFileUrl, cancellationToken);
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var csvRawFiles = await downloadService.ScrapeContentAsync(serviceProvider, requestContext.Server,
           csvUrl, cancellationToken);
        var csvRaw = csvRawFiles.Where(a => a.MimeType?.Equals("text/csv",
            StringComparison.OrdinalIgnoreCase) == true)
            .FirstOrDefault()?.Contents.ToString();

        if (string.IsNullOrEmpty(csvRaw))
        {
            return "CSV file empty".ToErrorCallToolResponse();
        }

        using var reader = new StringReader(csvRaw);

        // Auto-detect delimiter: leest eerste lijn, telt het meest gebruikte teken (je kunt ';', ',', '\t', '|' proberen)
        var sample = csvRaw.Split('\n').FirstOrDefault() ?? "";
        var delimiters = charArray;
        var detectedDelimiter = delimiters
            .OrderByDescending(d => sample.Count(c => c == d))
            .First();

        // Configure CsvHelper
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = detectedDelimiter.ToString(),
            Encoding = Encoding.UTF8,
            HasHeaderRecord = true,
            BadDataFound = null, // Ignore bad data
            IgnoreBlankLines = true
        };

        using var csv = new CsvReader(reader, config);

        // Lees de headers
        csv.Read();
        csv.ReadHeader();
        var headers = csv.HeaderRecord!;
        int columnCount = headers.Length;

        // Data rows
        var dataRows = new List<object[]>();
        while (csv.Read())
        {
            var row = new object[columnCount];
            for (int i = 0; i < columnCount; i++)
                row[i] = (csv.GetField(i) ?? string.Empty).Trim();
            dataRows.Add(row);
        }

        // Matrix: header + data
        var values = new object[dataRows.Count + 1][];
        values[0] = [.. headers.Cast<object>()];
        for (int i = 0; i < dataRows.Count; i++)
            values[i + 1] = dataRows[i];

        // Normaliseer breedte & nulls
        for (int r = 0; r < values.Length; r++)
        {
            var row = values[r] ?? [];
            if (row.Length != columnCount)
            {
                var fixedRow = new object[columnCount];
                Array.Copy(row, fixedRow, Math.Min(row.Length, columnCount));
                for (int j = row.Length; j < columnCount; j++) fixedRow[j] = "";
                row = fixedRow;
            }
            for (int j = 0; j < columnCount; j++)
                row[j] ??= "";
            values[r] = row;
        }
        int rowCount = values.Length;

        string lastColumn = GetExcelColumnName(columnCount);
        string address = $"A1:{lastColumn}{rowCount}";

        // 3. PATCH matrix naar worksheet-range (direct via Graph REST)
        var patchUrl = $"https://graph.microsoft.com/beta/drives/{driveItem?.ParentReference?.DriveId}/items/{driveItem?.Id}/workbook/worksheets/{worksheetName}/range(address='{address}')";
        var payload = new { values };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, MediaTypeNames.Application.Json);
        content.Headers.ContentType = new MediaTypeHeaderValue(MediaTypeNames.Application.Json);

        // Bearer-token van jouw graphClient (verondersteld geauthenticeerd!)
        if (graphClient.DefaultRequestHeaders.Authorization == null)
            throw new Exception("HttpClient mist Bearer token (DefaultRequestHeaders.Authorization)");

        var response = await graphClient.PatchAsync(patchUrl, content, cancellationToken);
        var dsds = await response.Content.ReadAsStringAsync();
        response.EnsureSuccessStatusCode();

        // 4. Maak een tabel aan over het bereik
        var tableUrl = $"https://graph.microsoft.com/beta/drives/{driveItem?.ParentReference?.DriveId}/items/{driveItem?.Id}/workbook/worksheets/{worksheetName}/tables/add";
        var tablePayload = new
        {
            address,
            hasHeaders = true
        };
        var tableContent = new StringContent(JsonSerializer.Serialize(tablePayload),
            Encoding.UTF8, MediaTypeNames.Application.Json);

        // Re-use authorized graphClient!
        var tableResp = await graphClient.PostAsync(tableUrl, tableContent, cancellationToken);

        tableResp.EnsureSuccessStatusCode();

        var addTableResult = JsonSerializer.Deserialize<JsonElement>(await tableResp.Content.ReadAsStringAsync());
        var tableName = addTableResult.GetProperty("name").GetString() ?? "UnknownTable";
        var workbookGraphUrl = $"https://graph.microsoft.com/beta/drives/{driveItem?.ParentReference?.DriveId}/items/{driveItem?.Id}/workbook/tables/{tableName}";

        return new { TableName = tableName, Address = address }
            .ToJsonContentBlock(workbookGraphUrl).ToCallToolResult();

    }));

    // Utility function as before
    private static string GetExcelColumnName(int columnNumber)
    {
        string columnName = "";
        while (columnNumber > 0)
        {
            int modulo = (columnNumber - 1) % 26;
            columnName = Convert.ToChar('A' + modulo) + columnName;
            columnNumber = (columnNumber - modulo) / 26;
        }
        return columnName;
    }

}
