using CsvHelper;
using System.Globalization;

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

        var options = new CsvReaderOptions
        {
            Separator = delimiter[0],
            FormatProvider = CultureInfo.InvariantCulture,
            HasHeader = true,
            SkipEmptyLines = true,
            MissingField = CsvMissingFieldHandling.ReturnEmpty,
            MalformedData = CsvMalformedDataHandling.Recover,
            TrimWhitespace = true,
            FieldCountMismatch = CsvFieldCountHandling.Allow
        };

        using var csv = CsvReader.Create(reader, options);
        var records = new List<IDictionary<string, object?>>();

        await foreach (var record in csv.GetRecordsAsync<CsvDynamicRecord>(cancellationToken))
        {
            records.Add(record.Names
                .Select((name, index) => new KeyValuePair<string, object?>(
                    name, index < record.FieldCount ? record[index] : null))
                .ToDictionary());
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
