using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.Contacts;

public static class GraphContactSync
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [Description("Sync team contacts to everyone's Outlook folder.")]
    [McpServerTool(Title = "Sync team contacts", Destructive = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphContactSync_SyncTeamContacts(
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Microsoft Team/group ID.")] string? groupId = null,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            if (string.IsNullOrWhiteSpace(groupId))
                throw new ArgumentException("Please provide groupId.");

            var services = requestContext.Services
                ?? throw new InvalidOperationException("Request services are unavailable.");

            var httpClientFactory = services.GetRequiredService<IHttpClientFactory>();
            var delegatedToken = await services.GetOboGraphToken(requestContext.Server);
            using var appGraph = await services.GetAppGraphClient();

            using var delegatedHttp = CreateGraphClient(httpClientFactory, delegatedToken);

            var me = await GetCurrentUserAsync(delegatedHttp, cancellationToken);
            var team = await GetTeamAsync(delegatedHttp, groupId!, cancellationToken);

            var ownerIds = await GetGroupOwnerIdsAsync(delegatedHttp, groupId!, cancellationToken);
            if (string.IsNullOrWhiteSpace(me.Id) || !ownerIds.Contains(me.Id!, StringComparer.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Only a team owner can run this sync.");

            var allUsers = await GetGroupUsersAsync(delegatedHttp, groupId!, cancellationToken);
            var teamMembers = allUsers
                .Where(a => string.Equals(a.UserType, "Member", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var syncUsers = teamMembers
                .Select(a => new SyncUser(a, ResolveEmail(a)))
                .Where(a => !string.IsNullOrWhiteSpace(a.Email) && !string.IsNullOrWhiteSpace(a.Id))
                .ToList();

            var usersWithoutEmail = teamMembers.Count - syncUsers.Count;
            var totalTargets = syncUsers.Count;
            var totalUpsertOperations = totalTargets * Math.Max(1, syncUsers.Count);

            int? progressCounter = requestContext.Params?.ProgressToken is not null ? 1 : null;

            var results = new List<MemberSyncResult>();
            for (var i = 0; i < totalTargets; i++)
            {
                var target = syncUsers[i];

                var result = new MemberSyncResult
                {
                    TargetUserId = target.Id!,
                    TargetUser = target.DisplayNameOrEmail,
                    TargetEmail = target.Email
                };

                try
                {
                    var folder = await GetOrCreateFolderAsync(
                        appGraph,
                        target.Id!,
                        team.DisplayName!,
                        cancellationToken);

                    result.FolderId = folder.Id;
                    result.FolderName = folder.DisplayName;

                    var existingContacts = await GetFolderContactsAsync(
                        appGraph,
                        target.Id!,
                        folder.Id!,
                        cancellationToken);

                    result.ExistingContacts = existingContacts.Count;

                    var existingByEmail = BuildContactIndex(existingContacts);
                    var desiredEmailSet = syncUsers
                        .Select(a => a.Email)
                        .Where(a => !string.IsNullOrWhiteSpace(a))
                        .Where(a => a?.Equals(me.Mail) != true)
                        .Select(a => a!.Trim())
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    foreach (var source in syncUsers)
                    {
                        if (string.IsNullOrWhiteSpace(source.Email))
                        {
                            result.Skipped++;
                            continue;
                        }

                        progressCounter = await requestContext.Server.SendProgressNotificationAsync(
                            requestContext,
                            progressCounter,
                            $"Upserting {source.DisplayNameOrEmail} into {target.DisplayNameOrEmail}",
                            totalUpsertOperations,
                            cancellationToken);

                        var payload = BuildContactPayload(source);

                        if (existingByEmail.TryGetValue(source.Email!, out var existing)
                            && !string.IsNullOrWhiteSpace(existing.Id))
                        {
                            await appGraph.Users[target.Id!]
                                .ContactFolders[folder.Id!]
                                .Contacts[existing.Id!]
                                .PatchAsync(payload, cancellationToken: cancellationToken);

                            await Task.Delay(10, cancellationToken);

                            result.Updated++;
                        }
                        else
                        {
                            _ = await appGraph.Users[target.Id!]
                                .ContactFolders[folder.Id!]
                                .Contacts
                                .PostAsync(payload, cancellationToken: cancellationToken);

                            await Task.Delay(10, cancellationToken);

                            result.Created++;

                        }
                    }

                    foreach (var stale in existingContacts.Where(a => IsStaleContact(a, desiredEmailSet)))
                    {
                        if (string.IsNullOrWhiteSpace(stale.Id))
                            continue;

                        await appGraph.Users[target.Id!]
                            .ContactFolders[folder.Id!]
                            .Contacts[stale.Id!]
                            .DeleteAsync(cancellationToken: cancellationToken);

                        await Task.Delay(10, cancellationToken);

                        result.Deleted++;
                    }
                }
                catch (Exception ex)
                {
                    result.Error = ex.Message;
                }

                results.Add(result);
            }

            return new
            {
                TeamId = groupId,
                TeamName = team.DisplayName,
                ExecutedBy = me.UserPrincipalName ?? me.Mail ?? me.Id,
                MembersFound = allUsers.Count,
                MembersEligible = teamMembers.Count,
                MembersWithoutEmail = usersWithoutEmail,
                MailboxesProcessed = totalTargets,
                Summary = new
                {
                    Created = results.Sum(a => a.Created),
                    Updated = results.Sum(a => a.Updated),
                    Deleted = results.Sum(a => a.Deleted),
                    Skipped = results.Sum(a => a.Skipped),
                    Errors = results.Count(a => !string.IsNullOrWhiteSpace(a.Error))
                },
                Members = results
            };
        }));

    private static HttpClient CreateGraphClient(IHttpClientFactory factory, string token)
    {
        var http = factory.CreateClient();
        http.BaseAddress = new Uri("https://graph.microsoft.com/beta/");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private static async Task<GraphUserDto> GetCurrentUserAsync(HttpClient http, CancellationToken cancellationToken)
        => await GetAsync<GraphUserDto>(http, "me?$select=id,displayName,mail,userPrincipalName", cancellationToken)
           ?? throw new InvalidOperationException("Unable to resolve current user.");

    private static async Task<GroupDto> GetTeamAsync(HttpClient http, string groupId, CancellationToken cancellationToken)
        => await GetAsync<GroupDto>(
            http,
            $"groups/{Uri.EscapeDataString(groupId)}?$select=id,displayName",
            cancellationToken)
           ?? throw new InvalidOperationException("Team not found.");

    private static async Task<HashSet<string>> GetGroupOwnerIdsAsync(HttpClient http, string groupId, CancellationToken cancellationToken)
    {
        var owners = await GetAllPagesAsync<GraphUserDto>(
            http,
            $"groups/{Uri.EscapeDataString(groupId)}/owners/microsoft.graph.user?$select=id&$top=999",
            cancellationToken);

        return owners
            .Where(a => !string.IsNullOrWhiteSpace(a.Id))
            .Select(a => a.Id!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<List<GraphUserDto>> GetGroupUsersAsync(HttpClient http, string groupId, CancellationToken cancellationToken)
        => await GetAllPagesAsync<GraphUserDto>(
            http,
            $"groups/{Uri.EscapeDataString(groupId)}/members/microsoft.graph.user?$select=id,displayName,givenName,surname,mail,userPrincipalName,mobilePhone,businessPhones,companyName,jobTitle,department,officeLocation,streetAddress,city,state,postalCode,country,userType&$top=999",
            cancellationToken);

    private static async Task<ContactFolder> GetOrCreateFolderAsync(
        GraphServiceClient client,
        string userId,
        string folderName,
        CancellationToken cancellationToken)
    {
        var folders = await GetAllContactFoldersAsync(client, userId, cancellationToken);

        var existing = folders.FirstOrDefault(a =>
            !string.IsNullOrWhiteSpace(a.DisplayName)
            && string.Equals(a.DisplayName, folderName, StringComparison.OrdinalIgnoreCase));

        if (existing?.Id != null)
            return existing;

        return await client.Users[userId]
            .ContactFolders
            .PostAsync(new ContactFolder { DisplayName = folderName }, cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException($"Unable to create contact folder '{folderName}'.");
    }

    private static async Task<List<Contact>> GetFolderContactsAsync(
        GraphServiceClient client,
        string userId,
        string folderId,
        CancellationToken cancellationToken)
    {
        return await GetAllContactsInFolderAsync(client, userId, folderId, cancellationToken);
    }

    private static Dictionary<string, Contact> BuildContactIndex(IEnumerable<Contact> contacts)
    {
        var map = new Dictionary<string, Contact>(StringComparer.OrdinalIgnoreCase);
        foreach (var contact in contacts)
        {
            foreach (var email in GetContactEmails(contact))
            {
                if (!map.ContainsKey(email))
                    map[email] = contact;
            }
        }

        return map;
    }

    private static IReadOnlyList<string> GetContactEmails(Contact contact)
    {
        var emails = new List<string>();

        if (contact.EmailAddresses != null)
        {
            emails.AddRange(contact.EmailAddresses
                .Where(a => !string.IsNullOrWhiteSpace(a.Address))
                .Select(a => a.Address!.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(contact.PrimaryEmailAddress?.Address))
            emails.Add(contact.PrimaryEmailAddress.Address.Trim());

        if (!string.IsNullOrWhiteSpace(contact.SecondaryEmailAddress?.Address))
            emails.Add(contact.SecondaryEmailAddress.Address.Trim());

        if (!string.IsNullOrWhiteSpace(contact.TertiaryEmailAddress?.Address))
            emails.Add(contact.TertiaryEmailAddress.Address.Trim());

        return emails
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsStaleContact(Contact contact, HashSet<string> desiredEmailSet)
    {
        var emails = GetContactEmails(contact);
        if (emails.Count == 0)
            return true;

        return emails.All(a => !desiredEmailSet.Contains(a));
    }

    private static Contact BuildContactPayload(SyncUser user)
    {
        var cleanBusinessPhones = user.BusinessPhones
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var phones = new List<Phone>();
        if (!string.IsNullOrWhiteSpace(user.MobilePhone))
        {
            phones.Add(new Phone
            {
                Number = user.MobilePhone
            });
        }

        phones.AddRange(cleanBusinessPhones.Select(a => new Phone
        {
            Number = a
        }));

        return new Contact
        {
            GivenName = user.GivenName,
            Surname = user.Surname,
            DisplayName = user.DisplayNameOrEmail,
            CompanyName = user.CompanyName,
            JobTitle = user.JobTitle,
            Department = user.Department,
            OfficeLocation = user.OfficeLocation,
            Phones = phones.Count == 0 ? [] : phones,
            PostalAddresses = user.BusinessAddress is null ? [] : [user.BusinessAddress],
            EmailAddresses =
                [
                    new() {
                        Type = EmailType.Work,
                        Address = user.Email
                    }
                ],
            PrimaryEmailAddress = new EmailAddress
            {
                Address = user.Email
            }
        };
    }

    private static async Task<List<ContactFolder>> GetAllContactFoldersAsync(
        GraphServiceClient client,
        string userId,
        CancellationToken cancellationToken)
    {
        var all = new List<ContactFolder>();
        var page = await client.Users[userId].ContactFolders.GetAsync(rq =>
        {
            rq.QueryParameters.Select = ["id", "displayName"];
            rq.QueryParameters.Top = 999;
        }, cancellationToken: cancellationToken);

        while (page != null)
        {
            if (page.Value != null)
                all.AddRange(page.Value);

            if (string.IsNullOrWhiteSpace(page.OdataNextLink))
                break;

            page = await client.Users[userId]
                .ContactFolders
                .WithUrl(page.OdataNextLink)
                .GetAsync(cancellationToken: cancellationToken);
        }

        return all;
    }

    private static async Task<List<Contact>> GetAllContactsInFolderAsync(
        GraphServiceClient client,
        string userId,
        string folderId,
        CancellationToken cancellationToken)
    {
        var all = new List<Contact>();
        var page = await client.Users[userId].ContactFolders[folderId].Contacts.GetAsync(rq =>
        {
            rq.QueryParameters.Select = ["id", "displayName", "emailAddresses", "primaryEmailAddress", "secondaryEmailAddress", "tertiaryEmailAddress"];
            rq.QueryParameters.Top = 999;
        }, cancellationToken: cancellationToken);

        while (page != null)
        {
            if (page.Value != null)
                all.AddRange(page.Value);

            if (string.IsNullOrWhiteSpace(page.OdataNextLink))
                break;

            page = await client.Users[userId]
                .ContactFolders[folderId]
                .Contacts
                .WithUrl(page.OdataNextLink)
                .GetAsync(cancellationToken: cancellationToken);
        }

        return all;
    }

    private static string? ResolveEmail(GraphUserDto user)
    {
        var value = string.IsNullOrWhiteSpace(user.Mail)
            ? user.UserPrincipalName
            : user.Mail;

        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static async Task<T?> GetAsync<T>(HttpClient http, string url, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(url, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Graph GET failed ({response.StatusCode}) at '{url}': {payload}");

        return JsonSerializer.Deserialize<T>(payload, JsonOptions);
    }

    private static async Task<List<T>> GetAllPagesAsync<T>(HttpClient http, string startUrl, CancellationToken cancellationToken)
    {
        var all = new List<T>();
        var next = startUrl;

        while (!string.IsNullOrWhiteSpace(next))
        {
            using var response = await http.GetAsync(next, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Graph list failed ({response.StatusCode}) at '{next}': {payload}");

            var page = JsonSerializer.Deserialize<GraphCollectionPage<T>>(payload, JsonOptions)
                       ?? new GraphCollectionPage<T>();

            if (page.Value != null)
                all.AddRange(page.Value);

            next = page.NextLink;
        }

        return all;
    }

    private sealed class GraphCollectionPage<T>
    {
        [JsonPropertyName("value")]
        public List<T>? Value { get; set; }

        [JsonPropertyName("@odata.nextLink")]
        public string? NextLink { get; set; }
    }

    private sealed class GroupDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }
    }

    private sealed class GraphUserDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("givenName")]
        public string? GivenName { get; set; }

        [JsonPropertyName("surname")]
        public string? Surname { get; set; }

        [JsonPropertyName("mail")]
        public string? Mail { get; set; }

        [JsonPropertyName("userPrincipalName")]
        public string? UserPrincipalName { get; set; }

        [JsonPropertyName("mobilePhone")]
        public string? MobilePhone { get; set; }

        [JsonPropertyName("businessPhones")]
        public List<string>? BusinessPhones { get; set; }

        [JsonPropertyName("companyName")]
        public string? CompanyName { get; set; }

        [JsonPropertyName("jobTitle")]
        public string? JobTitle { get; set; }

        [JsonPropertyName("department")]
        public string? Department { get; set; }

        [JsonPropertyName("officeLocation")]
        public string? OfficeLocation { get; set; }

        [JsonPropertyName("streetAddress")]
        public string? StreetAddress { get; set; }

        [JsonPropertyName("city")]
        public string? City { get; set; }

        [JsonPropertyName("state")]
        public string? State { get; set; }

        [JsonPropertyName("postalCode")]
        public string? PostalCode { get; set; }

        [JsonPropertyName("country")]
        public string? Country { get; set; }

        [JsonPropertyName("userType")]
        public string? UserType { get; set; }
    }

    private sealed class SyncUser
    {
        public SyncUser(GraphUserDto source, string? email)
        {
            Id = source.Id;
            DisplayName = source.DisplayName;
            GivenName = source.GivenName;
            Surname = source.Surname;
            Email = email;
            CompanyName = source.CompanyName;
            JobTitle = source.JobTitle;
            Department = source.Department;
            OfficeLocation = source.OfficeLocation;
            MobilePhone = source.MobilePhone;
            BusinessPhones = source.BusinessPhones ?? [];
            BusinessAddress = BuildBusinessAddress(source);
        }

        public string? Id { get; }
        public string? DisplayName { get; }
        public string? GivenName { get; }
        public string? Surname { get; }
        public string? Email { get; }
        public string? CompanyName { get; }
        public string? JobTitle { get; }
        public string? Department { get; }
        public string? OfficeLocation { get; }
        public string? MobilePhone { get; }
        public List<string> BusinessPhones { get; }
        public PhysicalAddress? BusinessAddress { get; }
        public string DisplayNameOrEmail => string.IsNullOrWhiteSpace(DisplayName) ? (Email ?? "Unknown") : DisplayName;

        private static PhysicalAddress? BuildBusinessAddress(GraphUserDto source)
        {
            if (string.IsNullOrWhiteSpace(source.StreetAddress)
                && string.IsNullOrWhiteSpace(source.City)
                && string.IsNullOrWhiteSpace(source.State)
                && string.IsNullOrWhiteSpace(source.PostalCode)
                && string.IsNullOrWhiteSpace(source.Country))
            {
                return null;
            }

            return new PhysicalAddress
            {
                Street = source.StreetAddress,
                City = source.City,
                State = source.State,
                Type = PhysicalAddressType.Business,
                PostalCode = source.PostalCode,
                CountryOrRegion = source.Country
            };
        }
    }

    private sealed class MemberSyncResult
    {
        public string? TargetUserId { get; set; }
        public string? TargetUser { get; set; }
        public string? TargetEmail { get; set; }
        public string? FolderId { get; set; }
        public string? FolderName { get; set; }
        public int ExistingContacts { get; set; }
        public int Created { get; set; }
        public int Updated { get; set; }
        public int Deleted { get; set; }
        public int Skipped { get; set; }
        public string? Error { get; set; }
    }
}
