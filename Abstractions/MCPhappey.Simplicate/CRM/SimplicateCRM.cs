using System.ComponentModel;
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
    [McpServerTool(OpenWorld = false,
        ReadOnly = true,
        Destructive = false,
        Name = "simplicate_crm_get_organizations",
        Title = "Get organizations")]
    [Description("Get organizations, filtered by organization filters.")]
    public static async Task<CallToolResult?> SimplicateCRM_GetOrganizations(
     IServiceProvider serviceProvider,
     RequestContext<CallToolRequestParams> requestContext,
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
        [Description("Industry id.")] string industryId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
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
                   Url = url,
                   IndustryId = industryId
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
                    industry = !string.IsNullOrEmpty(dto.IndustryId) ? new
                    {
                        id = dto.IndustryId
                    } : null
                },
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
            string? cocCode = null,
            string? vatNumber = null,
            Uri? linkedinUrl = null,
            CancellationToken cancellationToken = default)
    {
        var dto = new SimplicateNewOrganization
        {
            Name = name,
            Note = note,
            Email = email,
            Phone = phone,
            CocCode = cocCode,
            LinkedInUrl = linkedinUrl,
            VatNumber = vatNumber,
            Url = url,
            IndustryId = industryId
        };

        return await serviceProvider.PutSimplicateResourceMergedAsync(
            requestContext,
            "/crm/organization/" + organizationId,
            dto,
            d => new
            {
                name = d.Name,
                note = d.Note,
                email = d.Email,
                coc_code = d.CocCode,
                phone = d.Phone,
                linkedin_url = d.LinkedInUrl,
                vat_number = d.VatNumber,
                url = d.Url,
                industry = !string.IsNullOrEmpty(d.IndustryId)
                    ? new { id = d.IndustryId }
                    : null
            },
            cancellationToken);
    }


    [Description("Create a new person in Simplicate CRM")]
    [McpServerTool(Title = "Create new person in Simplicate", 
        ReadOnly = false,
        Idempotent = false,
        Destructive = true, OpenWorld = false)]
    public static async Task<CallToolResult?> SimplicateCRM_CreatePerson(
        [Description("The person's first name.")] string firstName,
        [Description("The person's family name or surname.")] string familyName,
      IServiceProvider serviceProvider,
      RequestContext<CallToolRequestParams> requestContext,
        [Description("A note or comment about the person.")] string? note = null,
        [Description("The person's primary email address.")] string? email = null,
        [Description("The person's phone number.")] string? phone = null,
        [Description("The person's website URL, if available.")] Uri? websiteUrl = null,
      CancellationToken cancellationToken = default) => await serviceProvider.PostSimplicateResourceAsync(
        requestContext,
        "/crm/person",
        new SimplicateNewPerson
        {
            FirstName = firstName,
            FamilyName = familyName,
            Note = note,
            Email = email,
            Phone = phone,
            WebsiteUrl = websiteUrl
        },
        cancellationToken
    );
}

