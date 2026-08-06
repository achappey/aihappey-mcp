using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
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

    [McpServerTool(OpenWorld = false,
       ReadOnly = true,
       Destructive = false,
       UseStructuredContent = true,
       OutputSchemaType = typeof(SimplicateData<SimplicateProject>),
       Name = "simplicate_projects_get_projects",
       Title = "Get Simplicate projects")]
    [Description("Returns projects with optional filters.")]
    public static async Task<CallToolResult?> SimplicateProjects_GetProjects(
       IServiceProvider serviceProvider,
       RequestContext<CallToolRequestParams> requestContext,
       [Description("Optional project status label filter.")] ProjectStatusLabel? projectStatusLabel = null,
       [Description("Optional project name filter.")] string? projectName = null,
       [Description("Optional project manager name filter.")] string? projectManagerName = null,
       CancellationToken cancellationToken = default)
       => await ModelContextToolExtensions.WithExceptionCheck(async ()
       => await requestContext.WithStructuredContent(async () =>
   {
       var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
       var downloadService = serviceProvider.GetRequiredService<DownloadService>();
       string baseUrl = simplicateOptions.GetApiUrl("/projects/project");
       var filters = new List<string>();

       if (projectStatusLabel.HasValue)
           filters.Add($"q[project_status.label]=*{Uri.EscapeDataString(projectStatusLabel.Value.ToString())}*");

       if (!string.IsNullOrEmpty(projectName))
           filters.Add($"q[name]=*{Uri.EscapeDataString(projectName.ToString())}*");

       if (!string.IsNullOrEmpty(projectManagerName))
           filters.Add($"q[project_manager.name]=*{Uri.EscapeDataString(projectManagerName)}*");

       var filterString = string.Join("&", filters);
       var projects = await downloadService.GetAllSimplicatePagesAsync<SimplicateProject>(
           serviceProvider,
           requestContext.Server,
           baseUrl,
           filterString,
           pageNum => $"Downloading projects",
           requestContext,
           cancellationToken: cancellationToken
       );

       return new SimplicateData<SimplicateProject>()
       {
           Data = projects
       };
   }));

    [Description("Create a new project in Simplicate")]
    [McpServerTool(OpenWorld = false, Title = "Create new project in Simplicate")]
    public static async Task<CallToolResult?> SimplicateProjects_CreateProject(
        [Description("Name of the new project")] string name,
        [Description("Id of the projectmanager")] string projectManagerId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Note")] string? note = null,
        [Description("Invoice reference")] string? invoiceReference = null,
        CancellationToken cancellationToken = default)
        => await serviceProvider.PostSimplicateResourceAsync(
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
       CancellationToken cancellationToken = default)
       => await serviceProvider.PutSimplicateResourceMergedAsync(
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
    CancellationToken cancellationToken = default)
    => await serviceProvider.PostSimplicateResourceAsync(
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
      CancellationToken cancellationToken = default)
      => await serviceProvider.PostSimplicateResourceAsync(
        requestContext,
        "/projects/projectemployee",
        new SimplicateAddProjectEmployee
        {
            ProjectId = projectId,
            EmployeeId = employeeId
        },
        cancellationToken
    );

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
        [JsonPropertyName("id")]
        public string Id { get; set; } = null!;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("project_manager")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public SimplicateProjectManager? ProjectManager { get; set; }

        [JsonPropertyName("budget")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public SimplicateProjectBudget? Budget { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement> AdditionalProperties { get; set; } = [];

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

