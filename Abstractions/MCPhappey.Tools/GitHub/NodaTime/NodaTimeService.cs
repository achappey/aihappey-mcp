using System.ComponentModel;
using ModelContextProtocol.Server;
using NodaTime;
using NodaTime.Text;

namespace MCPhappey.Tools.GitHub.NodaTime;

public static class NodaTimeService
{

    [Description("Converts a local date/time in a given time zone to a UTC Instant.")]
    [McpServerTool(
      Title = "Convert local time to UTC",
      Name = "github_nodatime_local_to_instant",
      ReadOnly = true,
      OpenWorld = false,
      UseStructuredContent = true)]
    public static async Task<string?> GitHubNodaTime_LocalToInstant(
      [Description("Local date/time (e.g. '2025-10-17T10:30:00')")] string localDateTime,
      [Description("IANA time zone ID (e.g. 'Europe/Amsterdam')")] string timeZone)
    {
        var local = LocalDateTimePattern.GeneralIso.Parse(localDateTime);
        if (!local.Success) return "Invalid LocalDateTime format.";

        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(timeZone);
        if (zone is null) return $"Unknown time zone: {timeZone}";

        var zoned = zone.AtStrictly(local.Value);
        return await Task.FromResult(zoned.ToInstant().ToString());
    }

    [Description("Adds or subtracts a time period (e.g. days, months) to a given local date.")]
    [McpServerTool(
        Title = "Add or subtract period from date",
        Name = "github_nodatime_add_period",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    public static async Task<string?> GitHubNodaTime_AddPeriod(
        [Description("Start date (e.g. '2025-10-17')")] string date,
        [Description("Number of days to add (negative to subtract)")] int days = 0,
        [Description("Number of months to add (negative to subtract)")] int months = 0,
        [Description("Number of years to add (negative to subtract)")] int years = 0)
    {
        var parsed = LocalDatePattern.Iso.Parse(date);
        if (!parsed.Success) return "Invalid date format.";

        var result = parsed.Value.Plus(Period.FromYears(years) + Period.FromMonths(months) + Period.FromDays(days));
        return await Task.FromResult(result.ToString("yyyy-MM-dd", null));
    }

    [Description("Lists all available IANA time zones known to NodaTime.")]
    [McpServerTool(
        Title = "List time zones",
        Name = "github_nodatime_list_zones",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    public static async Task<string[]> GitHubNodaTime_ListZones() =>
        await Task.FromResult(DateTimeZoneProviders.Tzdb.Ids.ToArray());

    [Description("Gets the current local time for a given time zone.")]
    [McpServerTool(
        Title = "Current local time in zone",
        Name = "github_nodatime_current_localtime",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    public static async Task<string?> GitHubNodaTime_CurrentLocalTime(
        [Description("IANA time zone ID (e.g. 'America/New_York')")] string timeZone)
    {
        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(timeZone);
        if (zone is null) return $"Unknown time zone: {timeZone}";

        var now = SystemClock.Instance.GetCurrentInstant().InZone(zone);
        return await Task.FromResult(now.ToString());
    }

    [Description("Calculates the number of business days between two dates (excluding weekends).")]
    [McpServerTool(
        Title = "Business days between dates",
        Name = "github_nodatime_business_days_between",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    public static async Task<int> GitHubNodaTime_BusinessDaysBetween(
        [Description("Start date (inclusive, e.g. '2025-10-01')")] string startDate,
        [Description("End date (exclusive, e.g. '2025-10-17')")] string endDate)
    {
        var start = LocalDatePattern.Iso.Parse(startDate);
        var end = LocalDatePattern.Iso.Parse(endDate);
        if (!start.Success || !end.Success) return -1;

        var count = 0;
        for (var date = start.Value; date < end.Value; date = date.PlusDays(1))
        {
            var isoDay = date.DayOfWeek;
            if (isoDay != IsoDayOfWeek.Saturday && isoDay != IsoDayOfWeek.Sunday)
                count++;
        }
        return await Task.FromResult(count);
    }


    [Description("Converts UTC Instant to local time in a given time zone.")]
    [McpServerTool(
        Title = "Convert Instant to local time",
        Name = "github_nodatime_instant_to_local",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    public static async Task<string?> GitHubNodaTime_InstantToLocal(
        [Description("UTC instant (e.g. '2025-10-17T08:30:00Z')")] string instant,
        [Description("IANA time zone ID (e.g. 'Europe/Amsterdam')")] string timeZone)
    {
        var parsed = InstantPattern.General.Parse(instant);
        if (!parsed.Success) return "Invalid Instant format.";

        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(timeZone);
        if (zone is null) return $"Unknown time zone: {timeZone}";

        var local = parsed.Value.InZone(zone);
        return await Task.FromResult(local.ToString());
    }

    [Description("Calculates the duration between two instants in human-readable form.")]
    [McpServerTool(
        Title = "Duration between instants",
        Name = "github_nodatime_duration_between",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    public static async Task<string?> GitHubNodaTime_DurationBetween(
        [Description("Start instant (e.g. '2025-10-17T08:30:00Z')")] string start,
        [Description("End instant (e.g. '2025-10-17T10:45:00Z')")] string end)
    {
        var s = InstantPattern.General.Parse(start);
        var e = InstantPattern.General.Parse(end);
        if (!s.Success || !e.Success) return "Invalid Instant input.";

        var duration = e.Value - s.Value;
        return await Task.FromResult(duration.ToString()); // e.g. "PT2H15M"
    }

    [Description("Humanizes the duration between two instants (e.g. '2 hours, 15 minutes ago').")]
    [McpServerTool(
        Title = "Humanize duration between instants",
        Name = "github_nodatime_humanize_duration",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    public static async Task<string?> GitHubNodaTime_HumanizeDuration(
        [Description("Earlier instant (e.g. '2025-10-17T08:30:00Z')")] string earlier,
        [Description("Later instant (e.g. '2025-10-17T10:45:00Z')")] string later)
    {
        var e1 = InstantPattern.General.Parse(earlier);
        var e2 = InstantPattern.General.Parse(later);
        if (!e1.Success || !e2.Success) return "Invalid Instant input.";

        var duration = e2.Value - e1.Value;

        // Simple readable version
        var totalMinutes = duration.TotalMinutes;
        var text = totalMinutes switch
        {
            < 1 => $"{duration.TotalSeconds:F0} seconds",
            < 60 => $"{duration.TotalMinutes:F0} minutes",
            < 1440 => $"{duration.TotalHours:F1} hours",
            _ => $"{duration.TotalDays:F1} days"
        };

        return await Task.FromResult(text);
    }

    [Description("The current UTC Instant (now).")]
    [McpServerTool(
        Title = "Get current Instant",
        Name = "github_nodatime_now",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    public static async Task<string?> GitHubNodaTime_Now() =>
        await Task.FromResult(SystemClock.Instance.GetCurrentInstant().ToString());
}
