using System.ComponentModel;
using MCPhappey.Common;
using MCPhappey.Common.Extensions;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Simplicate.Extensions;
using MCPhappey.Simplicate.Options;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Simplicate.CRM;

public static partial class SimplicateCRM
{
    [Description("Add an organization contact link to an existing person in Simplicate CRM. The client confirms the final work email and job title through elicitation before the person is updated.")]
    [McpServerTool(Title = "Add organization contact to person in Simplicate",
        Name = "simplicate_crm_add_organization_contact_to_person",
        ReadOnly = false,
        Idempotent = false,
        Destructive = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> SimplicateCRM_AddOrganizationContactToPerson(
        [Description("The id of the person to update.")] string personId,
        [Description("The id of the organization to link this person to as a contact.")] string organizationId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Work email for this organization contact link. This value is prefilled and then confirmed via elicitation.")] string? mail = null,
        [Description("Job title / work function for this organization contact link. This value is prefilled and then confirmed via elicitation.")] string? jobtitle = null,
        [Description("Work mobile phone for this organization contact link.")] string? workMobile = null,
        CancellationToken cancellationToken = default)
    {
        var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var scraper = serviceProvider.GetServices<IContentScraper>().OfType<SimplicateScraper>().First();

        var existingPerson = await downloadService.GetSimplicateItemAsync<SimplicatePerson>(
            serviceProvider,
            requestContext.Server,
            simplicateOptions.GetApiUrl("/crm/person/" + personId),
            cancellationToken);

        var person = existingPerson?.Data;
        if (person == null)
            return $"Person '{personId}' was not found in Simplicate CRM.".ToErrorCallToolResponse();

        var existingContacts = person.LinkedAsContactToOrganization?.ToList() ?? [];
        if (existingContacts.Any(x => string.Equals(x.OrganizationId, organizationId, StringComparison.OrdinalIgnoreCase)))
            return $"Person '{personId}' is already linked as a contact to organization '{organizationId}'.".ToErrorCallToolResponse();

        var input = new SimplicatePersonOrganizationContactInput
        {
            OrganizationId = organizationId,
            WorkEmail = mail,
            WorkFunction = jobtitle,
            WorkMobile = workMobile
        };

        var (confirmed, notAccepted, _) = await requestContext.Server.TryElicit(input, cancellationToken);
        if (notAccepted != null) return notAccepted;
        if (confirmed == null)
            return "Elicitation was not accepted.".ToErrorCallToolResponse();

        existingContacts.Add(new SimplicatePersonOrganizationContact
        {
            OrganizationId = confirmed.OrganizationId,
            WorkEmail = confirmed.WorkEmail,
            WorkFunction = confirmed.WorkFunction,
            WorkMobile = confirmed.WorkMobile
        });

        var body = BuildPersonWithOrganizationContactsBody(person, existingContacts);
        var content = await scraper.PutSimplicateItemAsync(
            serviceProvider,
            simplicateOptions.GetApiUrl("/crm/person/" + personId),
            body,
            requestContext: requestContext,
            cancellationToken: cancellationToken);

        return content?.ToCallToolResult();
    }

    [Description("Update an existing organization contact link on a person in Simplicate CRM. The client confirms the final work email, job title, mobile phone, and target organization through elicitation before the person is updated.")]
    [McpServerTool(Title = "Update organization contact on person in Simplicate",
        Name = "simplicate_crm_update_organization_contact_on_person",
        ReadOnly = false,
        Idempotent = false,
        Destructive = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> SimplicateCRM_UpdateOrganizationContactOnPerson(
        [Description("The id of the person to update.")] string personId,
        [Description("The id of the linked contactperson relation to update.")] string contactPersonId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The organization id for this organization contact link. This value is prefilled and then confirmed via elicitation.")] string? organizationId = null,
        [Description("Work email for this organization contact link. This value is prefilled and then confirmed via elicitation.")] string? mail = null,
        [Description("Job title / work function for this organization contact link. This value is prefilled and then confirmed via elicitation.")] string? jobtitle = null,
        [Description("Work mobile phone for this organization contact link. This value is prefilled and then confirmed via elicitation.")] string? workMobile = null,
        CancellationToken cancellationToken = default)
    {
        var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var scraper = serviceProvider.GetServices<IContentScraper>().OfType<SimplicateScraper>().First();

        var existingPerson = await downloadService.GetSimplicateItemAsync<SimplicatePerson>(
            serviceProvider,
            requestContext.Server,
            simplicateOptions.GetApiUrl("/crm/person/" + personId),
            cancellationToken);

        var person = existingPerson?.Data;
        if (person == null)
            return $"Person '{personId}' was not found in Simplicate CRM.".ToErrorCallToolResponse();

        var existingContacts = person.LinkedAsContactToOrganization?.ToList() ?? [];
        var contact = existingContacts.FirstOrDefault(x => string.Equals(x.Id, contactPersonId, StringComparison.OrdinalIgnoreCase));
        if (contact == null)
            return $"Person '{personId}' does not contain linked contactperson '{contactPersonId}'.".ToErrorCallToolResponse();

        var input = new SimplicatePersonOrganizationContactInput
        {
            OrganizationId = organizationId ?? contact.OrganizationId,
            WorkEmail = mail ?? contact.WorkEmail,
            WorkFunction = jobtitle ?? contact.WorkFunction,
            WorkMobile = workMobile ?? contact.WorkMobile
        };

        var (confirmed, notAccepted, _) = await requestContext.Server.TryElicit(input, cancellationToken);
        if (notAccepted != null) return notAccepted;
        if (confirmed == null)
            return "Elicitation was not accepted.".ToErrorCallToolResponse();

        if (existingContacts.Any(x =>
                !string.Equals(x.Id, contactPersonId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.OrganizationId, confirmed.OrganizationId, StringComparison.OrdinalIgnoreCase)))
            return $"Person '{personId}' is already linked as a contact to organization '{confirmed.OrganizationId}'.".ToErrorCallToolResponse();

        var updatedContacts = existingContacts
            .Select(x => string.Equals(x.Id, contactPersonId, StringComparison.OrdinalIgnoreCase)
                ? new SimplicatePersonOrganizationContact
                {
                    Id = x.Id,
                    OrganizationId = confirmed.OrganizationId,
                    WorkEmail = confirmed.WorkEmail,
                    WorkFunction = confirmed.WorkFunction,
                    WorkMobile = confirmed.WorkMobile,
                    Interests = x.Interests?.Select(i => new SimplicateInterestValue
                    {
                        Id = i.Id,
                        Name = i.Name,
                        ApiName = i.ApiName,
                        Value = i.Value
                    }).ToList()
                }
                : new SimplicatePersonOrganizationContact
                {
                    Id = x.Id,
                    OrganizationId = x.OrganizationId,
                    WorkEmail = x.WorkEmail,
                    WorkFunction = x.WorkFunction,
                    WorkMobile = x.WorkMobile,
                    Interests = x.Interests?.Select(i => new SimplicateInterestValue
                    {
                        Id = i.Id,
                        Name = i.Name,
                        ApiName = i.ApiName,
                        Value = i.Value
                    }).ToList()
                })
            .ToList();

        var body = BuildPersonWithOrganizationContactsBody(person, updatedContacts);
        var content = await scraper.PutSimplicateItemAsync(
            serviceProvider,
            simplicateOptions.GetApiUrl("/crm/person/" + personId),
            body,
            requestContext: requestContext,
            cancellationToken: cancellationToken);

        return content?.ToCallToolResult();
    }

    [Description("Delete an organization contact link from an existing person in Simplicate CRM after typed confirmation of the exact linked contactperson id. The person is updated by rewriting the remaining linked organization contact list.")]
    [McpServerTool(Title = "Delete organization contact from person in Simplicate",
        Name = "simplicate_crm_delete_organization_contact_from_person",
        ReadOnly = false,
        Idempotent = false,
        Destructive = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> SimplicateCRM_DeleteOrganizationContactFromPerson(
        [Description("The id of the person to update.")] string personId,
        [Description("The id of the linked contactperson relation to remove from the person.")] string contactPersonId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
    {
        var normalizedPersonId = personId.EnsurePrefix("person");
        var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var scraper = serviceProvider.GetServices<IContentScraper>().OfType<SimplicateScraper>().First();

        var existingPerson = await downloadService.GetSimplicateItemAsync<SimplicatePerson>(
            serviceProvider,
            requestContext.Server,
            simplicateOptions.GetApiUrl("/crm/person/" + normalizedPersonId),
            cancellationToken);

        var person = existingPerson?.Data;
        if (person == null)
            return $"Person '{normalizedPersonId}' was not found in Simplicate CRM.".ToErrorCallToolResponse();

        var existingContacts = person.LinkedAsContactToOrganization?.ToList() ?? [];
        var contact = existingContacts.FirstOrDefault(x => string.Equals(x.Id, contactPersonId, StringComparison.OrdinalIgnoreCase));
        if (contact == null)
            return $"Person '{normalizedPersonId}' does not contain linked contactperson '{contactPersonId}'.".ToErrorCallToolResponse();

        var remainingContacts = existingContacts
            .Where(x => !string.Equals(x.Id, contactPersonId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return await requestContext.ConfirmAndDeleteAsync<ConfirmDeleteSimplicatePersonOrganizationContact>(
            expectedName: contactPersonId,
            async ct =>
            {
                var body = BuildPersonWithOrganizationContactsBody(person, remainingContacts);
                await scraper.PutSimplicateItemAsync(
                    serviceProvider,
                    simplicateOptions.GetApiUrl("/crm/person/" + normalizedPersonId),
                    body,
                    requestContext: requestContext,
                    cancellationToken: ct);
            },
            $"Linked contactperson '{contactPersonId}' deleted from person '{normalizedPersonId}'.",
            cancellationToken);
    }

    [Description("Delete a person contact link from an existing organization in Simplicate CRM after typed confirmation of the exact linked contactperson id. The organization is updated by rewriting the remaining linked person contact list.")]
    [McpServerTool(Title = "Delete person contact from organization in Simplicate",
        Name = "simplicate_crm_delete_person_contact_from_organization",
        ReadOnly = false,
        Idempotent = false,
        Destructive = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> SimplicateCRM_DeletePersonContactFromOrganization(
        [Description("The id of the organization to update.")] string organizationId,
        [Description("The id of the linked contactperson relation to remove from the organization.")] string contactPersonId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
    {
        var normalizedOrganizationId = organizationId.EnsurePrefix("organization");
        var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var scraper = serviceProvider.GetServices<IContentScraper>().OfType<SimplicateScraper>().First();

        var existingOrganization = await downloadService.GetSimplicateItemAsync<SimplicateOrganization>(
            serviceProvider,
            requestContext.Server,
            simplicateOptions.GetApiUrl("/crm/organization/" + normalizedOrganizationId),
            cancellationToken);

        var organization = existingOrganization?.Data;
        if (organization == null)
            return $"Organization '{normalizedOrganizationId}' was not found in Simplicate CRM.".ToErrorCallToolResponse();

        var existingContacts = organization.LinkedPersonsContacts?.ToList() ?? [];
        var contact = existingContacts.FirstOrDefault(x => string.Equals(x.Id, contactPersonId, StringComparison.OrdinalIgnoreCase));
        if (contact == null)
            return $"Organization '{normalizedOrganizationId}' does not contain linked contactperson '{contactPersonId}'.".ToErrorCallToolResponse();

        var remainingContacts = existingContacts
            .Where(x => !string.Equals(x.Id, contactPersonId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return await requestContext.ConfirmAndDeleteAsync<ConfirmDeleteSimplicateOrganizationPersonContact>(
            expectedName: contactPersonId,
            async ct =>
            {
                var body = BuildOrganizationWithPersonContactsBody(organization, remainingContacts);
                await scraper.PutSimplicateItemAsync(
                    serviceProvider,
                    simplicateOptions.GetApiUrl("/crm/organization/" + normalizedOrganizationId),
                    body,
                    requestContext: requestContext,
                    cancellationToken: ct);
            },
            $"Linked contactperson '{contactPersonId}' deleted from organization '{normalizedOrganizationId}'.",
            cancellationToken);
    }

    [Description("Set an interest value on an existing organization contact link of a person in Simplicate CRM. The client confirms the final interest value through elicitation before the person is updated.")]
    [McpServerTool(Title = "Set organization contact interest on person in Simplicate",
        Name = "simplicate_crm_set_organization_contact_interest_on_person",
        ReadOnly = false,
        Idempotent = false,
        Destructive = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> SimplicateCRM_SetOrganizationContactInterestOnPerson(
        [Description("The id of the person to update.")] string personId,
        [Description("The id of the linked contactperson relation to update.")] string contactPersonId,
        [Description("The id of the interest to set on this person-organization contact link.")] string interestId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The interest value to prefill and then confirm via elicitation.")] bool value = true,
        CancellationToken cancellationToken = default)
    {
        var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var scraper = serviceProvider.GetServices<IContentScraper>().OfType<SimplicateScraper>().First();

        var existingPerson = await downloadService.GetSimplicateItemAsync<SimplicatePerson>(
            serviceProvider,
            requestContext.Server,
            simplicateOptions.GetApiUrl("/crm/person/" + personId),
            cancellationToken);

        var person = existingPerson?.Data;
        if (person == null)
            return $"Person '{personId}' was not found in Simplicate CRM.".ToErrorCallToolResponse();

        var existingContacts = person.LinkedAsContactToOrganization?.ToList() ?? [];
        var contact = existingContacts.FirstOrDefault(x => string.Equals(x.Id, contactPersonId, StringComparison.OrdinalIgnoreCase));
        if (contact == null)
            return $"Person '{personId}' does not contain linked contactperson '{contactPersonId}'.".ToErrorCallToolResponse();

        var existingInterest = contact.Interests?.FirstOrDefault(x => string.Equals(x.Id, interestId, StringComparison.OrdinalIgnoreCase));
        var input = new SimplicateLinkedContactInterestInput
        {
            InterestId = interestId,
            Value = value
        };

        var (confirmed, notAccepted, _) = await requestContext.Server.TryElicit(input, cancellationToken);
        if (notAccepted != null) return notAccepted;
        if (confirmed == null)
            return "Elicitation was not accepted.".ToErrorCallToolResponse();

        var updatedContacts = existingContacts
            .Select(x => string.Equals(x.Id, contactPersonId, StringComparison.OrdinalIgnoreCase)
                ? new SimplicatePersonOrganizationContact
                {
                    Id = x.Id,
                    OrganizationId = x.OrganizationId,
                    WorkEmail = x.WorkEmail,
                    WorkFunction = x.WorkFunction,
                    WorkMobile = x.WorkMobile,
                    Interests = UpsertInterest(x.Interests, confirmed.InterestId!, confirmed.Value, existingInterest)
                }
                : new SimplicatePersonOrganizationContact
                {
                    Id = x.Id,
                    OrganizationId = x.OrganizationId,
                    WorkEmail = x.WorkEmail,
                    WorkFunction = x.WorkFunction,
                    WorkMobile = x.WorkMobile,
                    Interests = x.Interests?.Select(i => new SimplicateInterestValue
                    {
                        Id = i.Id,
                        Name = i.Name,
                        ApiName = i.ApiName,
                        Value = i.Value
                    }).ToList()
                })
            .ToList();

        var body = BuildPersonWithOrganizationContactsBody(person, updatedContacts);
        var content = await scraper.PutSimplicateItemAsync(
            serviceProvider,
            simplicateOptions.GetApiUrl("/crm/person/" + personId),
            body,
            requestContext: requestContext,
            cancellationToken: cancellationToken);

        return content?.ToCallToolResult();
    }

    [Description("Set an interest value on an existing person contact link of an organization in Simplicate CRM. The client confirms the final interest value through elicitation before the organization is updated.")]
    [McpServerTool(Title = "Set person contact interest on organization in Simplicate",
        Name = "simplicate_crm_set_person_contact_interest_on_organization",
        ReadOnly = false,
        Idempotent = false,
        Destructive = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> SimplicateCRM_SetPersonContactInterestOnOrganization(
        [Description("The id of the organization to update.")] string organizationId,
        [Description("The id of the linked contactperson relation to update.")] string contactPersonId,
        [Description("The id of the interest to set on this organization-person contact link.")] string interestId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The interest value to prefill and then confirm via elicitation.")] bool value = true,
        CancellationToken cancellationToken = default)
    {
        var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var scraper = serviceProvider.GetServices<IContentScraper>().OfType<SimplicateScraper>().First();

        var existingOrganization = await downloadService.GetSimplicateItemAsync<SimplicateOrganization>(
            serviceProvider,
            requestContext.Server,
            simplicateOptions.GetApiUrl("/crm/organization/" + organizationId),
            cancellationToken);

        var organization = existingOrganization?.Data;
        if (organization == null)
            return $"Organization '{organizationId}' was not found in Simplicate CRM.".ToErrorCallToolResponse();

        var existingContacts = organization.LinkedPersonsContacts?.ToList() ?? [];
        var contact = existingContacts.FirstOrDefault(x => string.Equals(x.Id, contactPersonId, StringComparison.OrdinalIgnoreCase));
        if (contact == null)
            return $"Organization '{organizationId}' does not contain linked contactperson '{contactPersonId}'.".ToErrorCallToolResponse();

        var existingInterest = contact.Interests?.FirstOrDefault(x => string.Equals(x.Id, interestId, StringComparison.OrdinalIgnoreCase));
        var input = new SimplicateLinkedContactInterestInput
        {
            InterestId = interestId,
            Value = existingInterest?.Value ?? value
        };

        var (confirmed, notAccepted, _) = await requestContext.Server.TryElicit(input, cancellationToken);
        if (notAccepted != null) return notAccepted;
        if (confirmed == null)
            return "Elicitation was not accepted.".ToErrorCallToolResponse();

        var updatedContacts = existingContacts
            .Select(x => string.Equals(x.Id, contactPersonId, StringComparison.OrdinalIgnoreCase)
                ? new SimplicateOrganizationPersonContact
                {
                    Id = x.Id,
                    PersonId = x.PersonId,
                    WorkEmail = x.WorkEmail,
                    WorkFunction = x.WorkFunction,
                    WorkMobile = x.WorkMobile,
                    Interests = UpsertInterest(x.Interests, confirmed.InterestId!, confirmed.Value, existingInterest)
                }
                : new SimplicateOrganizationPersonContact
                {
                    Id = x.Id,
                    PersonId = x.PersonId,
                    WorkEmail = x.WorkEmail,
                    WorkFunction = x.WorkFunction,
                    WorkMobile = x.WorkMobile,
                    Interests = x.Interests?.Select(i => new SimplicateInterestValue
                    {
                        Id = i.Id,
                        Name = i.Name,
                        ApiName = i.ApiName,
                        Value = i.Value
                    }).ToList()
                })
            .ToList();

        var body = BuildOrganizationWithPersonContactsBody(organization, updatedContacts);
        var content = await scraper.PutSimplicateItemAsync(
            serviceProvider,
            simplicateOptions.GetApiUrl("/crm/organization/" + organizationId),
            body,
            requestContext: requestContext,
            cancellationToken: cancellationToken);

        return content?.ToCallToolResult();
    }

    [Description("Add a person contact link to an existing organization in Simplicate CRM. The client confirms the final person id, work email, job title, and mobile phone through elicitation before the organization is updated.")]
    [McpServerTool(Title = "Add person contact to organization in Simplicate",
        Name = "simplicate_crm_add_person_contact_to_organization",
        ReadOnly = false,
        Idempotent = false,
        Destructive = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> SimplicateCRM_AddPersonContactToOrganization(
        [Description("The id of the organization to update.")] string organizationId,
        [Description("The id of the person to link to this organization as a contact.")] string personId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Work email for this organization contact link. This value is prefilled and then confirmed via elicitation.")] string? mail = null,
        [Description("Job title / work function for this organization contact link. This value is prefilled and then confirmed via elicitation.")] string? jobtitle = null,
        [Description("Work mobile phone for this organization contact link. This value is prefilled and then confirmed via elicitation.")] string? workMobile = null,
        CancellationToken cancellationToken = default)
    {
        var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var scraper = serviceProvider.GetServices<IContentScraper>().OfType<SimplicateScraper>().First();

        var existingOrganization = await downloadService.GetSimplicateItemAsync<SimplicateOrganization>(
            serviceProvider,
            requestContext.Server,
            simplicateOptions.GetApiUrl("/crm/organization/" + organizationId),
            cancellationToken);

        var organization = existingOrganization?.Data;
        if (organization == null)
            return $"Organization '{organizationId}' was not found in Simplicate CRM.".ToErrorCallToolResponse();

        var existingContacts = organization.LinkedPersonsContacts?.ToList() ?? [];
        if (existingContacts.Any(x => string.Equals(x.PersonId, personId, StringComparison.OrdinalIgnoreCase)))
            return $"Organization '{organizationId}' is already linked to person '{personId}' as a contact.".ToErrorCallToolResponse();

        var input = new SimplicateOrganizationPersonContactInput
        {
            PersonId = personId,
            WorkEmail = mail,
            WorkFunction = jobtitle,
            WorkMobile = workMobile
        };

        var (confirmed, notAccepted, _) = await requestContext.Server.TryElicit(input, cancellationToken);
        if (notAccepted != null) return notAccepted;
        if (confirmed == null)
            return "Elicitation was not accepted.".ToErrorCallToolResponse();

        if (existingContacts.Any(x => string.Equals(x.PersonId, confirmed.PersonId, StringComparison.OrdinalIgnoreCase)))
            return $"Organization '{organizationId}' is already linked to person '{confirmed.PersonId}' as a contact.".ToErrorCallToolResponse();

        existingContacts.Add(new SimplicateOrganizationPersonContact
        {
            PersonId = confirmed.PersonId,
            WorkEmail = confirmed.WorkEmail,
            WorkFunction = confirmed.WorkFunction,
            WorkMobile = confirmed.WorkMobile
        });

        var body = BuildOrganizationWithPersonContactsBody(organization, existingContacts);
        var content = await scraper.PutSimplicateItemAsync(
            serviceProvider,
            simplicateOptions.GetApiUrl("/crm/organization/" + organizationId),
            body,
            requestContext: requestContext,
            cancellationToken: cancellationToken);

        return content?.ToCallToolResult();
    }

    [Description("Update an existing person contact link on an organization in Simplicate CRM. The client confirms the final person id, work email, job title, and mobile phone through elicitation before the organization is updated.")]
    [McpServerTool(Title = "Update person contact on organization in Simplicate",
        Name = "simplicate_crm_update_person_contact_on_organization",
        ReadOnly = false,
        Idempotent = false,
        Destructive = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> SimplicateCRM_UpdatePersonContactOnOrganization(
        [Description("The id of the organization to update.")] string organizationId,
        [Description("The id of the linked contactperson relation to update.")] string contactPersonId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The person id for this organization contact link. This value is prefilled and then confirmed via elicitation.")] string? personId = null,
        [Description("Work email for this organization contact link. This value is prefilled and then confirmed via elicitation.")] string? mail = null,
        [Description("Job title / work function for this organization contact link. This value is prefilled and then confirmed via elicitation.")] string? jobtitle = null,
        [Description("Work mobile phone for this organization contact link. This value is prefilled and then confirmed via elicitation.")] string? workMobile = null,
        CancellationToken cancellationToken = default)
    {
        var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var scraper = serviceProvider.GetServices<IContentScraper>().OfType<SimplicateScraper>().First();

        var existingOrganization = await downloadService.GetSimplicateItemAsync<SimplicateOrganization>(
            serviceProvider,
            requestContext.Server,
            simplicateOptions.GetApiUrl("/crm/organization/" + organizationId),
            cancellationToken);

        var organization = existingOrganization?.Data;
        if (organization == null)
            return $"Organization '{organizationId}' was not found in Simplicate CRM.".ToErrorCallToolResponse();

        var existingContacts = organization.LinkedPersonsContacts?.ToList() ?? [];
        var contact = existingContacts.FirstOrDefault(x => string.Equals(x.Id, contactPersonId, StringComparison.OrdinalIgnoreCase));
        if (contact == null)
            return $"Organization '{organizationId}' does not contain linked contactperson '{contactPersonId}'.".ToErrorCallToolResponse();

        var input = new SimplicateOrganizationPersonContactInput
        {
            PersonId = personId ?? contact.PersonId,
            WorkEmail = mail ?? contact.WorkEmail,
            WorkFunction = jobtitle ?? contact.WorkFunction,
            WorkMobile = workMobile ?? contact.WorkMobile
        };

        var (confirmed, notAccepted, _) = await requestContext.Server.TryElicit(input, cancellationToken);
        if (notAccepted != null) return notAccepted;
        if (confirmed == null)
            return "Elicitation was not accepted.".ToErrorCallToolResponse();

        if (existingContacts.Any(x =>
                !string.Equals(x.Id, contactPersonId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.PersonId, confirmed.PersonId, StringComparison.OrdinalIgnoreCase)))
            return $"Organization '{organizationId}' is already linked to person '{confirmed.PersonId}' as a contact.".ToErrorCallToolResponse();

        var updatedContacts = existingContacts
            .Select(x => string.Equals(x.Id, contactPersonId, StringComparison.OrdinalIgnoreCase)
                ? new SimplicateOrganizationPersonContact
                {
                    Id = x.Id,
                    PersonId = confirmed.PersonId,
                    WorkEmail = confirmed.WorkEmail,
                    WorkFunction = confirmed.WorkFunction,
                    WorkMobile = confirmed.WorkMobile,
                    Interests = x.Interests?.Select(i => new SimplicateInterestValue
                    {
                        Id = i.Id,
                        Name = i.Name,
                        ApiName = i.ApiName,
                        Value = i.Value
                    }).ToList()
                }
                : new SimplicateOrganizationPersonContact
                {
                    Id = x.Id,
                    PersonId = x.PersonId,
                    WorkEmail = x.WorkEmail,
                    WorkFunction = x.WorkFunction,
                    WorkMobile = x.WorkMobile,
                    Interests = x.Interests?.Select(i => new SimplicateInterestValue
                    {
                        Id = i.Id,
                        Name = i.Name,
                        ApiName = i.ApiName,
                        Value = i.Value
                    }).ToList()
                })
            .ToList();

        var body = BuildOrganizationWithPersonContactsBody(organization, updatedContacts);
        var content = await scraper.PutSimplicateItemAsync(
            serviceProvider,
            simplicateOptions.GetApiUrl("/crm/organization/" + organizationId),
            body,
            requestContext: requestContext,
            cancellationToken: cancellationToken);

        return content?.ToCallToolResult();
    }

    private static object BuildPersonWithOrganizationContactsBody(
        SimplicatePerson person,
        IEnumerable<SimplicatePersonOrganizationContact> contacts)
        => new
        {
            first_name = person.FirstName,
            note = person.Note,
            family_name = person.FamilyName,
            initials = person.Initials,
            phone = person.Phone,
            linkedin_url = person.LinkedInUrl,
            website_url = person.WebsiteUrl,
            email = person.Email,
            relation_type = person.RelationType != null
                ? new
                {
                    id = person.RelationType.Id
                }
                : null,
            linked_as_contact_to_organization = contacts.Select(x => new
            {
                id = x.Id,
                work_function = x.WorkFunction,
                work_email = x.WorkEmail,
                work_mobile = x.WorkMobile,
                organization_id = x.OrganizationId,
                interests = x.Interests?.Select(i => new
                {
                    id = i.Id,
                    name = i.Name,
                    api_name = i.ApiName,
                    value = i.Value
                }).ToList()
            }).ToList()
        };

    private static object BuildOrganizationWithPersonContactsBody(
        SimplicateOrganization organization,
        IEnumerable<SimplicateOrganizationPersonContact> contacts)
        => new
        {
            name = organization.Name,
            note = organization.Note,
            email = organization.Email,
            coc_code = organization.CocCode,
            is_active = organization.IsActive,
            phone = organization.Phone,
            linkedin_url = organization.LinkedinUrl,
            vat_number = organization.VatNumber,
            url = organization.Url,
            industry = organization.Industry != null
                ? new
                {
                    id = organization.Industry.Id
                }
                : null,
            relation_type = organization.RelationType != null
                ? new
                {
                    id = organization.RelationType.Id
                }
                : null,
            relation_manager = organization.RelationManager != null
                ? new
                {
                    id = organization.RelationManager.Id
                }
                : null,
            linked_persons_contacts = contacts.Select(x => new
            {
                id = x.Id,
                person_id = x.PersonId,
                work_function = x.WorkFunction,
                work_email = x.WorkEmail,
                work_mobile = x.WorkMobile,
                interests = x.Interests?.Select(i => new
                {
                    id = i.Id,
                    name = i.Name,
                    api_name = i.ApiName,
                    value = i.Value
                }).ToList()
            }).ToList()
        };

    private static List<SimplicateInterestValue> UpsertInterest(
        List<SimplicateInterestValue>? existingInterests,
        string interestId,
        bool value,
        SimplicateInterestValue? existingInterest)
    {
        var interests = existingInterests?.Select(x => new SimplicateInterestValue
        {
            Id = x.Id,
            Name = x.Name,
            ApiName = x.ApiName,
            Value = x.Value
        }).ToList() ?? [];

        var match = interests.FirstOrDefault(x => string.Equals(x.Id, interestId, StringComparison.OrdinalIgnoreCase));
        if (match != null)
        {
            match.Value = value;
            return interests;
        }

        interests.Add(new SimplicateInterestValue
        {
            Id = interestId,
            Name = existingInterest?.Name,
            ApiName = existingInterest?.ApiName,
            Value = value
        });

        return interests;
    }
}

[Description("Please confirm deletion of the Simplicate linked person-side contactperson id: {0}")]
public sealed class ConfirmDeleteSimplicatePersonOrganizationContact : IHasName
{
    [Description("Type the exact linked contactperson id to confirm deletion from the person.")]
    public string? Name { get; set; }
}

[Description("Please confirm deletion of the Simplicate linked organization-side contactperson id: {0}")]
public sealed class ConfirmDeleteSimplicateOrganizationPersonContact : IHasName
{
    [Description("Type the exact linked contactperson id to confirm deletion from the organization.")]
    public string? Name { get; set; }
}
