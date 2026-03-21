using System.ComponentModel;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Simplicate;
using MCPhappey.Simplicate.Extensions;
using MCPhappey.Simplicate.Options;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MCPhappey.Common.Extensions;

namespace MCPhappey.Simplicate.CRM;

public static partial class SimplicateCRM
{
    [McpServerTool(OpenWorld = false,
        ReadOnly = true,
        Destructive = false,
        Name = "simplicate_crm_get_organizations",
        Title = "Get organizations")]
    [Description("Get organizations, filtered by organization filters.")]
    public static async Task<CallToolResult?> SimplicateCRM_GetOrganizations(
     IServiceProvider serviceProvider,
     RequestContext<CallToolRequestParams> requestContext,
      [Description("(partial) Organization name.")] string? organizationName = null,
      [Description("Text value of industry name.")] string? industryName = null,
      [Description("Text value of relation type.")] string? relationType = null,
      [Description("(partial) text value of team name.")] string? teamName = null,
      [Description("Visiting address locality")] string? visitingAddressLocality = null,
      [Description("(partial) text value of the relation manager name")] string? relationManager = null,
      [Description("Offset used for pagination")] int? offset = null,
     CancellationToken cancellationToken = default) =>
     await requestContext.WithExceptionCheck(async () =>
     await requestContext.WithStructuredContent(async () =>
     {
         var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
         var downloadService = serviceProvider.GetRequiredService<DownloadService>();

         string select =
                "id," +
                "name," +
                "phone," +
                "url," +
                "email," +
                "linkedin_url," +
                "coc_code," +
                "vat_number," +
                "note," +
                "industry.," +
                "relation_type.," +
                "relation_manager.";

         var filters = new List<string>();

         if (!string.IsNullOrWhiteSpace(organizationName))
             filters.Add($"q[name]=*{Uri.EscapeDataString(organizationName)}*");

         if (!string.IsNullOrWhiteSpace(industryName))
             filters.Add($"q[industry.name]=*{Uri.EscapeDataString(industryName)}*");

         if (!string.IsNullOrWhiteSpace(relationType))
             filters.Add($"q[relation_type.label]=*{Uri.EscapeDataString(relationType)}*");

         if (!string.IsNullOrWhiteSpace(teamName))
             filters.Add($"q[teams.name]=*{Uri.EscapeDataString(teamName)}*");

         if (!string.IsNullOrWhiteSpace(relationManager))
             filters.Add($"q[relation_manager.name]=*{Uri.EscapeDataString(relationManager)}*");

         if (!string.IsNullOrWhiteSpace(visitingAddressLocality))
             filters.Add($"q[visiting_address.locality]=*{Uri.EscapeDataString(visitingAddressLocality)}*");

         if (offset.HasValue)
             filters.Add($"offset={offset.Value}");

         var filterString = string.Join("&", filters) + $"&select={select}&metadata=count,limit,offset&limit=100&sort=-created_at";

         return await downloadService.GetSimplicatePageAsync<SimplicateOrganization>(
             serviceProvider, requestContext.Server,
             simplicateOptions.GetApiUrl("/crm/organization") + "?" + filterString,
             cancellationToken: cancellationToken);
     }));

    [McpServerTool(OpenWorld = false,
       ReadOnly = true,
       Destructive = false,
       Name = "simplicate_crm_get_organization_totals_by_industry",
       Title = "Get organization totals by industry")]
    [Description("Get organization totals grouped by industry, optionally filtered by organization filters.")]
    public static async Task<CallToolResult?> SimplicateCRM_GetOrganizationTotalsByIndustry(
       IServiceProvider serviceProvider,
       RequestContext<CallToolRequestParams> requestContext,
        [Description("Text value of relation type.")] string? relationType = null,
        [Description("(partial) text value of team name.")] string? teamName = null,
        [Description("(partial) text value of the relation manager name")] string? relationManager = null,
       CancellationToken cancellationToken = default)
       => await requestContext.WithExceptionCheck(async () =>
       await requestContext.WithStructuredContent(async () =>
       {
           var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
           var downloadService = serviceProvider.GetRequiredService<DownloadService>();

           string select = "industry.,name";
           var filters = new List<string>();

           if (!string.IsNullOrWhiteSpace(relationType))
               filters.Add($"q[relation_type.label]=*{Uri.EscapeDataString(relationType)}*");

           if (!string.IsNullOrWhiteSpace(teamName))
               filters.Add($"q[teams.name]=*{Uri.EscapeDataString(teamName)}*");

           if (!string.IsNullOrWhiteSpace(relationManager))
               filters.Add($"q[relation_manager.name]=*{Uri.EscapeDataString(relationManager)}*");

           var filterString = string.Join("&", filters) + $"&select={select}";

           var items = await downloadService.GetAllSimplicatePagesAsync<SimplicateOrganization>(
               serviceProvider, requestContext.Server,
               simplicateOptions.GetApiUrl("/crm/organization"),
               filterString,
               page => $"Downloading organizations page {page}",
               requestContext, cancellationToken: cancellationToken);

           return new
           {
               industries = items.GroupBy(a => a.Industry?.Name ?? "(onbekend)")
                .Select(z => new
                {
                    name = z.Key,
                    count = z.Count()
                })
           };
       }));

    [McpServerTool(OpenWorld = false,
        ReadOnly = true,
        Destructive = false,
        Name = "simplicate_crm_get_organization_totals_by_relation_type",
        Title = "Get organization totals by industry")]
    [Description("Get organization totals grouped by relation type, optionally filtered by organization filters.")]
    public static async Task<CallToolResult?> SimplicateCRM_GetOrganizationTotalsByRelationType(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("(partial) text value of industry name.")] string? industryName = null,
        [Description("(partial) text value of team name.")] string? teamName = null,
        [Description("(partial) text value of the relation manager name")] string? relationManager = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
     await requestContext.WithStructuredContent(async () =>
    {
        var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();

        string select = "relation_type.,name";
        var filters = new List<string>();

        if (!string.IsNullOrWhiteSpace(industryName))
            filters.Add($"q[industry.name]=*{Uri.EscapeDataString(industryName)}*");

        if (!string.IsNullOrWhiteSpace(teamName))
            filters.Add($"q[teams.name]=*{Uri.EscapeDataString(teamName)}*");

        if (!string.IsNullOrWhiteSpace(relationManager))
            filters.Add($"q[relation_manager.name]=*{Uri.EscapeDataString(relationManager)}*");

        var filterString = string.Join("&", filters) + $"&select={select}";

        var items = await downloadService.GetAllSimplicatePagesAsync<SimplicateOrganization>(
            serviceProvider, requestContext.Server,
            simplicateOptions.GetApiUrl("/crm/organization"),
            filterString,
            page => $"Downloading organizations page {page}",
            requestContext, cancellationToken: cancellationToken);

        return new
        {
            relation_types = items.GroupBy(a => a.RelationType?.Label ?? "(onbekend)")
             .Select(z => new
             {
                 name = z.Key,
                 color = z.FirstOrDefault()?.RelationType?.Color,
                 count = z.Count()
             })
        };
    }));

    [Description("Get my organization profiles")]
    [McpServerTool(
     Title = "Get my organization profiles",
     Name = "simplicate_crm_get_my_organization_profiles",
     OpenWorld = false, ReadOnly = true)]
    public static async Task<CallToolResult?> SimplicateCRM_GetMyOrganzizationProfiles(
         IServiceProvider serviceProvider,
         RequestContext<CallToolRequestParams> requestContext,
         CancellationToken cancellationToken = default) => await requestContext.WithStructuredContent(async () =>
     {
         var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
         var downloadService = serviceProvider.GetRequiredService<DownloadService>();

         var items = await downloadService.GetAllSimplicatePagesAsync<SimplicateMyOrganizationProfile>(
             serviceProvider, requestContext.Server,
             simplicateOptions.GetApiUrl("/crm/myorganizationprofile"),
             string.Join("&", new[]
             {
                    "sort=name"
             }),
             page => $"Downloading my organization profiles page {page}",
             requestContext, cancellationToken: cancellationToken);

         return new SimplicateData<SimplicateMyOrganizationProfile>()
         {
             Data = items,
             Metadata = new SimplicateMetadata()
             {
                 Count = items.Count,
             }
         };
     });

    [Description("Create a new organization in Simplicate CRM")]
    [McpServerTool(Title = "Create new organization in Simplicate",
    Destructive = true,
    ReadOnly = false,
    Idempotent = false,
    OpenWorld = false)]
    public static async Task<CallToolResult?> SimplicateCRM_CreateOrganization(
        [Description("The full name of the organization.")] string name,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Industry id.")] string? industryId = null,
        [Description("The organization relation type id.")] string? relationTypeId = null,
        [Description("A note or description about the organization.")] string? note = null,
        [Description("The primary email address for the organization.")] string? email = null,
        [Description("The main website URL of the organization.")] Uri? url = null,
        [Description("LinkedIn url.")] Uri? linkedInUrl = null,
        CancellationToken cancellationToken = default) => await serviceProvider.PostSimplicateResourceAsync(
                requestContext,
                "/crm/organization",
                new SimplicateNewOrganization
               {
                   Name = name,
                   Note = note,
                   LinkedInUrl = linkedInUrl,
                   Email = email,
                   IsActive = true,
                   Url = url,
                   Industry = industryId,
                   RelationTypeId = relationTypeId
                },
                  dto => new
                  {
                      name = dto.Name,
                     note = dto.Note,
                     email = dto.Email,
                     coc_code = dto.CocCode,
                      phone = dto.Phone,
                      linkedin_url = dto.LinkedInUrl,
                      vat_number = dto.VatNumber,
                      url = dto.Url,
                       teams = dto.Teams.BuildSimplicateTeamAssignments(),
                       industry = !string.IsNullOrEmpty(dto.Industry) ? new
                       {
                           id = dto.Industry
                       } : null,
                       relation_type = !string.IsNullOrEmpty(dto.RelationTypeId) ? new
                       {
                           id = dto.RelationTypeId
                       } : null,
                       relation_manager = !string.IsNullOrEmpty(dto.RelationManagerId) ? new
                       {
                           id = dto.RelationManagerId
                       } : null
                  },
                  GetOrganizationWriteElicitOverridesAsync,
                  cancellationToken
             );


    [Description("Update an organization in Simplicate CRM")]
    [McpServerTool(Title = "Update organization in Simplicate",
            ReadOnly = false,
            Idempotent = false,
            Destructive = true,
            OpenWorld = false)]
    public static async Task<CallToolResult?> SimplicateCRM_UpdateOrganization(
            string organizationId,
            IServiceProvider serviceProvider,
            RequestContext<CallToolRequestParams> requestContext,
            string? name = null,
            string? note = null,
            string? email = null,
            string? phone = null,
            Uri? url = null,
            string? industryId = null,
            string? relationTypeId = null,
            string? relationManagerId = null,
            string? cocCode = null,
            string? vatNumber = null,
            bool? isActive = null,
            Uri? linkedinUrl = null,
            CancellationToken cancellationToken = default)
    {
        var dto = new SimplicateNewOrganization
        {
            Name = name,
            Note = note,
            Email = email,
            Phone = phone,
            RelationManagerId = relationManagerId,
            RelationTypeId = relationTypeId,
            CocCode = cocCode,
            IsActive = isActive,
            LinkedInUrl = linkedinUrl,
            VatNumber = vatNumber,
            Url = url,
            Industry = industryId
        };

        return await serviceProvider.PutSimplicateResourceMergedAsync<SimplicateOrganization, SimplicateNewOrganization>(
            requestContext,
            "/crm/organization/" + organizationId.EnsurePrefix("organization"),
            dto,
            (existingOrganization, d) => new
            {
                name = d.Name,
                note = d.Note,
                email = d.Email,
                coc_code = d.CocCode,
                is_active = d.IsActive,
                phone = d.Phone,
                linkedin_url = d.LinkedInUrl,
                vat_number = d.VatNumber,
                url = d.Url,
                industry = !string.IsNullOrEmpty(d.Industry)
                    ? new { id = d.Industry }
                    : null,
                relation_type = !string.IsNullOrEmpty(d.RelationTypeId)
                    ? new { id = d.RelationTypeId }
                    : null,
                relation_manager = !string.IsNullOrEmpty(d.RelationManagerId)
                    ? new { id = d.RelationManagerId }
                    : null,
                teams = d.Teams.BuildSimplicateTeamAssignments(existingOrganization?.Teams?.Select(team => team.Id))
            },
            MapOrganizationToWriteModel,
            GetOrganizationWriteElicitOverridesAsync,
             cancellationToken);
    }

    private static async Task<IReadOnlyDictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>?>
        GetOrganizationWriteElicitOverridesAsync(
            IServiceProvider serviceProvider,
            RequestContext<CallToolRequestParams> requestContext,
            SimplicateNewOrganization dto,
            CancellationToken cancellationToken)
    {
        var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();

        var industries = await downloadService.GetAllSimplicatePagesAsync<SimplicateIndustry>(
            serviceProvider,
            requestContext.Server,
            simplicateOptions.GetApiUrl("/crm/industry"),
            "sort=name&select=id,name",
            page => $"Downloading industries page {page}",
            requestContext,
            cancellationToken: cancellationToken);

        var industryOptions = industries
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => x.Name ?? x.Id, StringComparer.OrdinalIgnoreCase)
            .Select(x => new ElicitRequestParams.EnumSchemaOption
            {
                Title = string.IsNullOrWhiteSpace(x.Name) ? x.Id : x.Name,
                Const = x.Id
            })
            .ToArray();

        ElicitRequestParams.PrimitiveSchemaDefinition industrySchema = industryOptions.Length > 0
            ? new ElicitRequestParams.TitledSingleSelectEnumSchema
            {
                Title = "Industry",
                Description = "Select the organization industry. Clients see the industry names, while the submitted value remains the Simplicate industry id.",
                Default = dto.Industry,
                OneOf = industryOptions
            }
            : new ElicitRequestParams.StringSchema
            {
                Title = "Industry",
                Description = "Industry id.",
                Default = dto.Industry
            };

        var overrides = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["industry"] = industrySchema
        };

        var employeeOverrides = await serviceProvider.BuildSimplicateEmployeeElicitOverridesAsync<SimplicateNewOrganization>(
            requestContext,
            [
                new SimplicateElicitFieldOverride
                {
                    PropertyName = nameof(SimplicateNewOrganization.RelationManagerId),
                    Title = "Relation manager",
                    Description = "Select the organization relation manager.",
                    DefaultValue = dto.RelationManagerId
                }
            ],
            cancellationToken);

        foreach (var employeeOverride in employeeOverrides)
            overrides[employeeOverride.Key] = employeeOverride.Value;

        var relationTypeOverrides = await serviceProvider.BuildSimplicateRelationTypeElicitOverridesAsync<SimplicateNewOrganization>(
            requestContext,
            [
                new SimplicateElicitFieldOverride
                {
                    PropertyName = nameof(SimplicateNewOrganization.RelationTypeId),
                    Title = "Relation type",
                    Description = "Select the organization relation type.",
                    DefaultValue = dto.RelationTypeId
                }
            ],
            relationTypeScope: "crm",
            cancellationToken: cancellationToken);

        foreach (var relationTypeOverride in relationTypeOverrides)
            overrides[relationTypeOverride.Key] = relationTypeOverride.Value;

        var teamOverrides = await serviceProvider.BuildSimplicateTeamsElicitOverridesAsync<SimplicateNewOrganization>(
            requestContext,
            [
                new SimplicateElicitFieldOverride
                {
                    PropertyName = nameof(SimplicateNewOrganization.Teams),
                    Title = "Teams",
                    Description = "Select one or more teams for the organization.",
                    DefaultValues = dto.Teams
                }
            ],
            cancellationToken);

        foreach (var teamOverride in teamOverrides)
            overrides[teamOverride.Key] = teamOverride.Value;

        return overrides;
    }

    private static SimplicateNewOrganization MapOrganizationToWriteModel(SimplicateOrganization organization)
        => new()
        {
            Name = organization.Name,
            Note = organization.Note,
            Email = organization.Email,
            Phone = organization.Phone,
            Url = TryCreateAbsoluteUri(organization.Url),
            LinkedInUrl = TryCreateAbsoluteUri(organization.LinkedinUrl),
            CocCode = organization.CocCode,
            VatNumber = organization.VatNumber,
            Industry = organization.Industry?.Id,
            RelationTypeId = organization.RelationType?.Id,
            RelationManagerId = organization.RelationManager?.Id,
            Teams = organization.Teams?
                .Where(team => !string.IsNullOrWhiteSpace(team.Id))
                .Select(team => team.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            IsActive = organization.IsActive,
        };

    private static Uri? TryCreateAbsoluteUri(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
            ? uri
            : null;

    [Description("Delete an organization in Simplicate CRM after typed confirmation of the exact organization id.")]
    [McpServerTool(Title = "Delete organization in Simplicate",
        Name = "simplicate_crm_delete_organization",
        ReadOnly = false,
        Idempotent = false,
        Destructive = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> SimplicateCRM_DeleteOrganization(
        [Description("The Simplicate organization id.")] string organizationId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
    {
        var normalizedOrganizationId = organizationId.EnsurePrefix("organization");

        return await requestContext.ConfirmAndDeleteAsync<ConfirmDeleteSimplicateOrganization>(
            expectedName: normalizedOrganizationId,
            async ct => await serviceProvider.DeleteSimplicateResourceAsync(
                requestContext,
                "/crm/organization/" + normalizedOrganizationId,
                $"Organization '{normalizedOrganizationId}' deleted.",
                ct),
            $"Organization '{normalizedOrganizationId}' deleted.",
            cancellationToken);
    }
}

[Description("Please confirm deletion of the Simplicate organization id: {0}")]
public sealed class ConfirmDeleteSimplicateOrganization : IHasName
{
    [Description("Type the exact organization id to confirm deletion.")]
    public string? Name { get; set; }
}

