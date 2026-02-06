using System.ComponentModel;
using MCPhappey.Common.Extensions;
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
        [Description("The license SKU id (GUID)." )] string skuId,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
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

        await client.Users[typed.UserId].AssignLicense.PostAsync(requestBody, cancellationToken: cancellationToken);

        return new
        {
            userId = typed.UserId,
            skuId = typed.SkuId,
            action = "assign"
        };
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
        [Description("The license SKU id (GUID)." )] string skuId,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
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

        await client.Users[typed.UserId].AssignLicense.PostAsync(requestBody, cancellationToken: cancellationToken);

        return new
        {
            userId = typed.UserId,
            skuId = typed.SkuId,
            action = "revoke"
        };
    })));

    [Description("Get user SKUs grouped by department. If departmentName is set, only include that department. Users without department are grouped under empty string.")]
    [McpServerTool(Title = "User SKUs per department", ReadOnly = true,
        Idempotent = true, Destructive = false,
        Name = "graph_users_get_user_skus_per_department",
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphUsers_GetUserSkusPerDepartment(
            RequestContext<CallToolRequestParams> requestContext,
            string? departmentName = null,
            CancellationToken cancellationToken = default) =>
            await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async client =>
            await requestContext.WithStructuredContent(async () =>
    {
        var skuMap = await BuildSkuMap(client, cancellationToken);
        var result = new Dictionary<string, Dictionary<string, List<string>>>();

        // Only apply department filter if departmentName is provided and non-empty
        string? filter = "userType eq 'Member' and accountEnabled eq true";
        if (!string.IsNullOrEmpty(departmentName))
            filter += $" and department eq '{departmentName.Replace("'", "''")}'"; // SQL-escape any quotes

        var users = await client
            .Users
            .GetAsync(config =>
            {
                config.QueryParameters.Filter = filter;
                config.QueryParameters.Select = ["userPrincipalName", "assignedLicenses", "department"];
                config.QueryParameters.Top = 999;
            }, cancellationToken);

        foreach (var user in users?.Value ?? [])
        {
            var mail = user.UserPrincipalName;
            if (string.IsNullOrWhiteSpace(mail)) continue;

            if (string.IsNullOrEmpty(departmentName) && !string.IsNullOrEmpty(user.Department))
                continue;

            var dept = user.Department ?? "(Blank)";

            // Only needed when departmentName is null/empty, otherwise Graph already filtered

            if (!result.TryGetValue(dept, out var dict))
            {
                dict = [];
                result[dept] = dict;
            }

            var userLicenses = user.AssignedLicenses?
                .Where(l => l.SkuId != null && skuMap.ContainsKey(l.SkuId.ToString()!))
                .Select(l => skuMap[l.SkuId.ToString()!])
                .Distinct()
                .ToList() ?? [];

            if (userLicenses.Count > 0)
                dict[mail] = userLicenses;
        }

        return new
        {
            departments = result.Select(a => new
            {
                name = a.Key,
                users = a.Value.Select(z => new
                {
                    userId = z.Key,
                    skus = z.Value
                })
            })
        };
    })));


    private static async Task<Dictionary<string, string>> BuildSkuMap(GraphServiceClient client, CancellationToken cancellationToken)
    {
        var skuMap = new Dictionary<string, string>();
        var skus = await client
            .SubscribedSkus
            .GetAsync(config =>
            {
                config.QueryParameters.Select = ["skuId", "skuPartNumber"];
            }, cancellationToken);

        foreach (var sku in skus?.Value ?? [])
            if (!string.IsNullOrWhiteSpace(sku.SkuPartNumber) && sku.SkuId != null)
                skuMap[sku.SkuId.ToString()!] = sku.SkuPartNumber!;
        return skuMap;
    }

}
