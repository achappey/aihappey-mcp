using System.ComponentModel;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using MCPhappey.Core.Extensions;
using MCPhappey.Common.Extensions;
using PON = Plotly.NET;
using System.Text.Json.Nodes;

namespace MCPhappey.Tools.GitHub.Plotly;

public static class PlotlyService
{
    [Description("Create an interactive chart using Plotly.NET.")]
    [McpServerTool(
        Title = "Create a Plotly chart",
        Name = "plotly_create_chart",
        ReadOnly = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> Plotly_CreateChart(
        [Description("X values for the chart (comma-separated). Example: 1,2,3")]
        string xValues,

        [Description("Y values for the chart (comma-separated). Example: 3,1,6")]
        string yValues,

        [Description("Chart type to generate (point, line, bar).")]
        [DefaultValue("point")]
        string chartType = "point",

        [Description("Name of the data series.")]
        string? seriesName = "Series",

        RequestContext<CallToolRequestParams>? requestContext = null,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
        =>
        await requestContext!.WithExceptionCheck(async () =>
        await requestContext!.WithStructuredContent(async () =>
        {
            // Parse inputs
            var x = xValues.Split(',').Select(v => double.Parse(v.Trim())).ToArray();
            var y = yValues.Split(',').Select(v => double.Parse(v.Trim())).ToArray();

            // Choose chart type
            PON.GenericChart chart = chartType.ToLowerInvariant() switch
            {
                "line" => PON.CSharp.Chart.Line<double, double, string>(x, y, Name: seriesName!),
                "bar" => PON.CSharp.Chart.Column<double, double, string>(x, y, Name: seriesName!),
                _ => PON.CSharp.Chart.Point<double, double, string>(x, y, Name: seriesName!)
            };

            // Generate figure JSON
            string figureJson = PON.GenericChart.toFigureJson(chart);
            return JsonNode.Parse(figureJson);
        }));

    [Description("Create an interactive time-series Plotly chart using ISO timestamps.")]
    [McpServerTool(
        Title = "Create time-series chart",
        Name = "plotly_create_timeseries_chart",
        ReadOnly = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> Plotly_CreateTimeSeriesChart(
        [Description("ISO timestamps (comma-separated). Example: 2026-01-01,2026-01-02")]
            string timestamps,

        [Description("Values (comma-separated). Example: 10,15,8")]
            string values,

        [Description("Series name.")]
            string? seriesName = "Series",

        RequestContext<CallToolRequestParams>? requestContext = null) =>
        await requestContext!.WithExceptionCheck(async () =>
          await requestContext!.WithStructuredContent(async () =>
        {
            var x = timestamps.Split(',')
                .Select(v => DateTime.Parse(v.Trim()))
                .ToArray();

            var y = values.Split(',')
                .Select(v => double.Parse(v.Trim()))
                .ToArray();

            var chart =
                PON.CSharp.Chart.Line<DateTime, double, string>(
                    x.AsEnumerable(),
                    y.AsEnumerable(),
                    Name: seriesName!);

            var figureJson = PON.GenericChart.toFigureJson(chart);

            return JsonNode.Parse(figureJson);
        }));

}

