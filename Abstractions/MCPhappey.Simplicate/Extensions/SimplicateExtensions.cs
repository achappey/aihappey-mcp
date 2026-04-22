using System.Reflection;
using System.Text.Json;
using MCPhappey.Common;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Simplicate.Options;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using static MCPhappey.Simplicate.CRM.SimplicateCRM;

namespace MCPhappey.Simplicate.Extensions;

public static class SimplicateExtensions
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    { PropertyNameCaseInsensitive = true };

    private static async Task<IReadOnlyDictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>?>
        BuildElicitPropertyOverridesAsync<TDto>(
            this IServiceProvider serviceProvider,
            RequestContext<CallToolRequestParams> requestContext,
            TDto dto,
            Func<IServiceProvider, RequestContext<CallToolRequestParams>, TDto, CancellationToken,
                Task<IReadOnlyDictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>?>>?
                elicitPropertyOverridesFactory,
            CancellationToken cancellationToken)
        where TDto : class
        => elicitPropertyOverridesFactory == null
            ? null
            : await elicitPropertyOverridesFactory(serviceProvider, requestContext, dto, cancellationToken);

    public static async Task<IReadOnlyDictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>>
        BuildSimplicateEmployeeElicitOverridesAsync<TDto>(
            this IServiceProvider serviceProvider,
            RequestContext<CallToolRequestParams> requestContext,
            IReadOnlyCollection<SimplicateElicitFieldOverride> fields,
            CancellationToken cancellationToken = default)
        where TDto : class
    {
        if (fields.Count == 0)
            return new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>(StringComparer.OrdinalIgnoreCase);

        var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();

        var teams = await downloadService.GetAllSimplicatePagesAsync<SimplicateTeamEmployeeCollection>(
            serviceProvider,
            requestContext.Server,
            simplicateOptions.GetApiUrl("/hrm/team"),
            "sort=name",
            page => $"Downloading Simplicate team employees page {page}",
            requestContext,
            cancellationToken: cancellationToken);

        var employeeOptions = teams
            .SelectMany(team => team.Employees ?? [])
            .Where(employee => !string.IsNullOrWhiteSpace(employee.Id))
            .GroupBy(employee => employee.Id!, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(employee => !string.IsNullOrWhiteSpace(employee.Name))
                .ThenBy(employee => employee.Name ?? employee.Id, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderBy(employee => employee.Name ?? employee.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(employee => employee.Id, StringComparer.OrdinalIgnoreCase)
            .Select(employee => new ElicitRequestParams.EnumSchemaOption
            {
                Title = string.IsNullOrWhiteSpace(employee.Name) ? employee.Id! : employee.Name,
                Const = employee.Id!
            })
            .ToArray();

        return fields
            .Select(field => CreateEmployeeFieldOverrideDefinition<TDto>(field, employeeOptions))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    public static async Task<IReadOnlyDictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>>
        BuildSimplicatePersonElicitOverridesAsync<TDto>(
            this IServiceProvider serviceProvider,
            RequestContext<CallToolRequestParams> requestContext,
            IReadOnlyCollection<SimplicateElicitFieldOverride> fields,
            CancellationToken cancellationToken = default)
        where TDto : class
        => await serviceProvider.BuildSimplicateSingleSelectLookupElicitOverridesAsync<TDto, SimplicatePersonLookupItem>(
            requestContext,
            fields,
            "/crm/person",
            "sort=full_name&select=id,full_name",
            page => $"Downloading Simplicate persons page {page}",
            item => item.Id,
            item => item.FullName,
            "Person id.",
            cancellationToken);

    public static async Task<IReadOnlyDictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>>
        BuildSimplicateEmployeeStatusElicitOverridesAsync<TDto>(
            this IServiceProvider serviceProvider,
            RequestContext<CallToolRequestParams> requestContext,
            IReadOnlyCollection<SimplicateElicitFieldOverride> fields,
            CancellationToken cancellationToken = default)
        where TDto : class
        => await serviceProvider.BuildSimplicateSingleSelectLookupElicitOverridesAsync<TDto, SimplicateIdLabelLookupItem>(
            requestContext,
            fields,
            "/hrm/employeestatus",
            "sort=label&select=id,label",
            page => $"Downloading Simplicate employee statuses page {page}",
            item => item.Id,
            item => item.Label,
            "Employee status id.",
            cancellationToken);

    public static async Task<IReadOnlyDictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>>
        BuildSimplicateAbsenceTypeElicitOverridesAsync<TDto>(
            this IServiceProvider serviceProvider,
            RequestContext<CallToolRequestParams> requestContext,
            IReadOnlyCollection<SimplicateElicitFieldOverride> fields,
            CancellationToken cancellationToken = default)
        where TDto : class
        => await serviceProvider.BuildSimplicateSingleSelectLookupElicitOverridesAsync<TDto, SimplicateIdLabelLookupItem>(
            requestContext,
            fields,
            "/hrm/absencetype",
            "sort=label&select=id,label",
            page => $"Downloading Simplicate absence types page {page}",
            item => item.Id,
            item => item.Label,
            "Absence type id.",
            cancellationToken);

    public static async Task<IReadOnlyDictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>>
        BuildSimplicateLeaveTypeElicitOverridesAsync<TDto>(
            this IServiceProvider serviceProvider,
            RequestContext<CallToolRequestParams> requestContext,
            IReadOnlyCollection<SimplicateElicitFieldOverride> fields,
            CancellationToken cancellationToken = default)
        where TDto : class
        => await serviceProvider.BuildSimplicateSingleSelectLookupElicitOverridesAsync<TDto, SimplicateIdLabelLookupItem>(
            requestContext,
            fields,
            "/hrm/leavetype",
            "sort=label&select=id,label",
            page => $"Downloading Simplicate leave types page {page}",
            item => item.Id,
            item => item.Label,
            "Leave type id.",
            cancellationToken);

    private static async Task<IReadOnlyDictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>>
        BuildSimplicateSingleSelectLookupElicitOverridesAsync<TDto, TItem>(
            this IServiceProvider serviceProvider,
            RequestContext<CallToolRequestParams> requestContext,
            IReadOnlyCollection<SimplicateElicitFieldOverride> fields,
            string endpoint,
            string query,
            Func<int, string> progressMessageFactory,
            Func<TItem, string?> idSelector,
            Func<TItem, string?> titleSelector,
            string fallbackDescription,
            CancellationToken cancellationToken = default)
        where TDto : class
        where TItem : class
    {
        if (fields.Count == 0)
            return new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>(StringComparer.OrdinalIgnoreCase);

        var options = await serviceProvider.TryBuildSimplicateLookupOptionsAsync(
            requestContext,
            endpoint,
            query,
            progressMessageFactory,
            idSelector,
            titleSelector,
            cancellationToken);

        return fields
            .Select(field => CreateSingleSelectLookupFieldOverrideDefinition<TDto>(field, options, fallbackDescription))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<IReadOnlyCollection<ElicitRequestParams.EnumSchemaOption>>
        TryBuildSimplicateLookupOptionsAsync<TItem>(
            this IServiceProvider serviceProvider,
            RequestContext<CallToolRequestParams> requestContext,
            string endpoint,
            string query,
            Func<int, string> progressMessageFactory,
            Func<TItem, string?> idSelector,
            Func<TItem, string?> titleSelector,
            CancellationToken cancellationToken = default)
        where TItem : class
    {
        try
        {
            var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
            var downloadService = serviceProvider.GetRequiredService<DownloadService>();

            var items = await downloadService.GetAllSimplicatePagesAsync<TItem>(
                serviceProvider,
                requestContext.Server,
                simplicateOptions.GetApiUrl(endpoint),
                query,
                progressMessageFactory,
                requestContext,
                cancellationToken: cancellationToken);

            return items
                .Select(item => new
                {
                    Id = idSelector(item),
                    Title = titleSelector(item)
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.Id))
                .GroupBy(item => item.Id!, StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderByDescending(item => !string.IsNullOrWhiteSpace(item.Title))
                    .ThenBy(item => item.Title ?? item.Id, StringComparer.OrdinalIgnoreCase)
                    .First())
                .OrderBy(item => item.Title ?? item.Id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .Select(item => new ElicitRequestParams.EnumSchemaOption
                {
                    Title = string.IsNullOrWhiteSpace(item.Title) ? item.Id! : item.Title,
                    Const = item.Id!
                })
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static KeyValuePair<string, ElicitRequestParams.PrimitiveSchemaDefinition>
        CreateSingleSelectLookupFieldOverrideDefinition<TDto>(
            SimplicateElicitFieldOverride field,
            IReadOnlyCollection<ElicitRequestParams.EnumSchemaOption> options,
            string fallbackDescription)
        where TDto : class
    {
        var property = typeof(TDto).GetProperty(
            field.PropertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);

        var jsonPropertyName = property?.GetJsonPropertyName() ?? field.PropertyName;
        var title = string.IsNullOrWhiteSpace(field.Title)
            ? property?.Name ?? field.PropertyName
            : field.Title;
        var description = string.IsNullOrWhiteSpace(field.Description)
            ? property?.GetDescription()
            : field.Description;

        ElicitRequestParams.PrimitiveSchemaDefinition schema = options.Count > 0
            ? new ElicitRequestParams.TitledSingleSelectEnumSchema
            {
                Title = title,
                Description = description,
                Default = field.DefaultValue,
                OneOf = [.. options]
            }
            : new ElicitRequestParams.StringSchema
            {
                Title = title,
                Description = description ?? fallbackDescription,
                Default = field.DefaultValue
            };

        return new KeyValuePair<string, ElicitRequestParams.PrimitiveSchemaDefinition>(jsonPropertyName, schema);
    }

    private static KeyValuePair<string, ElicitRequestParams.PrimitiveSchemaDefinition>
        CreateEmployeeFieldOverrideDefinition<TDto>(
            SimplicateElicitFieldOverride field,
            IReadOnlyCollection<ElicitRequestParams.EnumSchemaOption> employeeOptions)
        where TDto : class
    {
        var property = typeof(TDto).GetProperty(
            field.PropertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);

        var jsonPropertyName = property?.GetJsonPropertyName() ?? field.PropertyName;
        var title = string.IsNullOrWhiteSpace(field.Title)
            ? property?.Name ?? field.PropertyName
            : field.Title;
        var description = string.IsNullOrWhiteSpace(field.Description)
            ? property?.GetDescription()
            : field.Description;

        ElicitRequestParams.PrimitiveSchemaDefinition schema = employeeOptions.Count > 0
            ? new ElicitRequestParams.TitledSingleSelectEnumSchema
            {
                Title = title,
                Description = description,
                Default = field.DefaultValue,
                OneOf = [.. employeeOptions]
            }
            : new ElicitRequestParams.StringSchema
            {
                Title = title,
                Description = description ?? "Employee id.",
                Default = field.DefaultValue
            };

        return new KeyValuePair<string, ElicitRequestParams.PrimitiveSchemaDefinition>(jsonPropertyName, schema);
    }

    public static async Task<IReadOnlyDictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>>
        BuildSimplicateRelationTypeElicitOverridesAsync<TDto>(
            this IServiceProvider serviceProvider,
            RequestContext<CallToolRequestParams> requestContext,
            IReadOnlyCollection<SimplicateElicitFieldOverride> fields,
            string? relationTypeScope = null,
            CancellationToken cancellationToken = default)
        where TDto : class
    {
        if (fields.Count == 0)
            return new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>(StringComparer.OrdinalIgnoreCase);

        var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var relationTypeQuery = string.IsNullOrWhiteSpace(relationTypeScope)
            ? "sort=label&select=id,label,color,type"
            : $"q[type]={Uri.EscapeDataString(relationTypeScope)}&sort=label&select=id,label,color,type";

        var relationTypes = await downloadService.GetAllSimplicatePagesAsync<SimplicateRelationType>(
            serviceProvider,
            requestContext.Server,
            simplicateOptions.GetApiUrl("/crm/relationtype"),
            relationTypeQuery,
            page => $"Downloading Simplicate relation types page {page}",
            requestContext,
            cancellationToken: cancellationToken);

        var relationTypeOptions = relationTypes
            .Where(relationType => !string.IsNullOrWhiteSpace(relationType.Id))
            .GroupBy(relationType => relationType.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(relationType => !string.IsNullOrWhiteSpace(relationType.Label))
                .ThenBy(relationType => relationType.Label ?? relationType.Id, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderBy(relationType => relationType.Label ?? relationType.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(relationType => relationType.Id, StringComparer.OrdinalIgnoreCase)
            .Select(relationType => new ElicitRequestParams.EnumSchemaOption
            {
                Title = BuildRelationTypeOptionTitle(relationType),
                Const = relationType.Id
            })
            .ToArray();

        return fields
            .Select(field => CreateRelationTypeFieldOverrideDefinition<TDto>(field, relationTypeOptions))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static string BuildRelationTypeOptionTitle(SimplicateRelationType relationType)
    {
        return string.IsNullOrWhiteSpace(relationType.Label)
            ? relationType.Id
            : relationType.Label;
    }

    private static KeyValuePair<string, ElicitRequestParams.PrimitiveSchemaDefinition>
        CreateRelationTypeFieldOverrideDefinition<TDto>(
            SimplicateElicitFieldOverride field,
            IReadOnlyCollection<ElicitRequestParams.EnumSchemaOption> relationTypeOptions)
        where TDto : class
    {
        var property = typeof(TDto).GetProperty(
            field.PropertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);

        var jsonPropertyName = property?.GetJsonPropertyName() ?? field.PropertyName;
        var title = string.IsNullOrWhiteSpace(field.Title)
            ? property?.Name ?? field.PropertyName
            : field.Title;
        var description = string.IsNullOrWhiteSpace(field.Description)
            ? property?.GetDescription()
            : field.Description;

        ElicitRequestParams.PrimitiveSchemaDefinition schema = relationTypeOptions.Count > 0
            ? new ElicitRequestParams.TitledSingleSelectEnumSchema
            {
                Title = title,
                Description = description,
                Default = field.DefaultValue,
                OneOf = [.. relationTypeOptions]
            }
            : new ElicitRequestParams.StringSchema
            {
                Title = title,
                Description = description ?? "Relation type id.",
                Default = field.DefaultValue
            };

        return new KeyValuePair<string, ElicitRequestParams.PrimitiveSchemaDefinition>(jsonPropertyName, schema);
    }

    public static async Task<IReadOnlyDictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>>
       BuildSimplicateTeamsElicitOverridesAsync<TDto>(
           this IServiceProvider serviceProvider,
           RequestContext<CallToolRequestParams> requestContext,
           IReadOnlyCollection<SimplicateElicitFieldOverride> fields,
           CancellationToken cancellationToken = default)
       where TDto : class
    {
        if (fields.Count == 0)
            return new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>(StringComparer.OrdinalIgnoreCase);

        var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();

        var teams = await downloadService.GetAllSimplicatePagesAsync<SimplicateTeamEmployeeCollection>(
            serviceProvider,
            requestContext.Server,
            simplicateOptions.GetApiUrl("/hrm/team"),
            "sort=name",
            page => $"Downloading Simplicate team page {page}",
            requestContext,
            cancellationToken: cancellationToken);

        var teamOptions = teams
            .Where(team => !string.IsNullOrWhiteSpace(team.Id))
            .GroupBy(team => team.Id!, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(team => !string.IsNullOrWhiteSpace(team.Name))
                .ThenBy(team => team.Name ?? team.Id, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderBy(team => team.Name ?? team.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(team => team.Id, StringComparer.OrdinalIgnoreCase)
            .Select(team => new ElicitRequestParams.EnumSchemaOption
            {
                Title = string.IsNullOrWhiteSpace(team.Name) ? team.Id! : team.Name,
                Const = team.Id!
            })
            .ToArray();

        return fields
            .Select(field => CreateTeamsFieldOverrideDefinition<TDto>(field, teamOptions))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static KeyValuePair<string, ElicitRequestParams.PrimitiveSchemaDefinition>
        CreateTeamsFieldOverrideDefinition<TDto>(
            SimplicateElicitFieldOverride field,
            IReadOnlyCollection<ElicitRequestParams.EnumSchemaOption> teamOptions)
        where TDto : class
    {
        var property = typeof(TDto).GetProperty(
            field.PropertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);

        var jsonPropertyName = property?.GetJsonPropertyName() ?? field.PropertyName;
        var title = string.IsNullOrWhiteSpace(field.Title)
            ? property?.Name ?? field.PropertyName
            : field.Title;
        var description = string.IsNullOrWhiteSpace(field.Description)
            ? property?.GetDescription()
            : field.Description;
        var defaultValues = field.DefaultValues?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if ((defaultValues == null || defaultValues.Length == 0) && !string.IsNullOrWhiteSpace(field.DefaultValue))
            defaultValues = [field.DefaultValue];

        ElicitRequestParams.PrimitiveSchemaDefinition schema = teamOptions.Count > 0
            ? new ElicitRequestParams.TitledMultiSelectEnumSchema
            {
                Title = title,
                Description = description ?? "Select one or more team ids.",
                Default = defaultValues ?? [],
                Items = new ElicitRequestParams.TitledEnumItemsSchema()
                {
                    AnyOf = [.. teamOptions]
                }
            }
            : new ElicitRequestParams.StringSchema
            {
                Title = title,
                Description = description ?? "Comma-separated team ids.",
                Default = field.DefaultValue
            };

        return new KeyValuePair<string, ElicitRequestParams.PrimitiveSchemaDefinition>(jsonPropertyName, schema);
    }

    public static IReadOnlyList<SimplicateTeamAssignment>? BuildSimplicateTeamAssignments(
        this IEnumerable<string>? selectedTeamIds,
        IEnumerable<string>? existingTeamIds = null)
    {
        var selectedIds = (selectedTeamIds ?? [])
            .Where(teamId => !string.IsNullOrWhiteSpace(teamId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var previousIds = (existingTeamIds ?? [])
            .Where(teamId => !string.IsNullOrWhiteSpace(teamId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var assignments = selectedIds
            .Select(teamId => new SimplicateTeamAssignment
            {
                Id = teamId,
                Value = true
            })
            .Concat(previousIds
                .Except(selectedIds, StringComparer.OrdinalIgnoreCase)
                .Select(teamId => new SimplicateTeamAssignment
                {
                    Id = teamId,
                    Value = false
                }))
            .ToArray();

        return assignments.Length > 0 ? assignments : null;
    }


    public static string EnsurePrefix(
               this string value,
               string prefix)
               => value.StartsWith($"{prefix}:") ? value : $"{prefix}:{value}";

    public static string GetApiUrl(
           this SimplicateOptions options,
           string endpoint)
           => $"https://{options.Organization}.simplicate.app/api/v2{endpoint}";

    public static async Task<SimplicateData<JsonElement>?> GetSimplicatePageAsync(
        this DownloadService downloadService,
        IServiceProvider serviceProvider,
        McpServer mcpServer,
        string url,
        CancellationToken cancellationToken = default)
    {
        var page = await downloadService.ScrapeContentAsync(serviceProvider, mcpServer, url, cancellationToken);
        var stringContent = page?.FirstOrDefault()?.Contents?.ToString();
        if (string.IsNullOrWhiteSpace(stringContent))
            return null;

        return JsonSerializer.Deserialize<SimplicateData<JsonElement>>(stringContent, JsonSerializerOptions);
    }

    public static async Task<List<JsonElement>> GetAllSimplicatePagesAsync(
        this DownloadService downloadService,
        IServiceProvider serviceProvider,
        McpServer mcpServer,
        string baseUrl,
        Func<int, string> progressTextSelector,
        RequestContext<CallToolRequestParams> requestContext,
        int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        var results = new List<JsonElement>();
        int offset = 0;
        int? totalCount = null;
        int? totalPages = null;

        while (true)
        {
            int pageNumber = (offset / pageSize) + 1;
            var builder = new UriBuilder(baseUrl);
            var queryDict = QueryHelpers.ParseQuery(builder.Query);
            queryDict["limit"] = pageSize.ToString();
            queryDict["offset"] = offset.ToString();
            queryDict["metadata"] = "count";
            builder.Query = string.Join("&", queryDict.SelectMany(kvp => kvp.Value.Select(v => $"{kvp.Key}={Uri.EscapeDataString(v!)}")));
            string url = builder.Uri.ToString();

            await requestContext.Server.SendProgressNotificationAsync(
                requestContext,
                pageNumber,
                progressTextSelector(pageNumber),
                totalPages > 0 ? totalPages : null,
                cancellationToken);

            var result = await downloadService.GetSimplicatePageAsync(
                serviceProvider, mcpServer, url, cancellationToken);

            if (result?.Data == null)
                break;

            var markdown =
                $"<details><summary><a href=\"{url}\" target=\"blank\">{new Uri(url).Host}</a></summary>\n\n```\n{JsonSerializer.Serialize(result)}\n```\n</details>";

            await requestContext.Server.SendMessageNotificationAsync(markdown, LoggingLevel.Debug,
                cancellationToken: cancellationToken);

            results.AddRange(result.Data);

            if (totalCount == null && result.Metadata != null)
            {
                totalCount = result.Metadata.Count;
                totalPages = (int)Math.Ceiling((double)totalCount.Value / pageSize);
            }

            offset += pageSize;
            if (totalCount.HasValue && offset >= totalCount.Value)
                break;
        }

        return results;
    }


    public static DateTime? ParseDate(this string? dateString)
    {
        if (string.IsNullOrWhiteSpace(dateString))
            return null;
        if (DateTime.TryParse(dateString, out var dt))
            return dt;
        // eventueel: custom parse logic als je een ISO-formaat of andere verwacht
        return null;
    }


    public static int? ParseInt(this string? intString)
    {
        if (string.IsNullOrWhiteSpace(intString))
            return null;
        if (int.TryParse(intString, out var dt))
            return dt;
        // eventueel: custom parse logic als je een ISO-formaat of andere verwacht
        return null;
    }


    public static decimal ToAmount(this decimal item) =>
         Math.Round(item, 2, MidpointRounding.AwayFromZero);


    public static async Task<SimplicateData<T>?> GetSimplicatePageAsync<T>(
        this DownloadService downloadService,
        IServiceProvider serviceProvider,
        McpServer mcpServer,
        string url,
        CancellationToken cancellationToken = default)
    {
        var page = await downloadService.ScrapeContentAsync(serviceProvider, mcpServer, url, cancellationToken);
        var stringContent = page?.FirstOrDefault()?.Contents?.ToString();

        if (string.IsNullOrWhiteSpace(stringContent))
            return null;

        var opts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        return JsonSerializer.Deserialize<SimplicateData<T>>(stringContent, opts);
    }

    public static async Task<SimplicateItemData<T>?> GetSimplicateItemAsync<T>(
        this DownloadService downloadService,
        IServiceProvider serviceProvider,
        McpServer mcpServer,
        string url,
        CancellationToken cancellationToken = default)
    {
        var page = await downloadService.ScrapeContentAsync(serviceProvider, mcpServer, url, cancellationToken);
        var stringContent = page?.FirstOrDefault()?.Contents?.ToString();

        if (string.IsNullOrWhiteSpace(stringContent))
            return null;

        return JsonSerializer.Deserialize<SimplicateItemData<T>>(stringContent);
    }


    public static async Task<List<T>> GetAllSimplicatePagesAsync<T>(
        this DownloadService downloadService,
        IServiceProvider serviceProvider,
        McpServer mcpServer,
        string baseUrl,
        string filterString,
        Func<int, string> progressTextSelector,
        RequestContext<CallToolRequestParams> requestContext,
        int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        var results = new List<T>();
        int offset = 0;
        int? totalCount = null;
        int? totalPages = null;

        while (true)
        {
            int pageNumber = (offset / pageSize) + 1;
            string url = $"{baseUrl}?{filterString}&limit={pageSize}&offset={offset}&metadata=count";

            await requestContext.Server.SendProgressNotificationAsync(
                requestContext,
                pageNumber,
                progressTextSelector(pageNumber),
                totalPages > 0 ? totalPages : null,
                cancellationToken
            );

            var result = await downloadService.GetSimplicatePageAsync<T>(
                        serviceProvider, mcpServer, url, cancellationToken);

            if (result == null || result.Data == null)
                break;

            var uri = new Uri(url);
            var domain = uri.Host;
            var markdown =
                  $"<details><summary><a href=\"{url}\" target=\"blank\">{domain}</a></summary>\n\n```\n{JsonSerializer.Serialize(result)}\n```\n</details>";
            await requestContext.Server.SendMessageNotificationAsync(markdown, LoggingLevel.Debug);

            results.AddRange(result.Data);

            if (totalCount == null && result.Metadata != null)
            {
                totalCount = result.Metadata.Count;
                totalPages = (int)Math.Ceiling((double)totalCount.Value / pageSize);
            }

            offset += pageSize;
            if (totalCount.HasValue && offset >= totalCount.Value)
                break;
        }

        return results;
    }

    /// <summary>
    /// Common case: elicit and POST the same DTO type.
    /// Requires a public parameterless ctor to satisfy TryElicit's constraint.
    /// </summary>
    public static async Task<CallToolResult?> PostSimplicateResourceAsync<TDto>(
        this IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string relativePath,                   // e.g. "/projects/projectservice"
        TDto seedDto,
        CancellationToken cancellationToken = default)
        where TDto : class, new()              // <-- add new()
        => await serviceProvider.PostSimplicateResourceAsync(
            requestContext,
            relativePath,
            seedDto,
            elicitPropertyOverridesFactory: null,
            cancellationToken);

    /// <summary>
    /// Common case: elicit and POST the same DTO type, with optional per-field elicitation overrides.
    /// </summary>
    public static async Task<CallToolResult?> PostSimplicateResourceAsync<TDto>(
        this IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string relativePath,
        TDto seedDto,
        Func<IServiceProvider, RequestContext<CallToolRequestParams>, TDto, CancellationToken,
            Task<IReadOnlyDictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>?>>?
            elicitPropertyOverridesFactory = null,
        CancellationToken cancellationToken = default)
        where TDto : class, new()              // <-- add new()
    {
        var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
        var url = simplicateOptions.GetApiUrl(relativePath);

        var elicitPropertyOverrides = await serviceProvider.BuildElicitPropertyOverridesAsync(
            requestContext,
            seedDto,
            elicitPropertyOverridesFactory,
            cancellationToken);

        var (dto, notAccepted, _) = await requestContext.Server.TryElicit(
            seedDto,
            elicitPropertyOverrides,
            cancellationToken);
        if (notAccepted != null) return notAccepted;

        var scraper = serviceProvider.GetServices<IContentScraper>()
                                     .OfType<SimplicateScraper>()
                                     .First();

        var content = await scraper.PostSimplicateItemAsync(
            serviceProvider, url, dto!, requestContext: requestContext, cancellationToken: cancellationToken);

        return content?.ToCallToolResult();
    }

    /// <summary>
    /// Common case: elicit a DTO, then map it into the proper JSON structure and POST.
    /// Requires a public parameterless ctor to satisfy TryElicit's constraint.
    /// </summary>
    public static async Task<CallToolResult?> PostSimplicateResourceAsync<TDto>(
        this IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string relativePath,                       // e.g. "/projects/projectservice"
        TDto seedDto,
        Func<TDto, object> mapper,                 // <-- new: map DTO → object/dynamic/anon type
        CancellationToken cancellationToken = default)
        where TDto : class, new()
        => await serviceProvider.PostSimplicateResourceAsync(
            requestContext,
            relativePath,
            seedDto,
            mapper,
            elicitPropertyOverridesFactory: null,
            cancellationToken);

    /// <summary>
    /// Common case: elicit a DTO, then map it into the proper JSON structure and POST.
    /// Supports optional per-field elicitation overrides.
    /// </summary>
    public static async Task<CallToolResult?> PostSimplicateResourceAsync<TDto>(
        this IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string relativePath,
        TDto seedDto,
        Func<TDto, object> mapper,
        Func<IServiceProvider, RequestContext<CallToolRequestParams>, TDto, CancellationToken,
            Task<IReadOnlyDictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>?>>?
            elicitPropertyOverridesFactory = null,
        CancellationToken cancellationToken = default)
        where TDto : class, new()
    {
        var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
        var url = simplicateOptions.GetApiUrl(relativePath);

        var elicitPropertyOverrides = await serviceProvider.BuildElicitPropertyOverridesAsync(
            requestContext,
            seedDto,
            elicitPropertyOverridesFactory,
            cancellationToken);

        // Let Elicit fill the flat DTO
        var (dto, notAccepted, _) = await requestContext.Server.TryElicit(
            seedDto,
            elicitPropertyOverrides,
            cancellationToken);
        if (notAccepted != null) return notAccepted;

        // Map flat DTO into the correct Simplicate structure
        var mappedObject = mapper(dto!);

        var scraper = serviceProvider.GetServices<IContentScraper>()
                                     .OfType<SimplicateScraper>()
                                     .First();

        var content = await scraper.PostSimplicateItemAsync(
            serviceProvider,
            url,
            mappedObject,
            requestContext: requestContext,
            cancellationToken: cancellationToken);

        return content?.ToCallToolResult();
    }

    public static async Task<CallToolResult?> PutSimplicateResourceMergedAsync<TDto>(
        this IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string relativePath,       // e.g. "/crm/organization/{id}"
        TDto incomingDto,          // partial update data
        Func<TDto, object> mapper, // maps final dto → PUT body
        CancellationToken cancellationToken = default)
        where TDto : class, new()
        => await serviceProvider.PutSimplicateResourceMergedAsync<TDto, TDto>(
            requestContext,
            relativePath,
            incomingDto,
            mapper,
            existing => existing,
            elicitPropertyOverridesFactory: null,
            cancellationToken);

    public static async Task<CallToolResult?> PutSimplicateResourceMergedAsync<TDto>(
        this IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string relativePath,
        TDto incomingDto,
        Func<TDto, object> mapper,
        Func<IServiceProvider, RequestContext<CallToolRequestParams>, TDto, CancellationToken,
            Task<IReadOnlyDictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>?>>?
            elicitPropertyOverridesFactory = null,
        CancellationToken cancellationToken = default)
        where TDto : class, new()
        => await serviceProvider.PutSimplicateResourceMergedAsync<TDto, TDto>(
            requestContext,
            relativePath,
            incomingDto,
            mapper,
            existing => existing,
            elicitPropertyOverridesFactory,
            cancellationToken);

    public static async Task<CallToolResult?> PutSimplicateResourceMergedAsync<TExisting, TDto>(
        this IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string relativePath,
        TDto incomingDto,
        Func<TDto, object> mapper,
        Func<TExisting, TDto> existingToDto,
        Func<IServiceProvider, RequestContext<CallToolRequestParams>, TDto, CancellationToken,
            Task<IReadOnlyDictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>?>>?
            elicitPropertyOverridesFactory = null,
        CancellationToken cancellationToken = default)
        where TExisting : class
        where TDto : class, new()
        => await serviceProvider.PutSimplicateResourceMergedAsync(
            requestContext,
            relativePath,
            incomingDto,
            (_, dto) => mapper(dto),
            existingToDto,
            elicitPropertyOverridesFactory,
            cancellationToken);

    public static async Task<CallToolResult?> PutSimplicateResourceMergedAsync<TExisting, TDto>(
        this IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string relativePath,
        TDto incomingDto,
        Func<TExisting?, TDto, object> mapper,
        Func<TExisting, TDto> existingToDto,
        Func<IServiceProvider, RequestContext<CallToolRequestParams>, TDto, CancellationToken,
            Task<IReadOnlyDictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>?>>?
            elicitPropertyOverridesFactory = null,
        CancellationToken cancellationToken = default)
        where TExisting : class
        where TDto : class, new()
    {
        var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var url = simplicateOptions.GetApiUrl(relativePath);

        // 1️⃣ Fetch existing item first
        var existing = await downloadService.GetSimplicateItemAsync<TExisting>(
            serviceProvider, requestContext.Server, url, cancellationToken);

        var baseDto = existing?.Data != null
            ? existingToDto(existing.Data) ?? new TDto()
            : new TDto();

        // 2️⃣ Pre-fill incomingDto with defaults from existing
        foreach (var prop in typeof(TDto).GetProperties())
        {
            var incomingVal = prop.GetValue(incomingDto);
            if (incomingVal == null)
            {
                var existingVal = prop.GetValue(baseDto);
                if (existingVal != null)
                    prop.SetValue(incomingDto, existingVal);
            }
        }

        // 3️⃣ Let user/AI elicit interactively (with defaults prefilled)
        var elicitPropertyOverrides = await serviceProvider.BuildElicitPropertyOverridesAsync(
            requestContext,
            incomingDto,
            elicitPropertyOverridesFactory,
            cancellationToken);

        var (dto, notAccepted, _) = await requestContext.Server.TryElicit(
            incomingDto,
            elicitPropertyOverrides,
            cancellationToken);
        if (notAccepted != null) return notAccepted;

        // 4️⃣ Merge: prefer elicited non-nulls over existing
        foreach (var prop in typeof(TDto).GetProperties())
        {
            var newVal = prop.GetValue(dto);
            if (newVal != null)
                prop.SetValue(baseDto, newVal);
        }

        // 5️⃣ Map and PUT
        var mappedObject = mapper(existing?.Data, baseDto);
        var scraper = serviceProvider.GetServices<IContentScraper>()
                                     .OfType<SimplicateScraper>()
                                     .First();

        var content = await scraper.PutSimplicateItemAsync(
            serviceProvider, url, mappedObject,
            requestContext: requestContext, cancellationToken: cancellationToken);

        return content?.ToCallToolResult();
    }

    public static async Task<ContentBlock?> PostSimplicateItemAsync<T>(
          this IServiceProvider serviceProvider,
          string baseUrl, // e.g. "https://{subdomain}.simplicate.nl/api/v2/project/project"
          T item,
          RequestContext<CallToolRequestParams> requestContext,
          CancellationToken cancellationToken = default)
    {
        var scraper = serviceProvider.GetServices<IContentScraper>()
         .OfType<SimplicateScraper>().First();

        return await scraper.PostSimplicateItemAsync(
         serviceProvider,
         baseUrl,
         item,
         requestContext: requestContext,
         cancellationToken: cancellationToken
     );
    }

    public static async Task<ContentBlock?> PostSimplicateItemAsync<T>(
        this SimplicateScraper downloadService,
        IServiceProvider serviceProvider,
        string baseUrl, // e.g. "https://{subdomain}.simplicate.nl/api/v2/project/project"
        T item,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(item, jsonOptions);

        if (LoggingLevel.Debug.ShouldLog(requestContext.Server.LoggingLevel))
        {
            await requestContext.Server.SendMessageNotificationAsync(
                $"<details><summary>POST <code>{baseUrl}</code></summary>\n\n```\n{json}\n```\n</details>",
                LoggingLevel.Debug
            );
        }

        // Use your DownloadService to POST (assumes similar signature to ScrapeContentAsync)
        var response = await downloadService.PostContentAsync<T>(
            serviceProvider, baseUrl, json, cancellationToken);

        if (LoggingLevel.Debug.ShouldLog(requestContext.Server.LoggingLevel))
        {
            await requestContext.Server.SendMessageNotificationAsync(
                $"<details><summary>RESPONSE</summary>\n\n```\n{JsonSerializer.Serialize(response,
                    ResourceExtensions.JsonSerializerOptions)}\n```\n</details>",
                LoggingLevel.Debug
            );
        }

        return response.ToJsonContentBlock($"{baseUrl}/{response?.Data.Id}");
    }

    private static JsonSerializerOptions jsonOptions = new(JsonSerializerOptions.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task<ContentBlock?> PutSimplicateItemAsync<T>(
        this SimplicateScraper downloadService,
        IServiceProvider serviceProvider,
        string baseUrl,
        T item,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(item, jsonOptions);

        if (LoggingLevel.Debug.ShouldLog(requestContext.Server.LoggingLevel))
        {
            await requestContext.Server.SendMessageNotificationAsync(
                $"<details><summary>PUT <code>{baseUrl}</code></summary>\n\n```\n{json}\n```\n</details>",
                LoggingLevel.Debug
            );
        }

        // Use your DownloadService to POST (assumes similar signature to ScrapeContentAsync)
        var response = await downloadService.PutContentAsync<T>(
            serviceProvider, baseUrl, json, cancellationToken);

        if (LoggingLevel.Debug.ShouldLog(requestContext.Server.LoggingLevel))
        {
            await requestContext.Server.SendMessageNotificationAsync(
                $"<details><summary>RESPONSE</summary>\n\n```\n{JsonSerializer.Serialize(response,
                    ResourceExtensions.JsonSerializerOptions)}\n```\n</details>",
                LoggingLevel.Debug
            );
        }

        return response.ToJsonContentBlock($"{baseUrl}/{response?.Data.Id}");
    }

    public static async Task<CallToolResult?> DeleteSimplicateResourceAsync(
        this IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string relativePath,
        string successText,
        CancellationToken cancellationToken = default)
    {
        var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
        var url = simplicateOptions.GetApiUrl(relativePath);

        var scraper = serviceProvider.GetServices<IContentScraper>()
                                     .OfType<SimplicateScraper>()
                                     .First();

        if (LoggingLevel.Debug.ShouldLog(requestContext.Server.LoggingLevel))
        {
            await requestContext.Server.SendMessageNotificationAsync(
                $"<details><summary>DELETE <code>{url}</code></summary></details>",
                LoggingLevel.Debug,
                cancellationToken: cancellationToken);
        }

        await scraper.DeleteContentAsync(serviceProvider, url, cancellationToken);
        return successText.ToTextCallToolResponse();
    }
}
