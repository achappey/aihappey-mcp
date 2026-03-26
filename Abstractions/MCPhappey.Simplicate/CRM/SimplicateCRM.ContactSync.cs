using System.ComponentModel;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Simplicate.Extensions;
using MCPhappey.Simplicate.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Simplicate.CRM;

public static class SimplicateCRMContactSync
{
    private const string SimplicateFolderName = "Simplicate";
    private const string SimplicateMarkerPrefix = "SimplicateContactpersonId:";
    private const string PersonSelect = "id,first_name,family_name,full_name,email,is_active,relation_manager.,teams.,linked_as_contact_to_organization.";

    [Description("Sync Simplicate contactpersons to the signed-in user's Outlook contact folder 'Simplicate'. Source includes persons where current user is relation manager, optionally merged with persons from comma-separated team names.")]
    [McpServerTool(
        Title = "Sync Simplicate contacts to Outlook",
        Name = "simplicate_crm_sync_contacts_to_outlook",
        ReadOnly = false,
        Idempotent = false,
        Destructive = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> SimplicateCRM_SyncContactsToOutlook(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional comma-separated Simplicate team names. When provided, contacts from those teams are merged with relation-manager contacts.")] string? teamNamesCsv = null,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
            var downloadService = serviceProvider.GetRequiredService<DownloadService>();
            using var graphClient = await serviceProvider.GetOboGraphClient(requestContext.Server);

            var me = await graphClient.Me.GetAsync(cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException("Unable to resolve the signed-in user from Microsoft Graph.");

            var meEmail = ResolveEmail(me)
                ?? throw new InvalidOperationException("Signed-in Graph user does not have a usable email address.");

            var currentEmployee = await GetCurrentEmployeeAsync(
                serviceProvider,
                requestContext,
                downloadService,
                simplicateOptions,
                meEmail,
                cancellationToken)
                ?? throw new InvalidOperationException($"No Simplicate employee found for '{meEmail}'.");

            var relationManagerName = currentEmployee.Name;
            if (string.IsNullOrWhiteSpace(relationManagerName))
                throw new InvalidOperationException("Current Simplicate employee has no name; cannot query relation manager persons.");

            var relationManagerPersons = await GetPersonsByRelationManagerAsync(
                serviceProvider,
                requestContext,
                downloadService,
                simplicateOptions,
                relationManagerName,
                cancellationToken);

            var requestedTeamNames = SplitCsv(teamNamesCsv);
            var personsFromTeams = new List<SimplicateCRM.SimplicatePerson>();
            var teamFetch = new List<object>();

            foreach (var teamName in requestedTeamNames)
            {
                var persons = await GetPersonsByTeamAsync(
                    serviceProvider,
                    requestContext,
                    downloadService,
                    simplicateOptions,
                    teamName,
                    cancellationToken);

                personsFromTeams.AddRange(persons);
                teamFetch.Add(new
                {
                    Team = teamName,
                    Persons = persons.Count
                });
            }

            var mergedPersons = MergePersonsById(relationManagerPersons.Concat(personsFromTeams));

            var flattenResult = FlattenContacts(mergedPersons);
            var desiredByContactpersonId = flattenResult.Candidates;

            var organizationNameMap = await BuildOrganizationNameMapAsync(
                serviceProvider,
                requestContext,
                downloadService,
                simplicateOptions,
                desiredByContactpersonId.Values.Select(a => a.OrganizationId),
                cancellationToken);

            var folder = await GetOrCreateFolderAsync(graphClient, SimplicateFolderName, cancellationToken);
            var existingContacts = await GetAllContactsInFolderAsync(graphClient, folder.Id!, cancellationToken);

            var existingByMarker = BuildExistingByMarker(existingContacts);
            var existingByEmail = BuildExistingByEmail(existingContacts);
            var matchedExistingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var created = 0;
            var updated = 0;
            var deleted = 0;
            var errors = 0;
            var diagnostics = new List<object>();

            foreach (var candidate in desiredByContactpersonId.Values)
            {
                Contact? existing = null;

                if (existingByMarker.TryGetValue(candidate.ContactpersonId, out var byMarker))
                    existing = byMarker;
                else if (!string.IsNullOrWhiteSpace(candidate.Email)
                         && existingByEmail.TryGetValue(candidate.Email, out var byEmail)
                         && !string.IsNullOrWhiteSpace(byEmail.Id)
                         && !matchedExistingIds.Contains(byEmail.Id!))
                    existing = byEmail;

                var payload = BuildGraphPayload(candidate, organizationNameMap);

                try
                {
                    if (!string.IsNullOrWhiteSpace(existing?.Id)
                        && !matchedExistingIds.Contains(existing.Id))
                    {
                        await graphClient.Me.ContactFolders[folder.Id!]
                            .Contacts[existing.Id!]
                            .PatchAsync(payload, cancellationToken: cancellationToken);

                        matchedExistingIds.Add(existing.Id!);
                        updated++;

                        diagnostics.Add(new
                        {
                            candidate.ContactpersonId,
                            candidate.DisplayName,
                            candidate.Email,
                            Action = "updated",
                            ExistingContactId = existing.Id
                        });
                    }
                    else
                    {
                        _ = await graphClient.Me.ContactFolders[folder.Id!]
                            .Contacts
                            .PostAsync(payload, cancellationToken: cancellationToken);

                        created++;

                        diagnostics.Add(new
                        {
                            candidate.ContactpersonId,
                            candidate.DisplayName,
                            candidate.Email,
                            Action = "created"
                        });
                    }
                }
                catch (Exception ex)
                {
                    errors++;
                    diagnostics.Add(new
                    {
                        candidate.ContactpersonId,
                        candidate.DisplayName,
                        candidate.Email,
                        Action = "error",
                        Error = ex.Message
                    });
                }
            }

            foreach (var stale in existingContacts
                         .Where(a => !string.IsNullOrWhiteSpace(a.Id) && !matchedExistingIds.Contains(a.Id!)))
            {
                try
                {
                    await graphClient.Me.ContactFolders[folder.Id!]
                        .Contacts[stale.Id!]
                        .DeleteAsync(cancellationToken: cancellationToken);

                    deleted++;
                }
                catch
                {
                    errors++;
                }
            }

            return new
            {
                Folder = SimplicateFolderName,
                ExecutedBy = me.UserPrincipalName ?? me.Mail ?? me.Id,
                RelationManager = relationManagerName,
                OptionalTeamFilters = requestedTeamNames,
                TeamFilterFetch = teamFetch,
                Source = new
                {
                    RelationManagerPersons = relationManagerPersons.Count,
                    TeamPersonsRaw = personsFromTeams.Count,
                    PersonsMerged = mergedPersons.Count,
                    ContactLinksFlattened = flattenResult.FlattenedLinks,
                    ContactLinksWithoutId = flattenResult.SkippedWithoutContactpersonId,
                    UniqueContactpersons = desiredByContactpersonId.Count
                },
                ExistingFolderContacts = existingContacts.Count,
                Summary = new
                {
                    Created = created,
                    Updated = updated,
                    Deleted = deleted,
                    Errors = errors
                },
                Diagnostics = diagnostics
            };
        }));

    private static async Task<List<SimplicateCRM.SimplicatePerson>> GetPersonsByRelationManagerAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        DownloadService downloadService,
        SimplicateOptions simplicateOptions,
        string relationManagerName,
        CancellationToken cancellationToken)
    {
        var filters = new List<string>
        {
            "q[is_active]=true",
            $"q[relation_manager.name]=*{Uri.EscapeDataString(relationManagerName)}*",
            $"select={PersonSelect}",
            "sort=full_name"
        };

        return await downloadService.GetAllSimplicatePagesAsync<SimplicateCRM.SimplicatePerson>(
            serviceProvider,
            requestContext.Server,
            simplicateOptions.GetApiUrl("/crm/person"),
            string.Join("&", filters),
            page => $"Downloading relation-manager persons page {page}",
            requestContext,
            cancellationToken: cancellationToken);
    }

    private static async Task<List<SimplicateCRM.SimplicatePerson>> GetPersonsByTeamAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        DownloadService downloadService,
        SimplicateOptions simplicateOptions,
        string teamName,
        CancellationToken cancellationToken)
    {
        var filters = new List<string>
        {
            "q[is_active]=true",
            $"q[teams.name]=*{Uri.EscapeDataString(teamName)}*",
            $"select={PersonSelect}",
            "sort=full_name"
        };

        return await downloadService.GetAllSimplicatePagesAsync<SimplicateCRM.SimplicatePerson>(
            serviceProvider,
            requestContext.Server,
            simplicateOptions.GetApiUrl("/crm/person"),
            string.Join("&", filters),
            page => $"Downloading persons for team '{teamName}' page {page}",
            requestContext,
            cancellationToken: cancellationToken);
    }

    private static async Task<SimplicateEmployee?> GetCurrentEmployeeAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        DownloadService downloadService,
        SimplicateOptions simplicateOptions,
        string mail,
        CancellationToken cancellationToken)
    {
        var employees = await downloadService.GetAllSimplicatePagesAsync<SimplicateEmployee>(
            serviceProvider,
            requestContext.Server,
            simplicateOptions.GetApiUrl("/hrm/employee"),
            $"q[work_email]=*{Uri.EscapeDataString(mail)}*&select=id,name,work_email",
            page => $"Downloading employee page {page}",
            requestContext,
            cancellationToken: cancellationToken);

        return employees
            .OrderByDescending(a => !string.IsNullOrWhiteSpace(a.WorkEmail))
            .FirstOrDefault(a => string.Equals(a.WorkEmail?.Trim(), mail, StringComparison.OrdinalIgnoreCase))
            ?? employees.FirstOrDefault();
    }

    private static List<SimplicateCRM.SimplicatePerson> MergePersonsById(IEnumerable<SimplicateCRM.SimplicatePerson> persons)
    {
        return persons
            .Where(a => !string.IsNullOrWhiteSpace(a.Id))
            .GroupBy(a => a.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var ordered = group.ToList();
                var seed = ordered[0];

                var mergedTeams = ordered
                    .SelectMany(a => a.Teams ?? [])
                    .Where(a => !string.IsNullOrWhiteSpace(a.Id) || !string.IsNullOrWhiteSpace(a.Name))
                    .GroupBy(a => a.Id ?? a.Name!, StringComparer.OrdinalIgnoreCase)
                    .Select(a => a.First())
                    .ToList();

                var mergedLinks = ordered
                    .SelectMany(a => a.LinkedAsContactToOrganization ?? [])
                    .Where(a => !string.IsNullOrWhiteSpace(a.Id)
                                || !string.IsNullOrWhiteSpace(a.OrganizationId)
                                || !string.IsNullOrWhiteSpace(a.WorkEmail))
                    .GroupBy(a => a.Id ?? $"{a.OrganizationId}|{a.WorkEmail}|{a.WorkFunction}", StringComparer.OrdinalIgnoreCase)
                    .Select(a => a.First())
                    .ToList();

                return new SimplicateCRM.SimplicatePerson
                {
                    Id = seed.Id,
                    FirstName = ordered.Select(a => a.FirstName).FirstOrDefault(a => !string.IsNullOrWhiteSpace(a)),
                    FamilyName = ordered.Select(a => a.FamilyName).FirstOrDefault(a => !string.IsNullOrWhiteSpace(a)),
                    Email = ordered.Select(a => a.Email).FirstOrDefault(a => !string.IsNullOrWhiteSpace(a)),
                    IsActive = ordered.Select(a => a.IsActive).FirstOrDefault(a => a.HasValue) ?? true,
                    RelationManager = ordered.Select(a => a.RelationManager).FirstOrDefault(a => a != null),
                    Teams = mergedTeams.Count > 0 ? mergedTeams : null,
                    LinkedAsContactToOrganization = mergedLinks.Count > 0 ? mergedLinks : null
                };
            })
            .ToList();
    }

    private static FlattenResult FlattenContacts(IEnumerable<SimplicateCRM.SimplicatePerson> persons)
    {
        var map = new Dictionary<string, SyncCandidate>(StringComparer.OrdinalIgnoreCase);
        var flattenedLinks = 0;
        var skippedWithoutId = 0;

        foreach (var person in persons)
        {
            var teamCategories = (person.Teams ?? [])
                .Where(a => a.Value != false && !string.IsNullOrWhiteSpace(a.Name))
                .Select(a => a.Name!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var displayName = BuildDisplayName(person);
            var links = person.LinkedAsContactToOrganization ?? [];

            foreach (var link in links)
            {
                flattenedLinks++;

                if (string.IsNullOrWhiteSpace(link.Id))
                {
                    skippedWithoutId++;
                    continue;
                }

                var id = link.Id.Trim();
                var candidate = new SyncCandidate(
                    contactpersonId: id,
                    givenName: person.FirstName,
                    surname: person.FamilyName,
                    displayName: displayName,
                    email: NormalizeEmail(link.WorkEmail) ?? NormalizeEmail(person.Email),
                    workFunction: link.WorkFunction,
                    workMobile: link.WorkMobile,
                    organizationId: link.OrganizationId,
                    categories: teamCategories);

                if (map.TryGetValue(id, out var existing))
                {
                    map[id] = MergeCandidate(existing, candidate);
                }
                else
                {
                    map[id] = candidate;
                }
            }
        }

        return new FlattenResult
        {
            Candidates = map,
            FlattenedLinks = flattenedLinks,
            SkippedWithoutContactpersonId = skippedWithoutId
        };
    }

    private static SyncCandidate MergeCandidate(SyncCandidate existing, SyncCandidate incoming)
    {
        var categories = existing.Categories
            .Union(incoming.Categories, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new SyncCandidate(
            contactpersonId: existing.ContactpersonId,
            givenName: Prefer(existing.GivenName, incoming.GivenName),
            surname: Prefer(existing.Surname, incoming.Surname),
            displayName: Prefer(existing.DisplayName, incoming.DisplayName)!,
            email: Prefer(existing.Email, incoming.Email),
            workFunction: Prefer(existing.WorkFunction, incoming.WorkFunction),
            workMobile: Prefer(existing.WorkMobile, incoming.WorkMobile),
            organizationId: Prefer(existing.OrganizationId, incoming.OrganizationId),
            categories: categories);
    }

    private static Contact BuildGraphPayload(
        SyncCandidate candidate,
        IReadOnlyDictionary<string, string> organizationNameMap)
    {
        var categories = new List<string> { };
        categories.AddRange(candidate.Categories);
        categories = [.. categories
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)];

        List<Phone> phones = string.IsNullOrWhiteSpace(candidate.WorkMobile)
    ? []
    : [
        new Phone
        {
            Number = candidate.WorkMobile.Trim()
        }
    ];

        List<TypedEmailAddress> emails = string.IsNullOrWhiteSpace(candidate.Email)
            ? []
            : [
                new TypedEmailAddress
        {
            Type = EmailType.Work,
            Address = candidate.Email
        }
            ];

        var companyName = ResolveOrganizationName(candidate.OrganizationId, organizationNameMap)
                          ?? candidate.OrganizationId;

        return new Contact
        {
            GivenName = candidate.GivenName,
            Surname = candidate.Surname,
            DisplayName = candidate.DisplayName,
            JobTitle = candidate.WorkFunction,
            CompanyName = companyName,
            Categories = categories,
            Phones = phones,
            EmailAddresses = emails,
            PrimaryEmailAddress = string.IsNullOrWhiteSpace(candidate.Email)
                ? null
                : new EmailAddress { Address = candidate.Email },
            PersonalNotes = BuildMarkerNote(candidate.ContactpersonId)
        };
    }

    private static async Task<Dictionary<string, string>> BuildOrganizationNameMapAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        DownloadService downloadService,
        SimplicateOptions simplicateOptions,
        IEnumerable<string?> organizationIds,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ids = organizationIds
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var id in ids)
        {
            try
            {
                var normalizedId = id.EnsurePrefix("organization");
                var item = await downloadService.GetSimplicateItemAsync<SimplicateCRM.SimplicateOrganization>(
                    serviceProvider,
                    requestContext.Server,
                    simplicateOptions.GetApiUrl("/crm/organization/" + normalizedId),
                    cancellationToken);

                var name = item?.Data?.Name;
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                map[id] = name.Trim();
                map[normalizedId] = name.Trim();
            }
            catch
            {
                // best effort only; fallback is organization id
            }
        }

        return map;
    }

    private static string? ResolveOrganizationName(
        string? organizationId,
        IReadOnlyDictionary<string, string> organizationNameMap)
    {
        if (string.IsNullOrWhiteSpace(organizationId))
            return null;

        return organizationNameMap.TryGetValue(organizationId.Trim(), out var name)
            ? name
            : null;
    }

    private static async Task<ContactFolder> GetOrCreateFolderAsync(
        GraphServiceClient graphClient,
        string folderName,
        CancellationToken cancellationToken)
    {
        var allFolders = new List<ContactFolder>();
        var page = await graphClient.Me.ContactFolders.GetAsync(requestConfiguration =>
        {
            requestConfiguration.QueryParameters.Select = ["id", "displayName"];
            requestConfiguration.QueryParameters.Top = 999;
        }, cancellationToken: cancellationToken);

        while (page != null)
        {
            if (page.Value != null)
                allFolders.AddRange(page.Value);

            if (string.IsNullOrWhiteSpace(page.OdataNextLink))
                break;

            page = await graphClient.Me.ContactFolders
                .WithUrl(page.OdataNextLink)
                .GetAsync(cancellationToken: cancellationToken);
        }

        var existing = allFolders.FirstOrDefault(a =>
            !string.IsNullOrWhiteSpace(a.DisplayName)
            && string.Equals(a.DisplayName, folderName, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(existing?.Id))
            return existing;

        return await graphClient.Me.ContactFolders
            .PostAsync(new ContactFolder { DisplayName = folderName }, cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException($"Unable to create Outlook contact folder '{folderName}'.");
    }

    private static async Task<List<Contact>> GetAllContactsInFolderAsync(
        GraphServiceClient graphClient,
        string folderId,
        CancellationToken cancellationToken)
    {
        var all = new List<Contact>();
        var page = await graphClient.Me.ContactFolders[folderId].Contacts.GetAsync(requestConfiguration =>
        {
            requestConfiguration.QueryParameters.Select =
            [
                "id",
                "displayName",
                "emailAddresses",
                "primaryEmailAddress",
                "secondaryEmailAddress",
                "tertiaryEmailAddress",
                "categories",
                "personalNotes"
            ];
            requestConfiguration.QueryParameters.Top = 999;
        }, cancellationToken: cancellationToken);

        while (page != null)
        {
            if (page.Value != null)
                all.AddRange(page.Value);

            if (string.IsNullOrWhiteSpace(page.OdataNextLink))
                break;

            page = await graphClient.Me.ContactFolders[folderId]
                .Contacts
                .WithUrl(page.OdataNextLink)
                .GetAsync(cancellationToken: cancellationToken);
        }

        return all;
    }

    private static Dictionary<string, Contact> BuildExistingByMarker(IEnumerable<Contact> contacts)
    {
        var map = new Dictionary<string, Contact>(StringComparer.OrdinalIgnoreCase);
        foreach (var contact in contacts)
        {
            var markerId = ExtractMarkerId(contact);
            if (string.IsNullOrWhiteSpace(markerId))
                continue;

            if (!map.ContainsKey(markerId))
                map[markerId] = contact;
        }

        return map;
    }

    private static Dictionary<string, Contact> BuildExistingByEmail(IEnumerable<Contact> contacts)
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

    private static string BuildDisplayName(SimplicateCRM.SimplicatePerson person)
    {
        var full = string.Join(' ', new[] { person.FirstName, person.FamilyName }
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a!.Trim()));

        if (!string.IsNullOrWhiteSpace(full))
            return full;

        return NormalizeEmail(person.Email)
               ?? person.Id
               ?? "Unknown";
    }

    private static List<string> SplitCsv(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value
                .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

    private static string? ResolveEmail(User me)
    {
        var value = string.IsNullOrWhiteSpace(me.Mail)
            ? me.UserPrincipalName
            : me.Mail;

        return NormalizeEmail(value);
    }

    private static string? NormalizeEmail(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();

    private static string? Prefer(string? left, string? right)
        => string.IsNullOrWhiteSpace(left) ? right : left;

    private static string BuildMarkerNote(string contactpersonId)
        => $"{SimplicateMarkerPrefix}{contactpersonId}";

    private static string? ExtractMarkerId(Contact contact)
    {
        if (string.IsNullOrWhiteSpace(contact.PersonalNotes))
            return null;

        var notes = contact.PersonalNotes;
        var start = notes.IndexOf(SimplicateMarkerPrefix, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return null;

        var marker = notes[(start + SimplicateMarkerPrefix.Length)..];
        var lineBreak = marker.IndexOfAny(['\r', '\n']);
        var value = (lineBreak >= 0 ? marker[..lineBreak] : marker).Trim();

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private sealed class SimplicateEmployee
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? WorkEmail { get; set; }
    }

    private sealed class FlattenResult
    {
        public required Dictionary<string, SyncCandidate> Candidates { get; init; }
        public int FlattenedLinks { get; init; }
        public int SkippedWithoutContactpersonId { get; init; }
    }

    private sealed class SyncCandidate
    {
        public SyncCandidate(
            string contactpersonId,
            string? givenName,
            string? surname,
            string displayName,
            string? email,
            string? workFunction,
            string? workMobile,
            string? organizationId,
            IEnumerable<string>? categories)
        {
            ContactpersonId = contactpersonId;
            GivenName = givenName;
            Surname = surname;
            DisplayName = displayName;
            Email = email;
            WorkFunction = workFunction;
            WorkMobile = workMobile;
            OrganizationId = organizationId;
            Categories = (categories ?? [])
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Select(a => a.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public string ContactpersonId { get; }
        public string? GivenName { get; }
        public string? Surname { get; }
        public string DisplayName { get; }
        public string? Email { get; }
        public string? WorkFunction { get; }
        public string? WorkMobile { get; }
        public string? OrganizationId { get; }
        public IReadOnlyList<string> Categories { get; }
    }
}

