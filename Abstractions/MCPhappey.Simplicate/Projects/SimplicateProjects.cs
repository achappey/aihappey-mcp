using System.ComponentModel;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Simplicate.Extensions;
using MCPhappey.Simplicate.Options;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Simplicate.Projects;

public static partial class SimplicateProjects
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
        [Description("The limit of max allowed results.")] int? limit = null,
        [Description("The offset to search from.")] int? offset = null,
       CancellationToken cancellationToken = default)
       => await ModelContextToolExtensions.WithExceptionCheck(async ()
       => await requestContext.WithStructuredContent(async () =>
   {
       var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
       var downloadService = serviceProvider.GetRequiredService<DownloadService>();
       string baseUrl = simplicateOptions.GetApiUrl("/projects/project");
       var filters = new List<string>();
       if (limit.HasValue) filters.Add($"limit={limit}");
       if (offset.HasValue) filters.Add($"offset={offset}");
       if (projectStatusLabel.HasValue)
           filters.Add($"q[project_status.label]=*{Uri.EscapeDataString(projectStatusLabel.Value.ToString())}*");

       if (!string.IsNullOrEmpty(projectName))
           filters.Add($"q[name]=*{Uri.EscapeDataString(projectName.ToString())}*");

       if (!string.IsNullOrEmpty(projectManagerName))
           filters.Add($"q[project_manager.name]=*{Uri.EscapeDataString(projectManagerName)}*");


       var filterString = string.Join("&", filters);

       if (limit.HasValue && limit.Value <= 100)
           return await downloadService.GetSimplicatePageAsync<SimplicateProject>(
               serviceProvider,
               requestContext.Server,
               $"{baseUrl}?{filterString}&metadata=count",
               cancellationToken: cancellationToken
           );

       var items = await downloadService.GetAllSimplicatePagesAsync<SimplicateProject>(
           serviceProvider,
           requestContext.Server,
           baseUrl,
           filterString,
           pageNum => $"Downloading projects (page {pageNum})",
           requestContext,
           cancellationToken: cancellationToken
       );

       return new SimplicateData<SimplicateProject>()
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

