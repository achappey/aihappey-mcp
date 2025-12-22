using CsvHelper;
using System.Linq.Dynamic.Core;
using System.Globalization;
using CsvHelper.Configuration;

namespace MCPhappey.Tools.DataAnalysis;

public static partial class DataAnalysisCsvExtensions
{
   
    public static async Task<List<IDictionary<string, object?>>> CsvToDynamicRecordsAsync(this Stream csvStream, CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(csvStream);

        // Read first line to detect delimiter
        var headerLine = await reader.ReadLineAsync(cancellationToken);
        if (headerLine == null)
            return [];

        var delimiter = DetectDelimiter(headerLine);

        // Reset reader to start
        csvStream.Seek(0, SeekOrigin.Begin);
        reader.DiscardBufferedData();

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter,
            HasHeaderRecord = true,
            IgnoreBlankLines = true,
            MissingFieldFound = null,
            BadDataFound = null,
            TrimOptions = TrimOptions.Trim,
            DetectColumnCountChanges = false
        };

        using var csv = new CsvReader(reader, config);
        var records = new List<IDictionary<string, object?>>();

        await foreach (IDictionary<string, object?> record in csv.GetRecordsAsync<dynamic>(cancellationToken))
        {
            records.Add(record);
        }

        return records;
    }

    private static string DetectDelimiter(string headerLine)
    {
        // Heuristic: count potential delimiter chars
        var candidates = new[] { ";", ",", "\t", "|" };
        return candidates.OrderByDescending(d => headerLine.Count(c => c.ToString() == d)).FirstOrDefault() ?? ",";
    }

}
