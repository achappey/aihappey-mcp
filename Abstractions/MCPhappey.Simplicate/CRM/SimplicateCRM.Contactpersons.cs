using System.ComponentModel;
using MCPhappey.Common;
using MCPhappey.Common.Extensions;
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

    [Description("Delete an organization contact link from an existing person in Simplicate CRM by rewriting the person's linked organization contact list.")]
    [McpServerTool(Title = "Delete organization contact from person in Simplicate",
        Name = "simplicate_crm_delete_organization_contact_from_person",
        ReadOnly = false,
        Idempotent = false,
        Destructive = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> SimplicateCRM_DeleteOrganizationContactFromPerson(
        [Description("The id of the person to update.")] string personId,
        [Description("The id of the organization link to remove from the person.")] string organizationId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
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
        var remainingContacts = existingContacts
            .Where(x => !string.Equals(x.OrganizationId, organizationId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (remainingContacts.Count == existingContacts.Count)
            return $"Person '{personId}' is not linked as a contact to organization '{organizationId}'.".ToErrorCallToolResponse();

        var body = BuildPersonWithOrganizationContactsBody(person, remainingContacts);
        var content = await scraper.PutSimplicateItemAsync(
            serviceProvider,
            simplicateOptions.GetApiUrl("/crm/person/" + personId),
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
            linked_as_contact_to_organization = contacts.Select(x => new
            {
                work_function = x.WorkFunction,
                work_email = x.WorkEmail,
                work_mobile = x.WorkMobile,
                organization_id = x.OrganizationId
            }).ToList()
        };
}
