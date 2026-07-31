using System.ComponentModel;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.Licensing;

public static class GraphLicensing
{
    [Description("Provide the user and license SKU for assignment changes.")]
    private sealed class GraphLicenseChange
    {
        [Description("The user id or UPN.")]
        public string UserId { get; set; } = default!;

        [Description("The license SKU id (GUID).")]
        public string SkuId { get; set; } = default!;
    }

    [Description("Assign a license SKU to a user by SKU ID.")]
    [McpServerTool(Title = "Assign license to user",
        OpenWorld = false,
        Destructive = true,
        ReadOnly = false,
        Idempotent = false)]
    public static async Task<CallToolResult?> GraphUsers_AssignLicense(
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The user id or UPN.")] string userId,
        [Description("The license SKU id (GUID).")] string skuId,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
    {
        var (typed, _, _) = await requestContext.Server.TryElicit(
            new GraphLicenseChange
            {
                UserId = userId ?? string.Empty,
                SkuId = skuId ?? string.Empty
            },
            cancellationToken
        );

        var requestBody = new Microsoft.Graph.Beta.Users.Item.AssignLicense.AssignLicensePostRequestBody
        {
            AddLicenses =
            [
                new AssignedLicense
                {
                    SkuId = Guid.Parse(typed.SkuId)
                }
            ],
            RemoveLicenses = []
        };

        return await client.Users[typed.UserId].AssignLicense.PostAsync(requestBody, cancellationToken: cancellationToken);
    })));

    [Description("Revoke a license SKU from a user by SKU ID.")]
    [McpServerTool(Title = "Revoke license from user",
        OpenWorld = false,
        Destructive = true,
        ReadOnly = false,
        Idempotent = false)]
    public static async Task<CallToolResult?> GraphUsers_RevokeLicense(
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The user id or UPN.")] string userId,
        [Description("The license SKU id (GUID).")] string skuId,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
    {
        var (typed, _, _) = await requestContext.Server.TryElicit(
            new GraphLicenseChange
            {
                UserId = userId ?? string.Empty,
                SkuId = skuId ?? string.Empty
            },
            cancellationToken
        );

        var requestBody = new Microsoft.Graph.Beta.Users.Item.AssignLicense.AssignLicensePostRequestBody
        {
            AddLicenses = [],
            RemoveLicenses =
            [
                Guid.Parse(typed.SkuId)
            ]
        };

        return await client.Users[typed.UserId].AssignLicense.PostAsync(requestBody, cancellationToken: cancellationToken);
    })));


    [Description("Get user SKUs grouped by department. If departmentName is set, only include that department.")]
    [McpServerTool(
        Title = "Get user SKUs per department",
        Name = "graph_users_get_user_skus_per_department",
        UseStructuredContent = true,
        OutputSchemaType = typeof(UserSkuDepartmentList),
        ReadOnly = true,
        Idempotent = true,
        Destructive = false,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphUsers_GetUserSkusPerDepartment(
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional department name to filter by.")]
    string? departmentName = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
            await GetUserSkusPerDepartment(
                client,
                departmentName,
                cancellationToken
            )
        )));


    [Description("Render user SKUs grouped by department in a UI widget.")]
    [McpServerTool(
        Title = "User SKUs per department widget",
        Name = "graph_users_render_user_skus_per_department_widget",
        UseStructuredContent = true,
        OutputSchemaType = typeof(UserSkuDepartmentList),
        ReadOnly = true,
        Idempotent = true,
        Destructive = false,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphUsers_RenderUserSkusPerDepartmentWidget(
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional department name to render.")]
    string? departmentName = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
            await GetUserSkusPerDepartment(
                client,
                departmentName,
                cancellationToken
            )
        )));


    private static async Task<UserSkuDepartmentList> GetUserSkusPerDepartment(
        GraphServiceClient client,
        string? departmentName,
        CancellationToken cancellationToken)
    {
        var skuMap = await BuildSkuMap(client, cancellationToken);

        var result = new Dictionary<
            string,
            Dictionary<string, List<string>>
        >(StringComparer.OrdinalIgnoreCase);

        var filter = "userType eq 'Member' and accountEnabled eq true";

        if (!string.IsNullOrWhiteSpace(departmentName))
        {
            // Escape single quotes for the OData filter.
            var escapedDepartmentName = departmentName.Replace("'", "''");

            filter += $" and department eq '{escapedDepartmentName}'";
        }

        var users = await client.Users.GetAsync(config =>
        {
            config.QueryParameters.Filter = filter;
            config.QueryParameters.Select =
            [
                "userPrincipalName",
            "assignedLicenses",
            "department"
            ];
            config.QueryParameters.Top = 999;
        }, cancellationToken);

        foreach (var user in users?.Value ?? [])
        {
            if (string.IsNullOrWhiteSpace(user.UserPrincipalName))
                continue;

            if (string.IsNullOrEmpty(departmentName) &&
                !string.IsNullOrEmpty(user.Department))
            {
                continue;
            }

            var licenses = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase
            );

            foreach (var assignedLicense in user.AssignedLicenses ?? [])
            {
                if (assignedLicense.SkuId is not Guid skuId)
                    continue;

                if (skuMap.TryGetValue(skuId.ToString(), out var skuPartNumber))
                    licenses.Add(skuPartNumber);
            }

            if (licenses.Count == 0)
                continue;

            var department = string.IsNullOrWhiteSpace(user.Department)
                ? "(Blank)"
                : user.Department;

            if (!result.TryGetValue(department, out var departmentUsers))
            {
                departmentUsers = new Dictionary<string, List<string>>(
                    StringComparer.OrdinalIgnoreCase
                );

                result[department] = departmentUsers;
            }

            departmentUsers[user.UserPrincipalName] =
            [
                .. licenses.OrderBy(
                sku => sku,
                StringComparer.OrdinalIgnoreCase
            )
            ];
        }

        return new UserSkuDepartmentList
        {
            Departments =
            [
                .. result
                .OrderBy(
                    department => department.Key,
                    StringComparer.OrdinalIgnoreCase
                )
                .Select(department => new UserSkuDepartment
                {
                    Name = department.Key,
                    Users =
                    [
                        .. department.Value
                            .OrderBy(
                                user => user.Key,
                                StringComparer.OrdinalIgnoreCase
                            )
                            .Select(user => new UserSkuUser
                            {
                                UserId = user.Key,
                                Skus = user.Value
                            })
                    ]
                })
            ]
        };
    }


    private static async Task<Dictionary<string, string>> BuildSkuMap(
        GraphServiceClient client,
        CancellationToken cancellationToken)
    {
        var skuMap = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase
        );

        var skus = await client.SubscribedSkus.GetAsync(config =>
        {
            config.QueryParameters.Select =
            [
                "skuId",
            "skuPartNumber"
            ];
        }, cancellationToken);

        foreach (var sku in skus?.Value ?? [])
        {
            if (sku.SkuId is not Guid skuId ||
                string.IsNullOrWhiteSpace(sku.SkuPartNumber))
            {
                continue;
            }

            skuMap[skuId.ToString()] = sku.SkuPartNumber;
        }

        return skuMap;
    }


    public sealed class UserSkuDepartmentList
    {
        [JsonPropertyName("departments")]
        public List<UserSkuDepartment> Departments { get; set; } = [];
    }


    public sealed class UserSkuDepartment
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = default!;

        [JsonPropertyName("users")]
        public List<UserSkuUser> Users { get; set; } = [];
    }


    public sealed class UserSkuUser
    {
        [JsonPropertyName("userId")]
        public string UserId { get; set; } = default!;

        [JsonPropertyName("skus")]
        public List<string> Skus { get; set; } = [];
    }
}
