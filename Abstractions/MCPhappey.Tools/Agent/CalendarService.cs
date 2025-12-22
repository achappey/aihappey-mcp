using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Agent;

public static class CalendarService
{
    public class WeekInfo
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
    }

    public class Now
    {
        public required DateTimeOffset UtcNow { get; set; }
        public required long Ticks { get; set; }
        public required long UnixTimeSeconds { get; set; }
        public required string IsoString { get; set; } = default!;
        public required DayOfWeek DayOfWeek { get; set; }

    }

    [Description("Gets current date and time information")]
    [McpServerTool(ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    public static Task<Now> CalendarService_GetDateTimeNow()
    {
        var now = DateTimeOffset.UtcNow;
        
        return Task.FromResult(new Now
        {
            UtcNow = now,
            UnixTimeSeconds = now.ToUnixTimeSeconds(),
            Ticks = now.Ticks,
            IsoString = now.ToString("o"),
            DayOfWeek = now.DayOfWeek,
        });
    }


    [Description("Returns all week numbers for the month of the specified date, including the start and end date for each week that covers at least one day of the month. Week rule and first day of week are configurable.")]
    [McpServerTool(ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    public static async Task<Dictionary<int, WeekInfo>> CalendarService_GetWeeksForMonth(
           [Description("Any date within the desired month, e.g. 2025-04-11.")] DateTime date,
           [Description("Optional: Calendar week rule (FirstDay, FirstFullWeek, FirstFourDayWeek). Default is ISO (FirstFourDayWeek).")] CalendarWeekRule? weekRule = null,
           [Description("Optional: First day of week (e.g. Monday, Sunday). Default is Monday.")] DayOfWeek? firstDayOfWeek = null)
    {
        int year = date.Year;
        int month = date.Month;

        var rule = weekRule ?? CalendarWeekRule.FirstFourDayWeek;
        var firstDay = firstDayOfWeek ?? DayOfWeek.Monday;

        // Find first and last day of month
        var firstOfMonth = new DateTime(year, month, 1);
        var lastOfMonth = firstOfMonth.AddMonths(1).AddDays(-1);

        // Find first week start (on/before first of month)
        int daysToFirstDay = (7 + (int)firstOfMonth.DayOfWeek - (int)firstDay) % 7;
        var firstWeekStart = firstOfMonth.AddDays(-daysToFirstDay);

        // Find last week end (on/after last of month)
        int daysToLastDay = (7 - (int)lastOfMonth.DayOfWeek + (int)firstDay + 6) % 7;
        var lastWeekEnd = lastOfMonth.AddDays(daysToLastDay);

        var calendar = CultureInfo.InvariantCulture.Calendar;
        var weeks = new Dictionary<int, WeekInfo>();

        for (var weekStart = firstWeekStart; weekStart <= lastWeekEnd; weekStart = weekStart.AddDays(7))
        {
            var weekEnd = weekStart.AddDays(6);

            // Only include weeks that have at least one day in the month
            if (weekEnd < firstOfMonth || weekStart > lastOfMonth)
                continue;

            int weekNumber = calendar.GetWeekOfYear(weekStart, rule, firstDay);

            weeks[weekNumber] = new WeekInfo
            {
                StartDate = weekStart,
                EndDate = weekEnd
            };
        }

        return await Task.FromResult(weeks);
    }
}