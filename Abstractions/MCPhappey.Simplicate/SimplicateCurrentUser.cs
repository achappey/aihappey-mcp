using System.ComponentModel;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Simplicate.Extensions;
using MCPhappey.Simplicate.Options;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Simplicate;

public static class SimplicateCurrentUser
{

    [Description("Get current user profile from Simplicate")]
    [McpServerTool(ReadOnly = true)]
    public static async Task<ContentBlock?> Simplicate_GetCurrentUser(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
    {
        using var graphClient = await serviceProvider.GetOboGraphClient(requestContext.Server);
        var currentUser = await graphClient.Me.GetAsync();

        var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();

        string employeeUrl = simplicateOptions.GetApiUrl("/hrm/employee");
        string employeeSelect = "id,name,employment_status,function,work_email,work_phone,hourly_sales_tariff";
        var employeeFilters = new List<string>
        {
            $"q[work_email]=*{currentUser?.Mail}",
            $"select={employeeSelect}"
        };

        var employeeFilterString = string.Join("&", employeeFilters);
        var employees = await downloadService.GetAllSimplicatePagesAsync<Employee>(
                  serviceProvider,
                  requestContext.Server,
                  employeeUrl,
                  employeeFilterString,
                  pageNum => $"Downloading employee",
                  requestContext,
                  cancellationToken: cancellationToken
              );

        var selectedId = employees.OfType<Employee>().FirstOrDefault();

        return selectedId.ToJsonContentBlock($"{employeeUrl}/{selectedId?.Id}");

    }

    public class Employee
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("function")]
        public string? Function { get; set; }

        [JsonPropertyName("employment_status")]
        public string? EmploymentStatus { get; set; }

        [JsonPropertyName("hourly_sales_tariff")]
        public string? HourlySalesTariff { get; set; }

        [JsonPropertyName("work_phone")]
        public string? WorkPhone { get; set; }

        [JsonPropertyName("work_email")]
        public string? WorkEmail { get; set; }

        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
    }
}

