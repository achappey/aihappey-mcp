using System.ComponentModel;
using MCPhappey.Common.Models;
using MCPhappey.Simplicate.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using MCPhappey.Simplicate.Options;
using MCPhappey.Core.Services;

namespace MCPhappey.Simplicate.CRM;

public static partial class SimplicateCRM
{

     [McpServerTool(OpenWorld = false,
        ReadOnly = true,
        Destructive = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(SimplicateData<SimplicatePerson>),
        Name = "simplicate_crm_get_persons",
        Title = "Get persons")]
    [Description("Get persons, filtered by persons filters.")]
    public static async Task<CallToolResult?> SimplicateCRM_GetOrganizations(
     IServiceProvider serviceProvider,
     RequestContext<CallToolRequestParams> requestContext,
      [Description("(partial) Full name.")] string? fullName = null,
      [Description("Text value of relation type.")] string? relationType = null,
      [Description("(partial) text value of team name.")] string? teamName = null,
      [Description("(partial) text value of the relation manager name")] string? relationManager = null,
      [Description("Offset used for pagination")] int? offset = null,
      [Description("The limit of max allowed results")] int? limit = null,
     CancellationToken cancellationToken = default) =>
     await ModelContextToolExtensions.WithExceptionCheck(async () =>
     await requestContext.WithStructuredContent(async () =>
     {
         var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
         var downloadService = serviceProvider.GetRequiredService<DownloadService>();

         var filters = new List<string>();

         if (!string.IsNullOrWhiteSpace(fullName))
             filters.Add($"q[full_name]=*{Uri.EscapeDataString(fullName)}*");

         if (!string.IsNullOrWhiteSpace(relationType))
             filters.Add($"q[relation_type.label]=*{Uri.EscapeDataString(relationType)}*");

         if (!string.IsNullOrWhiteSpace(teamName))
             filters.Add($"q[teams.name]=*{Uri.EscapeDataString(teamName)}*");

         if (!string.IsNullOrWhiteSpace(relationManager))
             filters.Add($"q[relation_manager.name]=*{Uri.EscapeDataString(relationManager)}*");

         if (offset.HasValue)
             filters.Add($"offset={offset.Value}");

         if (limit.HasValue)
             filters.Add($"limit={limit.Value}");

         var filterString = string.Join("&", filters) + $"&metadata=count,limit,offset&sort=-created_at";
         string baseUrl = simplicateOptions.GetApiUrl("/crm/person");

         if (limit.HasValue && limit.Value <= 100)
             return await downloadService.GetSimplicatePageAsync<SimplicatePerson>(
                 serviceProvider,
                 requestContext.Server,
                 $"{baseUrl}?{filterString}",
                 cancellationToken: cancellationToken
             );

         var items = await downloadService.GetAllSimplicatePagesAsync<SimplicatePerson>(
             serviceProvider,
             requestContext.Server,
             baseUrl,
             filterString,
             pageNum => $"Downloading persons (page {pageNum})",
             requestContext,
             cancellationToken: cancellationToken
         );

         return new SimplicateData<SimplicatePerson>()
         {
             Data = items.Skip(offset ?? 0).Take(limit ?? int.MaxValue),
             Metadata = new()
             {
                 Count = items.Count,
                 Offset = offset ?? null,
                 Limit = limit ?? null
             }
         };
     }));

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
        [Description("The relation manager employee id.")] string? relationManagerId = null,
        [Description("The person relation type id.")] string? relationTypeId = null,
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
            RelationManagerId = relationManagerId,
            RelationTypeId = relationTypeId,
            WebsiteUrl = websiteUrl
        },
          dto => new
          {
             first_name = dto.FirstName,
             family_name = dto.FamilyName,
             note = dto.Note,
             is_active = dto.IsActive,
             website_url = dto.WebsiteUrl,
              email = dto.Email,
              phone = dto.Phone,
              linkedin_url = dto.LinkedInUrl,
              initials = dto.Initials,
              teams = dto.Teams.BuildSimplicateTeamAssignments(),
              relation_type = !string.IsNullOrEmpty(dto.RelationTypeId) ? new
              {
                  id = dto.RelationTypeId
              } : null,
              relation_manager = !string.IsNullOrEmpty(dto.RelationManagerId) ? new
              {
                  id = dto.RelationManagerId
              } : null
         },
         GetPersonWriteElicitOverridesAsync,
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
        [Description("The relation manager employee id.")] string? relationManagerId = null,
        [Description("The person relation type id.")] string? relationTypeId = null,
        [Description("The person's website URL, if available.")] Uri? websiteUrl = null,
        [Description("The person's LinkedIn URL, if available.")] Uri? linkedInUrl = null,
      CancellationToken cancellationToken = default)
    {
        var dto = new SimplicateNewPerson
        {
            FirstName = firstName,
            FamilyName = familyName,
            Initials = initials,
            RelationManagerId = relationManagerId,
            RelationTypeId = relationTypeId,
            IsActive = isActive,
            LinkedInUrl = linkedInUrl,
            Note = note,
            Email = email,
            Phone = phone,
            WebsiteUrl = websiteUrl
        };

        return await serviceProvider.PutSimplicateResourceMergedAsync<SimplicatePerson, SimplicateNewPerson>(
            requestContext,
            "/crm/person/" + personId.EnsurePrefix("person"),
            dto,
            (existingPerson, d) => new
            {
                first_name = d.FirstName,
                note = d.Note,
                family_name = d.FamilyName,
                initials = d.Initials,
                is_active = d.IsActive,
                phone = d.Phone,
                linkedin_url = d.LinkedInUrl,
                website_url = d.WebsiteUrl,
                relation_type = !string.IsNullOrEmpty(d.RelationTypeId) ? new
                {
                    id = d.RelationTypeId
                } : null,
                relation_manager = !string.IsNullOrEmpty(d.RelationManagerId) ? new
                {
                    id = d.RelationManagerId
                } : null,
                email = d.Email,
                teams = d.Teams.BuildSimplicateTeamAssignments(existingPerson?.Teams?.Select(team => team.Id)),
            },
            MapPersonToWriteModel,
            GetPersonWriteElicitOverridesAsync,
            cancellationToken);
    }

    private static async Task<IReadOnlyDictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>?>
        GetPersonWriteElicitOverridesAsync(
            IServiceProvider serviceProvider,
            RequestContext<CallToolRequestParams> requestContext,
            SimplicateNewPerson dto,
            CancellationToken cancellationToken)
    {
        var overrides = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>(StringComparer.OrdinalIgnoreCase);

        var employeeOverrides = await serviceProvider.BuildSimplicateEmployeeElicitOverridesAsync<SimplicateNewPerson>(
            requestContext,
            [
                new SimplicateElicitFieldOverride
                {
                    PropertyName = nameof(SimplicateNewPerson.RelationManagerId),
                    Title = "Relation manager",
                    Description = "Select the person relation manager.",
                    DefaultValue = dto.RelationManagerId
                }
            ],
            cancellationToken);

        foreach (var employeeOverride in employeeOverrides)
            overrides[employeeOverride.Key] = employeeOverride.Value;

        var relationTypeOverrides = await serviceProvider.BuildSimplicateRelationTypeElicitOverridesAsync<SimplicateNewPerson>(
            requestContext,
            [
                new SimplicateElicitFieldOverride
                {
                    PropertyName = nameof(SimplicateNewPerson.RelationTypeId),
                    Title = "Relation type",
                    Description = "Select the person relation type.",
                    DefaultValue = dto.RelationTypeId
                }
            ],
            relationTypeScope: "crm",
            cancellationToken: cancellationToken);

        foreach (var relationTypeOverride in relationTypeOverrides)
            overrides[relationTypeOverride.Key] = relationTypeOverride.Value;

        var teamOverrides = await serviceProvider.BuildSimplicateTeamsElicitOverridesAsync<SimplicateNewPerson>(
            requestContext,
            [
                new SimplicateElicitFieldOverride
                {
                    PropertyName = nameof(SimplicateNewPerson.Teams),
                    Title = "Teams",
                    Description = "Select one or more teams for the person.",
                    DefaultValues = dto.Teams
                }
            ],
            cancellationToken);

        foreach (var teamOverride in teamOverrides)
            overrides[teamOverride.Key] = teamOverride.Value;

        return overrides;
    }

    private static SimplicateNewPerson MapPersonToWriteModel(SimplicatePerson person)
        => new()
        {
            Initials = person.Initials,
            FirstName = person.FirstName,
            FamilyName = person.FamilyName,
            Note = person.Note,
            Email = person.Email,
            Phone = person.Phone,
            WebsiteUrl = TryCreateAbsoluteUri(person.WebsiteUrl?.ToString()),
            LinkedInUrl = TryCreateAbsoluteUri(person.LinkedInUrl?.ToString()),
            RelationTypeId = person.RelationType?.Id,
            RelationManagerId = person.RelationManager?.Id,
            Teams = person.Teams?
                .Where(team => !string.IsNullOrWhiteSpace(team.Id))
                .Select(team => team.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            IsActive = person.IsActive,
        };

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

