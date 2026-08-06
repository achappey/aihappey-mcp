using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Auth.Extensions;
using MCPhappey.Auth.Models;
using MCPhappey.Common;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.SharePoint;

public static partial class SharePointREST
{
    [Description(
    "Add a user, Microsoft Entra security group, or Microsoft 365 group " +
    "to the associated Visitors or Editors group of a SharePoint site.")]
    [McpServerTool(
    Title = "Add principal to SharePoint site group",
    Name = "sharepoint_add_principal_to_site_group",
    Destructive = true,
    Idempotent = true,
    OpenWorld = false)]
    public static async Task<CallToolResult?>
    SharePointRest_AddPrincipalToSiteGroup(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,

        [Description(
            "Exact SharePoint web URL, e.g. " +
            "https://contoso.sharepoint.com/sites/project.")]
        string? siteUrl = null,

        [Description(
            "Target SharePoint group. Visitors grants read access; " +
            "Editors uses the site's associated Members group.")]
        SharePointAssociatedGroupType? targetGroup = null,

        [Description(
            "Type of principal being added.")]
        SharePointPrincipalInputType? principalType = null,

        [Description(
            "For User: the user's UPN or email address. " +
            "For EntraSecurityGroup or Microsoft365Group: the Entra object ID.")]
        string? principal = null,

        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (typed, notAccepted, _) =
                await requestContext.Server.TryElicit(
                    new SharePointAddPrincipalToSiteGroupInput
                    {
                        SiteUrl = siteUrl,
                        TargetGroup = targetGroup,
                        PrincipalType = principalType,
                        Principal = principal
                    },
                    cancellationToken);

            if (notAccepted is not null)
            {
                throw new Exception(
                    JsonSerializer.Serialize(notAccepted));
            }

            typed!.Validate();

            var tokenService =
                serviceProvider.GetRequiredService<HeaderProvider>();

            if (string.IsNullOrWhiteSpace(tokenService.Bearer))
            {
                throw new UnauthorizedAccessException(
                    "Missing bearer token.");
            }

            var httpClientFactory =
                serviceProvider.GetRequiredService<IHttpClientFactory>();

            var oauthSettings =
                serviceProvider.GetRequiredService<OAuthSettings>();

            var serverConfig =
                serviceProvider.GetServerConfig(
                    requestContext.Server);

            var siteUri = new Uri(typed.SiteUrl!);

            using var client =
                await httpClientFactory.GetOboHttpClient(
                    tokenService.Bearer,
                    siteUri.Host,
                    serverConfig!.Server,
                    oauthSettings);

            var loginName = BuildSharePointLoginName(
                typed.PrincipalType!.Value,
                typed.Principal!);

            var ensuredPrincipal = await EnsureSharePointPrincipalAsync(
                client,
                typed.SiteUrl!,
                loginName,
                cancellationToken);

            var group = await GetAssociatedSharePointGroupAsync(
                client,
                typed.SiteUrl!,
                typed.TargetGroup!.Value,
                cancellationToken);

            var alreadyMember =
                await IsSharePointGroupMemberAsync(
                    client,
                    typed.SiteUrl!,
                    group.Id,
                    ensuredPrincipal.Id,
                    cancellationToken);

            if (!alreadyMember)
            {
                await AddPrincipalToSharePointGroupAsync(
                    client,
                    typed.SiteUrl!,
                    group.Id,
                    ensuredPrincipal.LoginName,
                    cancellationToken);
            }

            return new
            {
                typed.SiteUrl,

                TargetGroupType =
                    typed.TargetGroup.ToString(),

                SharePointGroupId = group.Id,
                SharePointGroupTitle = group.Title,

                PrincipalInputType =
                    typed.PrincipalType.ToString(),

                PrincipalInput = typed.Principal,

                SharePointPrincipalId =
                    ensuredPrincipal.Id,

                SharePointLoginName =
                    ensuredPrincipal.LoginName,

                ensuredPrincipal.Title,
                ensuredPrincipal.Email,
                ensuredPrincipal.PrincipalType,

                AlreadyMember = alreadyMember,
                Added = !alreadyMember,

                Status = alreadyMember
                    ? "The principal was already a member of the SharePoint group."
                    : "Added the principal to the SharePoint group successfully."
            };
        }));

    private static string BuildSharePointLoginName(
SharePointPrincipalInputType principalType,
string principal)
    {
        var value = principal.Trim();

        return principalType switch
        {
            SharePointPrincipalInputType.User
                => value.Contains(
                    '|',
                    StringComparison.Ordinal)
                        ? value
                        : $"i:0#.f|membership|{value}",

            SharePointPrincipalInputType.EntraSecurityGroup
                => $"c:0t.c|tenant|{value}",

            SharePointPrincipalInputType.Microsoft365Group
                => $"c:0o.c|federateddirectoryclaimprovider|{value}",

            _ => throw new ArgumentOutOfRangeException(
                nameof(principalType),
                principalType,
                "Unsupported SharePoint principal type.")
        };
    }

    private static async Task<SharePointEnsuredPrincipal>
        EnsureSharePointPrincipalAsync(
            HttpClient client,
            string siteUrl,
            string loginName,
            CancellationToken cancellationToken)
    {
        var url =
            $"{siteUrl.TrimEnd('/')}" +
            "/_api/web/ensureuser";

        using var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                url);

        request.Headers.Accept.ParseAdd(
            "application/json;odata=nometadata");

        request.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                logonName = loginName
            }),
            System.Text.Encoding.UTF8,
            "application/json");

        using var response = await client.SendAsync(
            request,
            cancellationToken);

        var content = await response.Content.ReadAsStringAsync(
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Could not resolve the SharePoint principal " +
                $"({(int)response.StatusCode} {response.ReasonPhrase}). " +
                $"Login name: {loginName}. Response: {content}");
        }

        using var document = JsonDocument.Parse(content);

        var root = UnwrapSharePointResponse(
            document.RootElement);

        if (!TryGetInt32Property(root, "Id", out var id))
        {
            throw new InvalidOperationException(
                "SharePoint ensureUser returned no principal ID.");
        }

        var resolvedLoginName =
            GetOptionalStringProperty(root, "LoginName")
            ?? loginName;

        return new SharePointEnsuredPrincipal(
            id,
            resolvedLoginName,
            GetOptionalStringProperty(root, "Title"),
            GetOptionalStringProperty(root, "Email"),
            GetOptionalInt32Property(root, "PrincipalType"));
    }

    private static async Task<SharePointAssociatedGroup>
        GetAssociatedSharePointGroupAsync(
            HttpClient client,
            string siteUrl,
            SharePointAssociatedGroupType targetGroup,
            CancellationToken cancellationToken)
    {
        var endpoint = targetGroup switch
        {
            SharePointAssociatedGroupType.Visitors
                => "associatedvisitorgroup",

            SharePointAssociatedGroupType.Editors
                => "associatedmembergroup",

            _ => throw new ArgumentOutOfRangeException(
                nameof(targetGroup),
                targetGroup,
                "Unsupported associated SharePoint group.")
        };

        var url =
            $"{siteUrl.TrimEnd('/')}" +
            $"/_api/web/{endpoint}?$select=Id,Title";

        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                url);

        request.Headers.Accept.ParseAdd(
            "application/json;odata=nometadata");

        using var response = await client.SendAsync(
            request,
            cancellationToken);

        var content = await response.Content.ReadAsStringAsync(
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Could not retrieve the associated SharePoint group " +
                $"({(int)response.StatusCode} {response.ReasonPhrase}). " +
                $"Response: {content}");
        }

        using var document = JsonDocument.Parse(content);

        var root = UnwrapSharePointResponse(
            document.RootElement);

        if (!TryGetInt32Property(root, "Id", out var groupId))
        {
            throw new InvalidOperationException(
                $"The site has no associated {targetGroup} group.");
        }

        var groupTitle =
            GetOptionalStringProperty(root, "Title")
            ?? targetGroup.ToString();

        return new SharePointAssociatedGroup(
            groupId,
            groupTitle);
    }

    private static async Task<bool>
        IsSharePointGroupMemberAsync(
            HttpClient client,
            string siteUrl,
            int groupId,
            int principalId,
            CancellationToken cancellationToken)
    {
        var url =
            $"{siteUrl.TrimEnd('/')}" +
            $"/_api/web/sitegroups({groupId})" +
            $"/users?$select=Id&$filter=Id eq {principalId}";

        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                url);

        request.Headers.Accept.ParseAdd(
            "application/json;odata=nometadata");

        using var response = await client.SendAsync(
            request,
            cancellationToken);

        var content = await response.Content.ReadAsStringAsync(
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Could not inspect SharePoint group membership " +
                $"({(int)response.StatusCode} {response.ReasonPhrase}). " +
                $"Response: {content}");
        }

        using var document = JsonDocument.Parse(content);

        var root = UnwrapSharePointResponse(
            document.RootElement);

        if (root.ValueKind == JsonValueKind.Array)
            return root.GetArrayLength() > 0;

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("value", out var value) &&
            value.ValueKind == JsonValueKind.Array)
        {
            return value.GetArrayLength() > 0;
        }

        return false;
    }

    private static async Task
        AddPrincipalToSharePointGroupAsync(
            HttpClient client,
            string siteUrl,
            int groupId,
            string loginName,
            CancellationToken cancellationToken)
    {
        var url =
            $"{siteUrl.TrimEnd('/')}" +
            $"/_api/web/sitegroups({groupId})/users";

        using var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                url);

        request.Headers.Accept.ParseAdd(
            "application/json;odata=nometadata");

        request.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                LoginName = loginName
            }),
            System.Text.Encoding.UTF8,
            "application/json");

        using var response = await client.SendAsync(
            request,
            cancellationToken);

        var content = await response.Content.ReadAsStringAsync(
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Could not add the principal to SharePoint group {groupId} " +
                $"({(int)response.StatusCode} {response.ReasonPhrase}). " +
                $"Login name: {loginName}. Response: {content}");
        }
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SharePointAssociatedGroupType
    {
        Visitors,
        Editors
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SharePointPrincipalInputType
    {
        User,
        EntraSecurityGroup,
        Microsoft365Group
    }

    private sealed record SharePointAssociatedGroup(
        int Id,
        string Title);

    private sealed record SharePointEnsuredPrincipal(
        int Id,
        string LoginName,
        string? Title,
        string? Email,
        int? PrincipalType);

    [Description(
        "Confirm adding a user or Entra group to a SharePoint site's Visitors or Editors group.")]
    public sealed class SharePointAddPrincipalToSiteGroupInput
    {
        [JsonPropertyName("siteUrl")]
        [Required]
        public string? SiteUrl { get; set; }

        [JsonPropertyName("targetGroup")]
        [Required]
        public SharePointAssociatedGroupType? TargetGroup { get; set; }

        [JsonPropertyName("principalType")]
        [Required]
        public SharePointPrincipalInputType? PrincipalType { get; set; }

        [JsonPropertyName("principal")]
        [Required]
        public string? Principal { get; set; }

        public void Validate()
        {
            if (!Uri.TryCreate(
                    SiteUrl,
                    UriKind.Absolute,
                    out var siteUri) ||
                siteUri.Scheme != Uri.UriSchemeHttps ||
                !siteUri.Host.EndsWith(
                    ".sharepoint.com",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException(
                    "siteUrl must be an absolute SharePoint Online HTTPS URL.");
            }

            if (TargetGroup is null)
            {
                throw new ValidationException(
                    "targetGroup is required.");
            }

            if (PrincipalType is null)
            {
                throw new ValidationException(
                    "principalType is required.");
            }

            if (string.IsNullOrWhiteSpace(Principal))
            {
                throw new ValidationException(
                    "principal is required.");
            }

            if (PrincipalType == SharePointPrincipalInputType.User)
            {
                if (!Principal.Contains('@') &&
                    !Principal.Contains('|'))
                {
                    throw new ValidationException(
                        "For a user, principal must be a UPN, email address, " +
                        "or complete SharePoint login name.");
                }
            }
            else if (!Guid.TryParse(Principal, out _))
            {
                throw new ValidationException(
                    "For an Entra or Microsoft 365 group, " +
                    "principal must be its Entra object ID.");
            }
        }
    }

    private static bool TryGetInt32Property(
    JsonElement element,
    string propertyName,
    out int value)
    {
        value = default;

        if (!element.TryGetProperty(
                propertyName,
                out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number)
            return property.TryGetInt32(out value);

        return property.ValueKind == JsonValueKind.String &&
               int.TryParse(property.GetString(), out value);
    }

    private static int? GetOptionalInt32Property(
        JsonElement element,
        string propertyName)
    {
        return TryGetInt32Property(
            element,
            propertyName,
            out var value)
                ? value
                : null;
    }

    private static string? GetOptionalStringProperty(
        JsonElement element,
        string propertyName)
    {
        return element.TryGetProperty(
                   propertyName,
                   out var property) &&
               property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

}