using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Simplicate.Extensions;
using MCPhappey.Simplicate.Options;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Simplicate.Projects;

public static class SimplicateProjects
{

    [McpServerTool(OpenWorld = false, ReadOnly = true,
    Destructive = false,
    Name = "simplicate_projects_get_budget_totals_by_manager",
    Title = "Get project budget totals by project manager")]
    [Description("Returns, per project manager, the total budgeted, spent, and invoiced values aggregated across their projects.")]
    public static async Task<CallToolResult?> SimplicateProjects_GetBudgetTotalsByManager(
    IServiceProvider serviceProvider,
    RequestContext<CallToolRequestParams> requestContext,
    [Description("Optional project status label filter.")] ProjectStatusLabel? projectStatusLabel = null,
    CancellationToken cancellationToken = default)
    => await requestContext.WithStructuredContent(async () =>
{
    var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
    var downloadService = serviceProvider.GetRequiredService<DownloadService>();
    string baseUrl = simplicateOptions.GetApiUrl("/projects/project");
    string select = "name,project_manager.,budget.";
    var filters = new List<string>();

    if (projectStatusLabel.HasValue)
        filters.Add($"q[project_status.label]=*{Uri.EscapeDataString(projectStatusLabel.Value.ToString())}*");

    var filterString = string.Join("&", filters) + $"&select={select}";
    var projects = await downloadService.GetAllSimplicatePagesAsync<SimplicateProject>(
        serviceProvider,
        requestContext.Server,
        baseUrl,
        filterString,
        pageNum => $"Downloading project budgets",
        requestContext,
        cancellationToken: cancellationToken
    );

    return new
    {
        project_managers = projects
            .Where(p => p.ProjectManager?.Name != null)
            .GroupBy(p => p.ProjectManager!.Name)
            .Select(g => new
            {
                project_manager = g.Key,
                total_value_budget = g.Sum(p => p.Budget?.Total.ValueBudget ?? 0),
                total_value_spent = g.Sum(p => p.Budget?.Total.ValueSpent ?? 0),
                total_value_invoiced = g.Sum(p => p.Budget?.Total.ValueInvoiced ?? 0)
            })
            .OrderByDescending(x => x.total_value_budget)
    };
});


    [McpServerTool(OpenWorld = false, ReadOnly = true,
        Destructive = false,
        Name = "simplicate_projects_get_budget_totals_by_cost_type",
        Title = "Get project budget totals by cost type")]
    [Description("Returns aggregated totals for all projects, split into hours, costs, and total categories. Ideal for quick portfolio overviews.")]
    public static async Task<CallToolResult?> SimplicateProjects_GetBudgetTotalsByCostType(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithStructuredContent(async () =>
    {
        var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        string baseUrl = simplicateOptions.GetApiUrl("/projects/project");
        string select = "name,budget.";
        var projects = await downloadService.GetAllSimplicatePagesAsync<SimplicateProject>(
            serviceProvider,
            requestContext.Server,
            baseUrl,
            $"select={select}",
            pageNum => $"Downloading budgets",
            requestContext,
            cancellationToken: cancellationToken
        );

        return new
        {
            totals = new
            {
                hours_value_budget = projects.Sum(p => p.Budget?.Hours.ValueBudget ?? 0),
                hours_value_spent = projects.Sum(p => p.Budget?.Hours.ValueSpent ?? 0),
                costs_value_budget = projects.Sum(p => p.Budget?.Costs.ValueBudget ?? 0),
                costs_value_spent = projects.Sum(p => p.Budget?.Costs.ValueSpent ?? 0),
                total_value_budget = projects.Sum(p => p.Budget?.Total.ValueBudget ?? 0),
                total_value_spent = projects.Sum(p => p.Budget?.Total.ValueSpent ?? 0),
                total_value_invoiced = projects.Sum(p => p.Budget?.Total.ValueInvoiced ?? 0)
            }
        };
    });

    [McpServerTool(OpenWorld = false, ReadOnly = true,
        Destructive = false,
        Name = "simplicate_projects_get_budget_spent_vs_budget",
        Title = "Get project spent vs. budget ratios")]
    [Description("Returns, per project, how much of the budget has been spent (%). Useful for performance charts or burn-down visualizations.")]
    public static async Task<CallToolResult?> SimplicateProjects_GetBudgetSpentVsBudget(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithStructuredContent(async () =>
    {
        var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        string baseUrl = simplicateOptions.GetApiUrl("/projects/project");
        string select = "name,project_manager.,budget.total.";
        var projects = await downloadService.GetAllSimplicatePagesAsync<SimplicateProject>(
            serviceProvider,
            requestContext.Server,
            baseUrl,
            $"select={select}",
            pageNum => $"Downloading project budgets",
            requestContext,
            cancellationToken: cancellationToken
        );

        return new
        {
            projects = projects
                .Where(p => (p.Budget?.Total.ValueBudget ?? 0) > 0)
                .Select(p => new
                {
                    name = p.Name,
                    project_manager = p.ProjectManager?.Name,
                    value_budget = p.Budget!.Total.ValueBudget,
                    value_spent = p.Budget!.Total.ValueSpent,
                    spent_ratio = Math.Round(
                        p.Budget!.Total.ValueSpent / p.Budget!.Total.ValueBudget * 100, 1)
                })
                .OrderByDescending(x => x.spent_ratio)
        };
    });

    [Description("Create a new project in Simplicate")]
    [McpServerTool(OpenWorld = false, Title = "Create new project in Simplicate")]
    public static async Task<CallToolResult?> SimplicateProjects_CreateProject(
        [Description("Name of the new project")] string name,
        [Description("Id of the projectmanager")] string projectManagerId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Note")] string? note = null,
        [Description("Invoice reference")] string? invoiceReference = null,
        CancellationToken cancellationToken = default) => await serviceProvider.PostSimplicateResourceAsync(
        requestContext,
        "/projects/project",
        new SimplicateNewProject
        {
            Name = name,
            ProjectManagerId = projectManagerId,
            Note = note,
            InvoiceReference = invoiceReference
        },
        dto => new
        {
            name = dto.Name,
            project_manager_id = dto.ProjectManagerId,
            invoice_reference = dto.InvoiceReference,
            note = dto.Note,
        },
        cancellationToken
    );

    [Description("Update a project in Simplicate")]
    [McpServerTool(OpenWorld = false, Title = "Update project in Simplicate", Destructive = true)]
    public static async Task<CallToolResult?> SimplicateProjects_UpdateProject(
       [Description("Id of the project to update")] string projectId,
       IServiceProvider serviceProvider,
       RequestContext<CallToolRequestParams> requestContext,
       [Description("Name of the new project")] string name,
       [Description("Id of the projectmanager")] string projectManagerId,
       [Description("Note")] string? note = null,
       [Description("Invoice reference")] string? invoiceReference = null,
       CancellationToken cancellationToken = default) => await serviceProvider.PutSimplicateResourceMergedAsync(
       requestContext,
       "/projects/project/" + projectId,
       new SimplicateNewProject
       {
           Name = name,
           ProjectManagerId = projectManagerId,
           Note = note,
           InvoiceReference = invoiceReference
       },
       dto => new
       {
           name = dto.Name,
           project_manager_id = dto.ProjectManagerId,
           invoice_reference = dto.InvoiceReference,
           note = dto.Note,
       },
       cancellationToken
   );

    [Description("Create a new project service in Simplicate")]
    [McpServerTool(OpenWorld = false, Title = "Create new project service in Simplicate")]
    public static async Task<CallToolResult?> SimplicateProjects_CreateProjectService(
    [Description("Name of the new project service")] string name,
    [Description("Id of the project")] string projectId,
    IServiceProvider serviceProvider,
    RequestContext<CallToolRequestParams> requestContext,
    CancellationToken cancellationToken = default) => await serviceProvider.PostSimplicateResourceAsync(
            requestContext,
            "/projects/projectservice",
            new SimplicateNewProjectService
            {
                Name = name,
                ProjectId = projectId
            },
            cancellationToken
    );

    [Description("Add a project employee in Simplicate")]
    [McpServerTool(OpenWorld = false, Title = "Add a project employee in Simplicate")]
    public static async Task<CallToolResult?> SimplicateProjects_AddProjectEmployee(
      [Description("Id of the project")] string projectId,
      [Description("Id of the employee")] string employeeId,
      IServiceProvider serviceProvider,
      RequestContext<CallToolRequestParams> requestContext,
      CancellationToken cancellationToken = default) => await serviceProvider.PostSimplicateResourceAsync(
        requestContext,
        "/projects/projectemployee",
        new SimplicateAddProjectEmployee
        {
            ProjectId = projectId,
            EmployeeId = employeeId
        },
        cancellationToken
    );

    [Description("Get projects grouped by project manager filtered by my organization profile, optionally filtered by date (equal or greater than), project.")]
    [McpServerTool(OpenWorld = false,
        Title = "Get projects by project manager",
        ReadOnly = true)]
    public static async Task<CallToolResult?> SimplicateProjects_GetProjectNamesByProjectManager(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("My organization profile name of the project filter. Optional.")] string myOrganizationProfileName,
        [Description("End date for filtering (on or after), format yyyy-MM-dd. Optional.")] string? date = null,
        [Description("Project status label to filter by. Optional.")] ProjectStatusLabel? projectStatusLabel = null,
        CancellationToken cancellationToken = default) => await GetProjectsGroupedBy(
            serviceProvider,
            requestContext,
            x => x.ProjectManager?.Name,
            myOrganizationProfileName, date, projectStatusLabel, cancellationToken);

    private static async Task<CallToolResult> GetProjectsGroupedBy(
            IServiceProvider serviceProvider,
            RequestContext<CallToolRequestParams> requestContext,
            Func<SimplicateProject, string?> groupKeySelector,
            string? myOrganizationProfileName = null,
            string? date = null,
            ProjectStatusLabel? projectStatusLabel = null,
            CancellationToken cancellationToken = default)
            => await requestContext.WithStructuredContent(async () =>
    {
        if (
             string.IsNullOrWhiteSpace(myOrganizationProfileName)
            && string.IsNullOrWhiteSpace(date))
            throw new ArgumentException("At least one filter (managerName, date, myOrganizationProfileName) must be provided.");

        var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();

        string baseUrl = simplicateOptions.GetApiUrl("/projects/project");
        string select = "project_manager.,name";
        var filters = new List<string>();

        if (!string.IsNullOrWhiteSpace(date))
            filters.Add($"q[end_date][ge]={Uri.EscapeDataString(date)}");

        if (!string.IsNullOrWhiteSpace(myOrganizationProfileName)) filters.Add($"q[my_organization_profile.organization.name]=*{Uri.EscapeDataString(myOrganizationProfileName)}*");
        if (projectStatusLabel.HasValue) filters.Add($"q[project_status.label]=*{Uri.EscapeDataString(projectStatusLabel.Value.ToString())}*");

        var filterString = string.Join("&", filters) + $"&select={select}";

        var hours = await downloadService.GetAllSimplicatePagesAsync<SimplicateProject>(
            serviceProvider,
            requestContext.Server,
            baseUrl,
            filterString,
            pageNum => $"Downloading projects",
            requestContext,
            cancellationToken: cancellationToken
        );

        return hours
            .GroupBy(x => groupKeySelector(x) ?? string.Empty)
            .ToDictionary(
                g => g.Key,
                g => g.Select(t => t.Name)) ?? [];
    });

    [Description("Please fill in the project employee details")]
    public class SimplicateAddProjectEmployee
    {
        [JsonPropertyName("project_id")]
        [Required]
        [Description("The id of the project.")]
        public string? ProjectId { get; set; }

        [JsonPropertyName("employee_id")]
        [Required]
        [Description("The id of the employee.")]
        public string? EmployeeId { get; set; }
    }

    [Description("Please fill in the project service details")]
    public class SimplicateNewProjectService
    {
        [JsonPropertyName("name")]
        [Required]
        [Description("The name of the project service.")]
        public string? Name { get; set; }

        [JsonPropertyName("project_id")]
        [Required]
        [Description("The id of the project.")]
        public string? ProjectId { get; set; }

        [JsonPropertyName("track_hours")]
        [Required]
        [DefaultValue(true)]
        [Description("Track project service hours.")]
        public bool? TrackHours { get; set; } = true;

        [JsonPropertyName("track_cost")]
        [Required]
        [DefaultValue(true)]
        [Description("Track project service costs.")]
        public bool? TrackCost { get; set; } = true;

        [JsonPropertyName("vat_class_id")]
        [Required]
        [Description("Id of the vat class.")]
        public string VatClassId { get; set; } = default!;

        [JsonPropertyName("start_date")]
        [Description("Start date")]
        public DateTime? StartDate { get; set; }

        [JsonPropertyName("end_date")]
        [Description("End date.")]
        public DateTime? EndDate { get; set; }
    }

    [Description("Please fill in the project details")]
    public class SimplicateNewProject
    {
        [JsonPropertyName("name")]
        [Required]
        [Description("The name of the project.")]
        public string? Name { get; set; }

        [JsonPropertyName("project_manager_id")]
        [Required]
        [Description("The id of the project manager.")]
        public string? ProjectManagerId { get; set; }

        [JsonPropertyName("note")]
        [Description("Note.")]
        public string? Note { get; set; }

        [JsonPropertyName("invoice_reference")]
        [Description("Invoice reference.")]
        public string? InvoiceReference { get; set; }
    }


    public enum ProjectStatusLabel
    {
        active,
        closed
    }

    public class SimplicateProject
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("project_manager")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public SimplicateProjectManager? ProjectManager { get; set; }

        [JsonPropertyName("budget")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public SimplicateProjectBudget? Budget { get; set; }

    }

    public class SimplicateProjectManager
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    public class SimplicateProjectBudget
    {
        [JsonPropertyName("hours")]
        public BudgetHours Hours { get; set; } = new();

        [JsonPropertyName("costs")]
        public BudgetCosts Costs { get; set; } = new();

        [JsonPropertyName("total")]
        public BudgetTotal Total { get; set; } = new();
    }

    public class BudgetHours
    {
        [JsonPropertyName("amount_budget")]
        public decimal AmountBudget { get; set; }

        [JsonPropertyName("amount_spent")]
        public decimal AmountSpent { get; set; }

        [JsonPropertyName("value_budget")]
        public decimal ValueBudget { get; set; }

        [JsonPropertyName("value_spent")]
        public decimal ValueSpent { get; set; }
    }

    public class BudgetCosts
    {
        [JsonPropertyName("value_budget")]
        public decimal ValueBudget { get; set; }

        [JsonPropertyName("value_spent")]
        public decimal ValueSpent { get; set; }
    }

    public class BudgetTotal
    {
        [JsonPropertyName("value_budget")]
        public decimal ValueBudget { get; set; }

        [JsonPropertyName("value_spent")]
        public decimal ValueSpent { get; set; }

        [JsonPropertyName("value_invoiced")]
        public decimal ValueInvoiced { get; set; }
    }


}

