using System.ComponentModel;
using MCPhappey.Common.Models;
using MCPhappey.Simplicate.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MCPhappey.Common.Extensions;

namespace MCPhappey.Simplicate.CRM;

public static partial class SimplicateCRM
{

    [Description("Create a new person in Simplicate CRM")]
    [McpServerTool(Title = "Create new person in Simplicate",
        ReadOnly = false,
        Idempotent = false,
        Destructive = true, OpenWorld = false)]
    public static async Task<CallToolResult?> SimplicateCRM_CreatePerson(
        [Description("The person's initials.")] string initials,
        [Description("The person's first name.")] string firstName,
        [Description("The person's family name or surname.")] string familyName,
      IServiceProvider serviceProvider,
      RequestContext<CallToolRequestParams> requestContext,
        [Description("A note or comment about the person.")] string? note = null,
        [Description("The person's primary email address.")] string? email = null,
        [Description("The person's phone number.")] string? phone = null,
        [Description("The person's website URL, if available.")] Uri? websiteUrl = null,
        [Description("The person's LinkedIn URL, if available.")] Uri? linkedInUrl = null,
      CancellationToken cancellationToken = default) => await serviceProvider.PostSimplicateResourceAsync(
        requestContext,
        "/crm/person",
        new SimplicateNewPerson
        {
            FirstName = firstName,
            FamilyName = familyName,
            Initials = initials,
            Note = note,
            IsActive = true,
            LinkedInUrl = linkedInUrl,
            Email = email,
            Phone = phone,
            WebsiteUrl = websiteUrl
        },
        cancellationToken
    );

    [Description("Update a person in Simplicate CRM")]
    [McpServerTool(Title = "Update a person in Simplicate",
        ReadOnly = false,
        Idempotent = false,
        Destructive = true, OpenWorld = false)]
    public static async Task<CallToolResult?> SimplicateCRM_UpdatePerson(
            string personId,
        [Description("The person's initials.")] string initials,
        [Description("The person's first name.")] string firstName,
        [Description("The person's family name or surname.")] string familyName,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("A note or comment about the person.")] string? note = null,
        [Description("The person's primary email address.")] string? email = null,
        [Description("The person's phone number.")] string? phone = null,
        [Description("Person is active or not.")] bool? isActive = true,
        [Description("The person's website URL, if available.")] Uri? websiteUrl = null,
        [Description("The person's LinkedIn URL, if available.")] Uri? linkedInUrl = null,
      CancellationToken cancellationToken = default)
    {
        var dto = new SimplicateNewPerson
        {
            FirstName = firstName,
            FamilyName = familyName,
            Initials = initials,
            IsActive = isActive,
            LinkedInUrl = linkedInUrl,
            Note = note,
            Email = email,
            Phone = phone,
            WebsiteUrl = websiteUrl
        };

        return await serviceProvider.PutSimplicateResourceMergedAsync(
            requestContext,
            "/crm/person/" + personId.EnsurePrefix("person"),
            dto,
            d => new
            {
                first_name = d.FirstName,
                note = d.Note,
                family_name = d.FamilyName,
                initials = d.Initials,
                is_active = d.IsActive,
                phone = d.Phone,
                linkedin_url = d.LinkedInUrl,
                website_url = d.WebsiteUrl,
                email = d.Email,
            },
             cancellationToken);
    }

    [Description("Delete a person in Simplicate CRM after typed confirmation of the exact person id.")]
    [McpServerTool(Title = "Delete person in Simplicate",
        Name = "simplicate_crm_delete_person",
        ReadOnly = false,
        Idempotent = false,
        Destructive = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> SimplicateCRM_DeletePerson(
        [Description("The Simplicate person id.")] string personId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
    {
        var normalizedPersonId = personId.EnsurePrefix("person");

        return await requestContext.ConfirmAndDeleteAsync<ConfirmDeleteSimplicatePerson>(
            expectedName: normalizedPersonId,
            async ct => await serviceProvider.DeleteSimplicateResourceAsync(
                requestContext,
                "/crm/person/" + normalizedPersonId,
                $"Person '{normalizedPersonId}' deleted.",
                ct),
            $"Person '{normalizedPersonId}' deleted.",
            cancellationToken);
    }

}

[Description("Please confirm deletion of the Simplicate person id: {0}")]
public sealed class ConfirmDeleteSimplicatePerson : IHasName
{
    [Description("Type the exact person id to confirm deletion.")]
    public string? Name { get; set; }
}
 
