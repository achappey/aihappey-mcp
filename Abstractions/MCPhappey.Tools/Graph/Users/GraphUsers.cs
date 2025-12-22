using System.ComponentModel;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using Microsoft.Graph.Beta.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.Users;

public static class GraphUsers
{
    [Description("List all users grouped by department.")]
    [McpServerTool(Title = "Group users by department", OpenWorld = false, ReadOnly = true)]
    public static async Task<CallToolResult?> GraphUsers_GroupUsersByDepartment(
            RequestContext<CallToolRequestParams> requestContext,
            [Description("Include users without a department (null/empty). Default is false.")]
            bool includeEmpty = false,
            [Description("Include disabled users. Default is false.")]
            bool includeDisabled = false,
            CancellationToken cancellationToken = default) =>
            await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async (client) =>
        {
            // Map: Department -> List of user display names (or IDs if displayName is empty)
            var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            // First page
            var page = await client.Users.GetAsync(req =>
            {
                req.QueryParameters.Select = new[] { "id", "userPrincipalName", "department" };
                req.QueryParameters.Top = 999; // big page size; paging handled below

                if (!includeDisabled)
                {
                    // Apply server-side filter
                    req.QueryParameters.Filter = "accountEnabled eq true";
                }

            }, cancellationToken);

            while (page != null)
            {
                foreach (var u in page.Value ?? Enumerable.Empty<User>())
                {
                    var dept = string.IsNullOrWhiteSpace(u?.Department) ? null : u!.Department!;
                    if (dept is null && !includeEmpty) continue;

                    var key = dept ?? "(none)";
                    if (!map.TryGetValue(key, out var list))
                    {
                        list = [];
                        map[key] = list;
                    }

                    if (!string.IsNullOrWhiteSpace(u?.UserPrincipalName))
                        list.Add(u.UserPrincipalName);
                }

                // Next page?
                if (!string.IsNullOrWhiteSpace(page.OdataNextLink))
                {
                    page = await client.Users.WithUrl(page.OdataNextLink).GetAsync(cancellationToken: cancellationToken);
                }
                else
                {
                    break;
                }
            }

            // Sort departments and names for stable output
            var ordered = map
                .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value.OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToList(),
                    StringComparer.OrdinalIgnoreCase
                );

            return ordered
                .ToJsonContentBlock("https://graph.microsoft.com/beta/users?$select=id,displayName,department")
                .ToCallToolResult();
        }));

}
