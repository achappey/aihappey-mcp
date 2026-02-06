using MCPhappey.Common;
using MCPhappey.Common.Models;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using MCPhappey.Core.Extensions;

namespace MCPhappey.Tools.Graph;

public class GraphCompletion : IAutoCompletion
{
    public bool SupportsHost(ServerConfig serverConfig)
        => serverConfig.Server.ServerInfo.Name.StartsWith("Microsoft-");

    public async Task<Completion> GetCompletion(
     McpServer mcpServer,
     IServiceProvider serviceProvider,
     CompleteRequestParams? completeRequestParams,
     CancellationToken cancellationToken = default)
    {
        if (completeRequestParams?.Argument?.Name is not string argName || completeRequestParams.Argument.Value is not string argValue)
            return new();

        using var client = await serviceProvider.GetOboGraphClient(mcpServer);

        IEnumerable<string> result = [];

        switch (completeRequestParams.Argument.Name)
        {
            case "appName":
                var apps = await client.Applications.GetAsync(requestConfiguration =>
                {
                    if (!string.IsNullOrWhiteSpace(argValue))
                        requestConfiguration.QueryParameters.Filter = $"startswith(displayName,'{argValue.Replace("'", "''")}')";
                    requestConfiguration.QueryParameters.Top = 100;
                }, cancellationToken);

                result = apps?.Value?.Select(a => a.DisplayName)
                                        .OfType<string>()
                                        .Order()
                                        .ToList() ?? [];
                break;

            case "roleName":
                var roles = await client.DirectoryRoles.GetAsync(requestConfiguration =>
                {
                    if (!string.IsNullOrWhiteSpace(argValue))
                        requestConfiguration.QueryParameters.Filter = $"startswith(displayName,'{argValue.Replace("'", "''")}')";

                }, cancellationToken);

                result = roles?.Value?.Select(r => r.DisplayName)
                                        .OfType<string>()
                                        .Order()
                                        .ToList() ?? [];
                break;
            case "mail":
                var messages = await client.Me.Messages.GetAsync(requestConfiguration =>
                {
                    requestConfiguration.QueryParameters.Top = 100;
                    requestConfiguration.QueryParameters.Select = ["subject", "from"];
                    if (!string.IsNullOrWhiteSpace(argValue))
                    {
                        // Search works across subject, body, from, etc.
                        requestConfiguration.QueryParameters.Search = $"\"{argValue}\"";
                    }
                }, cancellationToken);

                result = messages?.Value?
                .Select(m =>
                {
                    var from = m.From?.EmailAddress;
                    var sender = from == null ? null :
                        string.IsNullOrWhiteSpace(from.Name)
                            ? from.Address
                            : $"{from.Name} <{from.Address}>";

                    // Prefer subject if available, else sender
                    return !string.IsNullOrWhiteSpace(m.Subject)
                        ? $"{m.Subject} — {sender}"
                        : sender;
                })
                        .OfType<string>()
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Distinct()
                        .Take(100)
                        .ToList() ?? [];
                break;
            case "calendarEventSeries":
                var escapedSeries = argValue?.Replace("'", "''") ?? "";

                var series = await client.Me.Events.GetAsync(rc =>
                {
                    rc.QueryParameters.Top = 100;
                    rc.QueryParameters.Select = ["subject", "organizer", "start", "end", "location", "type"];
                    rc.QueryParameters.Orderby = ["start/dateTime asc"];

                    // Base filter: only recurring series masters
                    var baseFilter = "type eq 'seriesMaster'";

                    if (string.IsNullOrWhiteSpace(argValue))
                    {
                        // Upcoming series only
                        var now = DateTimeOffset.UtcNow.ToString("o");
                        rc.QueryParameters.Filter = $"{baseFilter} and start/dateTime ge '{now}'";
                    }
                    else
                    {
                        // Fast prefix-style filtering on common fields
                        rc.QueryParameters.Filter =
                            $"{baseFilter} and (" +
                            $"startswith(subject,'{escapedSeries}') or " +
                            $"startswith(organizer/emailAddress/name,'{escapedSeries}') or " +
                            $"startswith(location/displayName,'{escapedSeries}'))";
                    }
                }, cancellationToken);

                result = series?.Value?
                    .Select(e =>
                    {
                        var subj = string.IsNullOrWhiteSpace(e.Subject) ? "(no subject)" : e.Subject;
                        var start = e.Start?.DateTime;
                        var tz = e.Start?.TimeZone;
                        var when = !string.IsNullOrWhiteSpace(start) && !string.IsNullOrWhiteSpace(tz)
                                    ? $"{start} {tz}"
                                    : start ?? "";
                        var org = e.Organizer?.EmailAddress;
                        var organizer = org == null
                            ? ""
                            : (string.IsNullOrWhiteSpace(org.Name) ? org.Address : $"{org.Name} <{org.Address}>");
                        var loc = e.Location?.DisplayName;

                        // Tag as series for clarity
                        var tag = "[series]";

                        return string.Join(" — ", new[] { subj, when, organizer, loc, tag }.Where(s => !string.IsNullOrWhiteSpace(s)));
                    })
                    .OfType<string>()
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct()
                    .Take(100)
                    .ToList() ?? [];
                break;

            case "calendarEvent":
                var escaped = argValue?.Replace("'", "''") ?? "";

                var events = await client.Me.Events.GetAsync(rc =>
                {
                    rc.QueryParameters.Top = 100;
                    rc.QueryParameters.Select = ["subject", "organizer", "start", "end", "location"];
                    rc.QueryParameters.Orderby = ["start/dateTime asc"];

                    if (string.IsNullOrWhiteSpace(argValue))
                    {
                        // Upcoming events only
                        var now = DateTimeOffset.UtcNow.ToString("o");
                        rc.QueryParameters.Filter = $"start/dateTime ge '{now}'";
                    }
                    else
                    {
                        // Lightweight filter across common fields
                        rc.QueryParameters.Filter =
                            $"startswith(subject,'{escaped}') or " +
                            $"startswith(organizer/emailAddress/name,'{escaped}') or " +
                            $"startswith(location/displayName,'{escaped}')";
                    }
                }, cancellationToken);

                result = events?.Value?
                    .Select(e =>
                    {
                        var subj = string.IsNullOrWhiteSpace(e.Subject) ? "(no subject)" : e.Subject;
                        var start = e.Start?.DateTime;
                        var tz = e.Start?.TimeZone;
                        var when = !string.IsNullOrWhiteSpace(start) && !string.IsNullOrWhiteSpace(tz)
                                    ? $"{start} {tz}"
                                    : start ?? "";
                        var org = e.Organizer?.EmailAddress;
                        var organizer = org == null
                            ? ""
                            : (string.IsNullOrWhiteSpace(org.Name) ? org.Address : $"{org.Name} <{org.Address}>");
                        var loc = e.Location?.DisplayName;

                        // Nice, compact line for autocomplete
                        return string.Join(" — ", new[] { subj, when, organizer, loc }.Where(s => !string.IsNullOrWhiteSpace(s)));
                    })
                    .OfType<string>()
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct()
                    .Take(100)
                    .ToList() ?? [];
                break;

            case "siteName":
                var sites = await client.Sites.GetAsync(requestConfiguration =>
                {
                    if (!string.IsNullOrWhiteSpace(argValue))
                        requestConfiguration.QueryParameters.Filter = $"startswith(displayName,'{argValue.Replace("'", "''")}')";
                    requestConfiguration.QueryParameters.Top = 100;
                }, cancellationToken);

                result = sites?.Value?.Select(s => s.DisplayName)
                                       .OfType<string>()
                                       .Order()
                                       .ToList() ?? [];
                break;
            case "driveName":
                var drives = await client.Drives.GetAsync(requestConfiguration =>
                {
                    if (!string.IsNullOrWhiteSpace(argValue))
                        requestConfiguration.QueryParameters.Filter = $"startswith(name,'{argValue.Replace("'", "''")}')";
                    requestConfiguration.QueryParameters.Top = 100;
                }, cancellationToken);

                result = drives?.Value?.Select(d => d.Name)
                                        .OfType<string>()
                                        .Order()
                                        .ToList() ?? [];
                break;


            case "teamName":
                var teams = await client.Teams.GetAsync((requestConfiguration) =>
                {
                    if (!string.IsNullOrWhiteSpace(argValue))
                    {
                        requestConfiguration.QueryParameters.Filter = $"startswith(displayName,'{argValue.Replace("'", "''")}')";
                    }

                    requestConfiguration.QueryParameters.Top = 100;
                }, cancellationToken);

                result = teams?.Value?.Select(a => a.DisplayName)
                                            .OfType<string>()
                                            .ToList() ?? [];
                break;
            case "userPrincipalName":
                // UPN/email; returns userPrincipalName for autocompletion
                var userNameUsers = await client.Users.GetAsync(requestConfiguration =>
                {
                    if (!string.IsNullOrWhiteSpace(argValue))
                        requestConfiguration.QueryParameters.Filter = $"startswith(userPrincipalName,'{argValue.Replace("'", "''")}')";
                    requestConfiguration.QueryParameters.Top = 100;
                    requestConfiguration.QueryParameters.Select = ["userPrincipalName"];
                }, cancellationToken);

                result = userNameUsers?.Value?
                            .Select(u => u.UserPrincipalName)
                            .OfType<string>()
                            .Order()
                            .Take(100)
                            .ToList() ?? [];
                break;


            case "userDisplayName":
                // DisplayName; returns DisplayName for autocompletion
                var displayNameUsers = await client.Users.GetAsync(requestConfiguration =>
                {
                    if (!string.IsNullOrWhiteSpace(argValue))
                        requestConfiguration.QueryParameters.Filter = $"startswith(displayName,'{argValue.Replace("'", "''")}')";
                    requestConfiguration.QueryParameters.Top = 100;
                    requestConfiguration.QueryParameters.Select = ["displayName"];
                }, cancellationToken);

                result = displayNameUsers?.Value?
                            .Select(u => u.DisplayName)
                            .OfType<string>()
                            .Order()
                            .ToList() ?? [];
                break;

            case "departmentName":
                var users = await client.Users.GetAsync(requestConfiguration =>
                {
                    requestConfiguration.QueryParameters.Top = 999;
                    requestConfiguration.QueryParameters.Select = ["department"];
                }, cancellationToken);

                result = users?.Value?
                    .Where(u => !string.IsNullOrWhiteSpace(u.Department))
                    .GroupBy(u => u.Department)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Key)
                    .OfType<string>()
                    .Order()
                    .Take(100)
                    .ToList() ?? [];

                // Optionally filter by argValue for autocomplete
                if (!string.IsNullOrWhiteSpace(argValue))
                    result = [.. result.Where(d => d.Contains(argValue, StringComparison.OrdinalIgnoreCase))];

                break;

            case "plannerName":
                var plans = await client.Me.Planner.Plans.GetAsync(cancellationToken: cancellationToken);

                var items = plans?.Value ?? [];
                if (!string.IsNullOrWhiteSpace(argValue))
                    items = [.. items.Where(d => d.Title?.Contains(argValue, StringComparison.OrdinalIgnoreCase) == true)];

                result = items.Select(p => p.Title)
                                        .OfType<string>()
                                        .Take(100)
                                        .ToList() ?? [];
                break;

            case "groupName":
                var groups = await client.Groups.GetAsync((requestConfiguration) =>
                {
                    if (!string.IsNullOrWhiteSpace(argValue))
                    {
                        requestConfiguration.QueryParameters.Filter = $"startswith(displayName,'{argValue.Replace("'", "''")}')";
                    }
                    requestConfiguration.QueryParameters.Top = 100;
                    requestConfiguration.QueryParameters.Select = ["id", "displayName"];
                }, cancellationToken);

                result = groups?.Value?.Select(g => g.DisplayName)
                                        .OfType<string>()
                                        .ToList() ?? [];
                break;
            case "groupId":
                var groupsById = await client.Groups.GetAsync((requestConfiguration) =>
                {
                    if (!string.IsNullOrWhiteSpace(argValue))
                    {
                        requestConfiguration.QueryParameters.Filter = $"startswith(displayName,'{argValue.Replace("'", "''")}')";
                    }
                    requestConfiguration.QueryParameters.Top = 100;
                    requestConfiguration.QueryParameters.Select = ["id", "displayName"];
                }, cancellationToken);

                result = groupsById?.Value?
                            .Select(g => g.Id)
                            .Where(id => !string.IsNullOrWhiteSpace(id))
                            .OfType<string>()
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList() ?? [];
                break;
            case "securityGroupName":
                var securityGroups = await client.Groups.GetAsync((requestConfiguration) =>
                {
                    var escaped = argValue?.Replace("'", "''") ?? "";

                    var filters = new List<string>
                    {
                        "securityEnabled eq true",
                        "mailEnabled eq false"
                    };

                    if (!string.IsNullOrWhiteSpace(escaped))
                    {
                        filters.Add($"startswith(displayName,'{escaped}')");
                    }

                    requestConfiguration.QueryParameters.Filter = string.Join(" and ", filters);

                    requestConfiguration.QueryParameters.Top = 100;
                }, cancellationToken);

                result = securityGroups?.Value?.Select(g => g.DisplayName)
                                        .OfType<string>()
                                        .ToList() ?? [];
                break;

            default:
                break;
        }

        return new Completion()
        {
            Values = [.. result]
        };

    }

    public IEnumerable<string> GetArguments(IServiceProvider serviceProvider)
    {
        return
        [
        "appName",
        "roleName",
        "mail",
        "calendarEventSeries",
        "calendarEvent",
        "siteName",
        "driveName",
        "teamName",
        "userPrincipalName",
        "userDisplayName",
        "departmentName",
        "plannerName",
        "groupName",
        "groupId",
        "securityGroupName"
    ];
    }

}
